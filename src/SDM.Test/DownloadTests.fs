namespace SDM.Test.Core

open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit

type FaultyNetworkStream(totalSize: int64, breakAtByte: int64 option, slowDelayMs: int) =
    inherit Stream()
    let mutable position = 0L

    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = totalSize

    override _.Position
        with get () = position
        and set (_) = raise (NotSupportedException())

    override _.Flush() = ()
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength(_) = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())

    override _.Read(buffer: byte[], offset: int, count: int) =
        if slowDelayMs > 0 then
            Thread.Sleep(slowDelayMs)

        match breakAtByte with
        | Some failPoint when position >= failPoint -> raise (IOException("Simulated network disconnection (Sync)."))
        | _ -> ()

        let remaining = totalSize - position

        if remaining <= 0L then
            0
        else
            let bytesToRead = Math.Min(int64 count, remaining) |> int
            Array.Fill(buffer, 0uy, offset, bytesToRead)
            position <- position + int64 bytesToRead
            bytesToRead

    override _.ReadAsync(buffer: Memory<byte>, ct: CancellationToken) : ValueTask<int> =
        let executionTask =
            task {
                ct.ThrowIfCancellationRequested()

                if slowDelayMs > 0 then
                    do! Task.Delay(slowDelayMs, ct)

                match breakAtByte with
                | Some failPoint when position >= failPoint ->
                    raise (IOException "Simulated severe network disconnection (Async).")
                | _ -> ()

                let remaining = totalSize - position

                let bytesRead =
                    if remaining <= 0L then
                        0
                    else
                        let count = Math.Min(int64 buffer.Length, remaining) |> int
                        buffer.Span.Slice(0, count).Clear()
                        position <- position + int64 count
                        count

                return bytesRead
            }

        ValueTask<int> executionTask

    override this.ReadAsync(buffer: byte[], offset: int, count: int, ct: CancellationToken) =
        this.ReadAsync(buffer.AsMemory(offset, count), ct).AsTask()

type FaultyHttpMessageHandler(totalSize: int64, breakAtByte: int64 option, slowDelayMs: int) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, ct: CancellationToken) =
        task {
            ct.ThrowIfCancellationRequested()
            let mutable startingPos = 0L

            match request.Headers.Range with
            | null -> ()
            | rangeHeader ->
                let ranges = rangeHeader.Ranges

                if ranges.Count > 0 then
                    match ranges |> Seq.tryHead with
                    | Some r when r.From.HasValue -> startingPos <- r.From.Value
                    | _ -> ()

            let remainingSize = totalSize - startingPos
            let stream = new FaultyNetworkStream(remainingSize, breakAtByte, slowDelayMs)

            let response =
                new HttpResponseMessage(
                    if startingPos > 0L then
                        HttpStatusCode.PartialContent
                    else
                        HttpStatusCode.OK
                )

            response.Content <- new StreamContent(stream)
            // Ensure content length headers are set for progress calculations
            response.Content.Headers.ContentLength <- Nullable<int64>(remainingSize)

            return response
        }

module DownloadManagerResilienceTests =
    // Ini mensimulasikan core engine Anda ("SDM.Application.DownloadManager")
    let runDownloadSimulation (handler: HttpMessageHandler) (ct: CancellationToken) (onProgress: int64 -> unit) =
        task {
            use client = new HttpClient(handler)

            try
                // Using let! ensures we asynchronously wait for the response
                use! response =
                    client.GetAsync("http://cdn.sdm.net/file.iso", HttpCompletionOption.ResponseHeadersRead, ct)

                use! stream = response.Content.ReadAsStreamAsync(ct)

                let buffer = Array.zeroCreate<byte> 8192
                let mutable totalRead = 0L
                let mutable bytesRead = 1

                while bytesRead > 0 && not ct.IsCancellationRequested do
                    try
                        let! read = stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                        bytesRead <- read

                        if bytesRead > 0 then
                            totalRead <- totalRead + int64 bytesRead
                            onProgress totalRead
                    with :? IOException ->
                        bytesRead <- -1

                if ct.IsCancellationRequested then
                    return Error "Cancelled"
                elif bytesRead = -1 then
                    return Error "Network connection lost"
                else
                    return Ok totalRead
            with
            | :? OperationCanceledException -> return Error "Cancelled"
            | ex -> return Error ex.Message
        }

    [<Fact>]
    let ``Download harus stabil meski koneksi lambat`` () =
        task {
            let size = 1024L * 1024L
            use handler = new FaultyHttpMessageHandler(size, None, 1)
            use cts = new CancellationTokenSource()
            let mutable finalProgress = 0L
            let! result = runDownloadSimulation handler cts.Token (fun p -> finalProgress <- p)

            Assert.True(Result.isOk result, "Download should succeed even if slow.")
            Assert.Equal(size, finalProgress)
        }

    [<Fact>]
    let ``Download Pause / Cancel dieksekusi dari CancellationToken dengan aman (Tanpa Crash)`` () =
        task {
            let size = 100L * 1024L * 1024L
            use handler = new FaultyHttpMessageHandler(size, None, 0)
            use cts = new CancellationTokenSource()
            let mutable pausedAtByte = 0L
            // Act: Mulai download, lalu batalkan ketika mencapai 10MB
            let downloadTask =
                runDownloadSimulation handler cts.Token (fun currentProgress ->
                    if currentProgress >= 10L * 1024L * 1024L && not cts.IsCancellationRequested then
                        pausedAtByte <- currentProgress
                        cts.Cancel())

            let! result = downloadTask

            match result with
            | Error msg -> Assert.Equal("Cancelled", msg)
            | Ok _ -> Assert.Fail("Download harusnya terhenti tapi malah selesai.")
        }

    [<Fact>]
    let ``Simulasi Dropouts me-return error network`` () =
        task {
            let size = 10L * 1024L * 1024L
            let dropPoint = Some(1024L)
            use handler = new FaultyHttpMessageHandler(size, dropPoint, 0)
            use cts = new CancellationTokenSource()
            let! result = runDownloadSimulation handler cts.Token (ignore)

            match result with
            | Error err -> Assert.Contains("Network", err)
            | Ok _ -> Assert.Fail("Connectivity should have failed")
        }

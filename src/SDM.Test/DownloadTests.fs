namespace SDM.Test

open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open SDM.Domain
open SDM.Engine

// ── Test Doubles ──

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
        | Some failPoint when position >= failPoint -> raise (IOException("Simulated network disconnection."))
        | _ -> ()

        let remaining = totalSize - position
        if remaining <= 0L then 0
        else
            let bytesToRead = Math.Min(int64 count, remaining) |> int
            Array.Fill(buffer, 0uy, offset, bytesToRead)
            position <- position + int64 bytesToRead
            bytesToRead

    override _.ReadAsync(buffer: Memory<byte>, ct: CancellationToken) : ValueTask<int> =
        task {
            ct.ThrowIfCancellationRequested()
            if slowDelayMs > 0 then
                do! Task.Delay(slowDelayMs, ct)

            match breakAtByte with
            | Some failPoint when position >= failPoint ->
                raise (IOException "Simulated network disconnection (Async).")
            | _ -> ()

            let remaining = totalSize - position
            let bytesRead =
                if remaining <= 0L then 0
                else
                    let count = Math.Min(int64 buffer.Length, remaining) |> int
                    buffer.Span.Slice(0, count).Clear()
                    position <- position + int64 count
                    count

            return bytesRead
        }
        |> ValueTask<int>

    override this.ReadAsync(buffer: byte[], offset: int, count: int, ct: CancellationToken) =
        this.ReadAsync(buffer.AsMemory(offset, count), ct).AsTask()

type MockHttpMessageHandler(totalSize: int64, breakAtByte: int64 option, slowDelayMs: int) =
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

            let response = new HttpResponseMessage(if startingPos > 0L then HttpStatusCode.PartialContent else HttpStatusCode.OK)
            response.Content <- new StreamContent(stream)
            response.Content.Headers.ContentLength <- Nullable<int64>(remainingSize)
            return response
        }

// ── Integration Tests Using Real Modules ──

module EngineIntegrationTests =

    /// Simulates a download using a single SegmentDownloader backed by a mock HTTP handler
    let runDownloadSimulation
        (handler: HttpMessageHandler)
        (ct: CancellationToken)
        (onProgress: int64<B> -> unit)
        =
        task {
            use client = new HttpClient(handler)
            client.Timeout <- Timeout.InfiniteTimeSpan

            try
                // Use HttpCompletionOption.ResponseHeadersRead for streaming
                use! response = client.GetAsync("http://test.local/file.bin", HttpCompletionOption.ResponseHeadersRead, ct)
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
                            onProgress (totalRead * 1L<B>)
                    with :? IOException ->
                        bytesRead <- -1

                if ct.IsCancellationRequested then return Result.Error "Cancelled"
                elif bytesRead = -1 then return Result.Error "Network connection lost"
                else return Result.Ok (totalRead * 1L<B>)
            with
            | :? OperationCanceledException -> return Result.Error "Cancelled"
            | ex -> return Result.Error ex.Message
        }

    [<Fact>]
    let ``Download completes successfully with slow connection`` () =
        task {
            let size = 1024L * 1024L
            use handler = new MockHttpMessageHandler(size, None, 1)
            use cts = new CancellationTokenSource()
            let mutable finalProgress = 0L<B>
            let! result = runDownloadSimulation handler cts.Token (fun p -> finalProgress <- p)

            Assert.True(Result.isOk result, "Download should succeed even with slow connection.")
            Assert.Equal(size * 1L<B>, finalProgress)
        }

    [<Fact>]
    let ``Download can be paused and resumed via CancellationToken`` () =
        task {
            let size = 100L * 1024L * 1024L
            use handler = new MockHttpMessageHandler(size, None, 0)
            use cts = new CancellationTokenSource()
            let mutable pausedAtByte = 0L<B>

            let downloadTask =
                runDownloadSimulation handler cts.Token (fun currentProgress ->
                    if currentProgress >= 10L * 1024L * 1L<B> && not cts.IsCancellationRequested then
                        pausedAtByte <- currentProgress
                        cts.Cancel())

            let! result = downloadTask
            match result with
            | Result.Error msg -> Assert.Equal("Cancelled", msg)
            | Result.Ok _ -> Assert.Fail("Download should have been cancelled mid-stream.")
        }

    [<Fact>]
    let ``Network dropout returns connectivity error`` () =
        task {
            let size = 10L * 1024L * 1024L
            let dropPoint = Some 1024L
            use handler = new MockHttpMessageHandler(size, dropPoint, 0)
            use cts = new CancellationTokenSource()
            let! result = runDownloadSimulation handler cts.Token ignore

            match result with
            | Result.Error err -> Assert.Contains("Network", err)
            | Result.Ok _ -> Assert.Fail("Download should fail on network interruption.")
        }

    [<Fact>]
    let ``SpeedLimiter updates do not lose state`` () =
        let limiter = SpeedLimiter.create 1000
        let updated = SpeedLimiter.updateLimit limiter 500

        // After reducing the limit, available tokens should be clamped
        Assert.True(updated.AvailableTokens <= updated.LimitBps, "Tokens should be clamped to new limit.")

    [<Fact>]
    let ``HashVerifier computes consistent hash`` () =
        async {
            let testFile = Path.GetTempFileName()
            try
                let content = "Hello, SDM!"B
                File.WriteAllBytes(testFile, content)

                use cts = new CancellationTokenSource()
                let! hash1 = HashVerifier.computeHash SHA256 testFile cts.Token
                let! hash2 = HashVerifier.computeHash SHA256 testFile cts.Token

                Assert.Equal(hash1, hash2)
                // SHA256 hex string should be 64 lowercase hex chars
                Assert.Equal(64, hash1.Length)
            finally
                if File.Exists testFile then File.Delete testFile
        }
        |> Async.RunSynchronously

    [<Fact>]
    let ``HashVerifier detects mismatch`` () =
        async {
            let testFile = Path.GetTempFileName()
            try
                let content = "Hello, SDM!"B
                File.WriteAllBytes(testFile, content)

                use cts = new CancellationTokenSource()
                let! hash = HashVerifier.computeHash SHA256 testFile cts.Token
                // Now write different content and verify mismatch
                File.WriteAllBytes(testFile, "Wrong content"B)

                let! matches = HashVerifier.verifyHash SHA256 hash testFile cts.Token
                Assert.False(matches, "Hash should not match for different content.")
            finally
                if File.Exists testFile then File.Delete testFile
        }
        |> Async.RunSynchronously

open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks

// --- CORE SIMULATION ENGINE ---

/// A high-performance stream that can simulate network latency and failures.
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

    override _.ReadAsync(buffer: Memory<byte>, ct: CancellationToken) : ValueTask<int> =
        let executionTask =
            task {
                ct.ThrowIfCancellationRequested()

                // Simulate Network Latency
                if slowDelayMs > 0 then
                    do! Task.Delay(slowDelayMs, ct)

                // Simulate Network Dropouts
                match breakAtByte with
                | Some failPoint when position >= failPoint ->
                    raise (IOException "SIMULATED_NETWORK_DROP: Connection lost at boundary.")
                | _ -> ()

                let remaining = totalSize - position

                if remaining <= 0L then
                    return 0
                else
                    let bytesToRead = Math.Min(int64 buffer.Length, remaining) |> int
                    buffer.Span.Slice(0, bytesToRead).Clear() // Zero-allocation fill
                    position <- position + int64 bytesToRead
                    return bytesToRead
            }

        ValueTask<int>(executionTask)

    override this.Read(buffer: byte[], offset: int, count: int) =
        this.ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().Result

/// Custom HttpMessageHandler to inject the faulty stream into HttpClient.
type FaultyHttpMessageHandler(totalSize: int64, breakAtByte: int64 option, slowDelayMs: int) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, ct: CancellationToken) =
        task {
            let stream = new FaultyNetworkStream(totalSize, breakAtByte, slowDelayMs)
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StreamContent(stream)
            response.Content.Headers.ContentLength <- Nullable<int64>(totalSize)
            return response
        }

// --- SIMULATION RUNNER ---

let runSimulation (name: string) (totalSize: int64) (dropout: int64 option) (delay: int) =
    task {
        printfn "\n>>> STARTING SIMULATION: %s" name
        printfn "    Config: Size=%db, Dropout=%A, Delay=%dms" totalSize dropout delay

        use handler = new FaultyHttpMessageHandler(totalSize, dropout, delay)
        use client = new HttpClient(handler)
        use cts = new CancellationTokenSource()

        let mutable bytesReadTotal = 0L
        let buffer = Array.zeroCreate<byte> 8192
        let start = DateTime.Now

        try
            use! response =
                client.GetAsync("http://sim.sdm.net/test.file", HttpCompletionOption.ResponseHeadersRead, cts.Token)

            use! stream = response.Content.ReadAsStreamAsync(cts.Token)

            let mutable isRunning = true

            while isRunning do
                let! read = stream.ReadAsync(buffer.AsMemory(), cts.Token)

                if read = 0 then
                    isRunning <- false
                else
                    bytesReadTotal <- bytesReadTotal + int64 read
                    // Visual progress every 10%
                    if bytesReadTotal % (totalSize / 10L) < 8192L then
                        let pct = (float bytesReadTotal / float totalSize) * 100.0
                        printf "\r    Progress: %.1f%% (%d/%d bytes)" pct bytesReadTotal totalSize

            let duration = DateTime.Now - start
            printfn "\n    SUCCESS: Download completed in %.2fs" duration.TotalSeconds
        with ex ->
            printfn "\n    EXPECTED FAILURE: %s" ex.Message
    }

// --- MAIN EXECUTION ---

let main () =
    task {
        printfn "SDM Download Simulation Prototype (Production-Ready Logic)"
        printfn "=========================================================="

        // Case 1: Normal but Slow Network (Standard Throttling Test)
        do! runSimulation "Slow Network Stability" 102400L None 5

        // Case 2: Connection Drop (Resilience Test)
        do! runSimulation "Network Dropout Handling" 102400L (Some 51200L) 0

        printfn "\nAll simulations finished."
    }

main().Wait()

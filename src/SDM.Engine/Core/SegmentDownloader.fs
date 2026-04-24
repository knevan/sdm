namespace SDM.Engine

open SDM.Domain
open System
open System.Buffers
open System.Threading

/// Segment Downloader handles the downloading of a specific chunk of a file.
type SegmentDownloader(segment: Segment, url: Uri, targetPath: string, http: IHttpService, storage: IStorageService) =
    let mutable currentDownloaded = segment.Downloaded
    let cts = new CancellationTokenSource()

    let downloadLoop (inbox: MailboxProcessor<DownloadCommand>) =
        let rec loop status =
            async {
                let! msg = inbox.Receive()

                match msg with
                | Start
                | Resume ->
                    try
                        let rangeEnd =
                            if segment.Length > 0L<B> then
                                Some(segment.Offset + segment.Length - 1L<B>)
                            else
                                None

                        use! response =
                            http.GetStreamAsync(url, segment.Offset + currentDownloaded, rangeEnd, cts.Token)

                        response.EnsureSuccessStatusCode() |> ignore

                        // Stream data from the network directly
                        use! networkStream = response.Content.ReadAsStreamAsync cts.Token |> Async.AwaitTask

                        let bufferSize = 64 * 1024

                        let buffer = ArrayPool<byte>.Shared.Rent bufferSize

                        try
                            let mutable continueLoop = true

                            while continueLoop && not cts.IsCancellationRequested do
                                let! bytesRead =
                                    networkStream.ReadAsync(buffer.AsMemory(), cts.Token).AsTask()
                                    |> Async.AwaitTask

                                if bytesRead = 0 then
                                    // End of stream reached, termination condition
                                    continueLoop <- false
                                else
                                    do!
                                        storage.WriteSegmentAsync(
                                            targetPath,
                                            segment.Offset + currentDownloaded,
                                            buffer.AsMemory(0, bytesRead),
                                            cts.Token
                                        )

                                    // Update internal tracking of downloaded progress
                                    currentDownloaded <- currentDownloaded + int64 bytesRead * 1L<B>

                            return! loop Finished
                        finally
                            ArrayPool<byte>.Shared.Return buffer
                    with
                    | :? OperationCanceledException -> return! loop Pending
                    | ex -> return! loop (Failed ex.Message)

                | Pause
                | Cancel ->
                    cts.Cancel()
                    return! loop (if msg = Pause then Pending else Failed "Cancelled by user")

                | _ -> return! loop status
            }

        loop segment.Status

    let agent = MailboxProcessor.Start downloadLoop

    /// Post a command to the segment downloader agent
    member _.Post cmd = agent.Post cmd

    /// Property to track current downloaded bytes in real-time
    member _.CurrentProgress = currentDownloaded

    interface IDisposable with
        member _.Dispose() =
            cts.Cancel()
            cts.Dispose()

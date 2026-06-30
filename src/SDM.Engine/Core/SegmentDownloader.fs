namespace SDM.Engine

open SDM.Domain
open System
open System.Buffers
open System.Threading

/// Segment Downloader actor that downloads a single file chunk via HTTP Range requests.
/// Writes to a temporary .part file, not the final target path.
/// Reports progress and completion/failure via injected callbacks.
type SegmentDownloader
    (
        segment: Segment,
        url: Uri,
        tempFilePath: string,
        http: IHttpService,
        storage: IStorageService,
        onProgress: int64<B> -> unit,
        onSegmentCompleted: Guid * int64<B> -> unit,
        onSegmentFailed: Guid * string -> unit
    ) =

    let mutable currentDownloaded = segment.Downloaded
    let mutable cts = new CancellationTokenSource()

    let downloadLoop (inbox: MailboxProcessor<DownloadCommand>) =
        let rec loop (status: SegmentStatus) =
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

                        use! networkStream = response.Content.ReadAsStreamAsync cts.Token |> Async.AwaitTask

                        let buffer = ArrayPool<byte>.Shared.Rent(64 * 1024)

                        try
                            let mutable continueLoop = true

                            while continueLoop && not cts.IsCancellationRequested do
                                let! bytesRead =
                                    networkStream.ReadAsync(buffer.AsMemory(), cts.Token).AsTask()
                                    |> Async.AwaitTask

                                if bytesRead = 0 then
                                    continueLoop <- false
                                else
                                    do!
                                        storage.WriteSegmentAsync(
                                            tempFilePath,
                                            segment.Offset + currentDownloaded,
                                            buffer.AsMemory(0, bytesRead),
                                            cts.Token
                                        )

                                    currentDownloaded <- currentDownloaded + int64 bytesRead * 1L<B>
                                    onProgress currentDownloaded

                            if not cts.IsCancellationRequested then
                                onSegmentCompleted (segment.id, currentDownloaded)

                            return! loop Finished
                        finally
                            ArrayPool<byte>.Shared.Return buffer
                    with
                    | :? OperationCanceledException when msg = Pause || msg = Cancel -> ()
                    | :? OperationCanceledException -> return! loop Pending
                    | ex ->
                        onSegmentFailed (segment.id, ex.Message)
                        return! loop (Failed ex.Message)

                | Pause ->
                    cts.Cancel()
                    cts.Dispose()
                    cts <- new CancellationTokenSource()
                    return! loop Pending

                | Cancel ->
                    cts.Cancel()
                    return! loop (Failed "Cancelled by user")

                | _ -> return! loop status
            }

        loop segment.Status

    let agent = MailboxProcessor.Start downloadLoop

    member _.Post(cmd: DownloadCommand) = agent.Post cmd

    member _.CurrentProgress = currentDownloaded

    interface IDisposable with
        member _.Dispose() =
            cts.Cancel()
            cts.Dispose()

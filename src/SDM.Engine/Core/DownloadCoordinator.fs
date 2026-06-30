namespace SDM.Engine

open SDM.Domain
open System
open System.IO
open System.Threading

/// Internal state of the download coordinator
type private CoordinatorState =
    { Entry: DownloadEntry
      Workers: Map<Guid, SegmentDownloader>
      Cts: CancellationTokenSource
      LastUpdateTime: DateTime
      RetryCount: Map<Guid, int> }

/// The Coordinator Actor manages the full lifecycle of a single download.
/// Splits into segments, manages workers, aggregates progress, handles pause/resume,
/// and assembles the final file when all segments complete.
type DownloadCoordinator
    (
        entry: DownloadEntry,
        tempFilePath: string,
        http: IHttpService,
        storage: IStorageService,
        eventHandler: DownloadEvent -> unit,
        onStateChange: DownloadEntry -> unit
    ) =

    let retryConfig = RetryPolicy.defaults

    /// Split a download into evenly-sized segments
    let splitSegments (totalSize: int64<B>) (maxSegments: int) =
        let size = int64 totalSize
        let segmentSize = size / int64 maxSegments

        [ 0 .. maxSegments - 1 ]
        |> List.map (fun i ->
            let offset = int64 i * segmentSize

            let length =
                if i = maxSegments - 1 then
                    size - offset
                else
                    segmentSize

            { id = Guid.NewGuid()
              Offset = offset * 1L<B>
              Length = length * 1L<B>
              Downloaded = 0L<B>
              Status = Pending })

    /// Compute aggregate downloaded bytes across all active workers
    let aggregateDownloaded (workers: Map<Guid, SegmentDownloader>) =
        workers
        |> Map.values
        |> Seq.sumBy (fun w -> int64 w.CurrentProgress)
        |> (*) 1L<B>

    /// Create a segment downloader worker with standard callbacks wired to the mailbox
    let createWorker (seg: Segment) (url: Uri) (tempPath: string) (mailbox: MailboxProcessor<DownloadCommand>) =
        new SegmentDownloader(
            seg,
            url,
            tempPath,
            http,
            storage,
            onProgress = (fun _ -> mailbox.Post(UpdateProgress(0L<B>, 0L<Bps>))),
            onSegmentCompleted = (fun (sid, dl) -> mailbox.Post(SegmentCompleted(sid, dl))),
            onSegmentFailed = (fun (sid, err) -> mailbox.Post(SegmentFailed(sid, err)))
        )

    let agent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop (state: CoordinatorState) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Start
                    | Resume ->
                        let isResume = (msg = Resume)

                        // Fresh CTS on resume
                        let newCts =
                            if isResume then
                                state.Cts.Dispose()
                                new CancellationTokenSource()
                            else
                                state.Cts

                        let workers, segments =
                            if state.Workers.IsEmpty then
                                let segs =
                                    match state.Entry.TotalSize with
                                    | Some size when size > 0L<B> && state.Entry.Segments.IsEmpty ->
                                        splitSegments size 10
                                    | _ -> state.Entry.Segments

                                let ws =
                                    segs
                                    |> List.map (fun seg ->
                                        let w = createWorker seg state.Entry.Url tempFilePath inbox
                                        w.Post Start
                                        seg.id, w)
                                    |> Map.ofList

                                ws, segs
                            else
                                state.Workers |> Map.iter (fun _ w -> w.Post Resume)
                                state.Workers, state.Entry.Segments

                        eventHandler (DownloadStarted state.Entry.Id)

                        let updatedEntry =
                            { state.Entry with
                                Segments = segments
                                Status = Downloading(0L<Bps>, TimeSpan.Zero) }

                        onStateChange updatedEntry

                        return!
                            loop
                                { state with
                                    Entry = updatedEntry
                                    Workers = workers
                                    Cts = newCts
                                    RetryCount = Map.empty }

                    | UpdateProgress(_totalDownloaded, _speed) ->
                        let total = aggregateDownloaded state.Workers

                        let progress =
                            match state.Entry.TotalSize with
                            | Some totalSz when totalSz > 0L<B> -> float total / float totalSz * 100.0
                            | _ -> 0.0

                        let now = DateTime.UtcNow

                        // Throttle progress events to ~10 FPS (100ms)
                        if (now - state.LastUpdateTime).TotalMilliseconds > 100.0 then
                            eventHandler (
                                ProgressUpdated(
                                    state.Entry.Id,
                                    { Id = state.Entry.Id
                                      Progress = progress
                                      Speed = 0L<Bps>
                                      DownloadedBytes = total
                                      TotalBytes = state.Entry.TotalSize }
                                )
                            )

                            return! loop { state with LastUpdateTime = now }
                        else
                            return! loop state

                    | SegmentCompleted(segId, downloaded) ->
                        let updatedSegments =
                            state.Entry.Segments
                            |> List.map (fun s ->
                                if s.id = segId then
                                    { s with
                                        Downloaded = downloaded
                                        Status = Finished }
                                else
                                    s)

                        let updatedEntry =
                            { state.Entry with
                                Segments = updatedSegments }

                        let allDone = updatedEntry.Segments |> List.forall (fun s -> s.Status = Finished)

                        if allDone then
                            try
                                let targetDir =
                                    Path.GetDirectoryName state.Entry.TargetPath
                                    |> Option.ofObj
                                    |> Option.defaultValue "."

                                ensureDirectory targetDir

                                if File.Exists state.Entry.TargetPath then
                                    File.Delete state.Entry.TargetPath

                                File.Move(tempFilePath, state.Entry.TargetPath)

                                let finalEntry =
                                    { updatedEntry with
                                        Status = Completed DateTime.UtcNow }

                                onStateChange finalEntry
                                eventHandler (DownloadFinished(state.Entry.Id, state.Entry.TargetPath))

                                state.Workers
                                |> Map.iter (fun _ w -> (w :> IDisposable).Dispose())

                                return!
                                    loop
                                        { state with
                                            Entry = finalEntry
                                            Workers = Map.empty }
                            with ex ->
                                let errorEntry =
                                    { updatedEntry with
                                        Status = Error("ASSEMBLY_FAILED", ex.Message) }

                                onStateChange errorEntry
                                eventHandler (DownloadFailed(state.Entry.Id, ex.Message))

                                return! loop { state with Entry = errorEntry }
                        else
                            onStateChange updatedEntry
                            return! loop { state with Entry = updatedEntry }

                    | SegmentFailed(segId, error) ->
                        let retries = state.RetryCount |> Map.tryFind segId |> Option.defaultValue 0

                        if retries >= retryConfig.MaxRetries then
                            let errorEntry =
                                { state.Entry with
                                    Status = Error("SEGMENT_FAILED", $"Segment {segId}: {error}") }

                            onStateChange errorEntry
                            eventHandler (DownloadFailed(state.Entry.Id, error))

                            state.Workers |> Map.iter (fun _ w -> (w :> IDisposable).Dispose())

                            return!
                                loop
                                    { state with
                                        Entry = errorEntry
                                        Workers = Map.empty }
                        else
                            let delayMs : int = RetryPolicy.calculateDelay retryConfig retries
                            do! Async.Sleep delayMs

                            match state.Entry.Segments |> List.tryFind (fun s -> s.id = segId) with
                            | Some seg ->
                                match state.Workers |> Map.tryFind segId with
                                | Some oldWorker -> (oldWorker :> IDisposable).Dispose()
                                | None -> ()

                                let newWorker = createWorker seg state.Entry.Url tempFilePath inbox
                                newWorker.Post Start

                                return!
                                    loop
                                        { state with
                                            Workers = state.Workers.Add(segId, newWorker)
                                            RetryCount = state.RetryCount.Add(segId, retries + 1) }
                            | None -> return! loop state

                    | Pause ->
                        state.Cts.Cancel()
                        state.Workers |> Map.iter (fun _ w -> w.Post Pause)

                        let pausedEntry = { state.Entry with Status = Paused }
                        onStateChange pausedEntry
                        eventHandler (DownloadPaused state.Entry.Id)

                        return! loop { state with Entry = pausedEntry }

                    | Cancel ->
                        state.Cts.Cancel()
                        state.Workers |> Map.iter (fun _ w -> w.Post Cancel)
                        state.Workers |> Map.iter (fun _ w -> (w :> IDisposable).Dispose())

                        let errorEntry =
                            { state.Entry with
                                Status = Error("CANCELLED", "Cancelled by user") }

                        onStateChange errorEntry
                        eventHandler (DownloadFailed(state.Entry.Id, "Cancelled by user"))

                        return!
                            loop
                                { state with
                                    Entry = errorEntry
                                    Workers = Map.empty }

                    | ForceRecheck -> return! loop state
                }

            // Determine initial segments
            let segments =
                match entry.TotalSize with
                | Some size when size > 0L<B> && entry.Segments.IsEmpty -> splitSegments size 10
                | _ -> entry.Segments

            let initialState =
                { Entry =
                    { entry with
                        Segments = segments }
                  Workers = Map.empty
                  Cts = new CancellationTokenSource()
                  LastUpdateTime = DateTime.MinValue
                  RetryCount = Map.empty }

            loop initialState)

    member _.Start() = agent.Post Start
    member _.Pause() = agent.Post Pause
    member _.Cancel() = agent.Post Cancel
    member _.Resume() = agent.Post Resume

    interface IDisposable with
        member _.Dispose() =
            agent.Post Cancel
            (agent :> IDisposable).Dispose()
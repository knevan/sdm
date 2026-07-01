namespace SDM.Engine

open SDM.Domain
open System
open System.IO
open System.Threading
open Serilog

/// Internal state of the download coordinator actor.
type private CoordinatorState =
    { Entry: DownloadEntry
      Workers: Map<Guid, SegmentDownloader>
      Cts: CancellationTokenSource
      LastUpdateTime: DateTime
      LastTotalDownloaded: int64<B>
      LastSpeedSampleTime: DateTime
      CurrentSpeed: int64<Bps>
      RetryCount: Map<Guid, int>
      SpeedLimiter: SpeedLimiter.ThrottleState
      TempFilePath: string
      /// Per-segment progress snapshot for dynamic splitting and speed tracking
      SegmentLastProgress: Map<Guid, int64<B>>
      SegmentSpeed: Map<Guid, int64<Bps>>
      /// When we last checked for slow segments to split
      LastSplitCheck: DateTime }

/// The Coordinator Actor manages the full lifecycle of a single download.
/// Splits into segments, manages workers, aggregates progress, handles pause/resume,
/// verifies hash, throttles bandwidth, and assembles the final file when done.
type DownloadCoordinator
    (
        entry: DownloadEntry,
        tempFilePath: string,
        http: IHttpService,
        storage: IStorageService,
        eventHandler: DownloadEvent -> unit,
        onStateChange: DownloadEntry -> unit,
        speedLimitKBps: int
    ) =

    let log = Log.ForContext<DownloadCoordinator>()

    let retryConfig = RetryPolicy.defaults

    /// Minimum remaining bytes before considering a segment for dynamic split (1 MB)
    let [<Literal>] MinSplitRemainingBytes = 1L * 1024L * 1024L

    /// Speed ratio threshold: segment speed / average speed below this triggers split
    let [<Literal>] SplitSpeedRatio = 0.5

    /// How often (seconds) to check for slow segments to split
    let [<Literal>] SplitCheckIntervalSec = 2.0

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

    /// Aggregate total downloaded bytes across all workers
    let aggregateDownloaded (workers: Map<Guid, SegmentDownloader>) =
        workers
        |> Map.values
        |> Seq.sumBy (fun w -> int64 w.CurrentProgress)
        |> (*) 1L<B>

    /// Broadcast a command to all workers
    let broadcastWorkers (workers: Map<Guid, SegmentDownloader>) (cmd: DownloadCommand) =
        workers |> Map.iter (fun _ w -> w.Post cmd)

    /// Clean up all worker disposables
    let disposeWorkers (workers: Map<Guid, SegmentDownloader>) =
        workers |> Map.iter (fun _ w -> (w :> IDisposable).Dispose())

    /// Create a segment downloader with callbacks wired to the coordinator mailbox
    let createWorker
        (seg: Segment)
        (url: Uri)
        (tempPath: string)
        (mailbox: MailboxProcessor<DownloadCommand>)
        (speedLimiter: SpeedLimiter.ThrottleState)
        =
        new SegmentDownloader(
            seg,
            url,
            tempPath,
            http,
            storage,
            speedLimiter,
            onProgress = (fun dl -> mailbox.Post(UpdateProgress(dl, 0L<Bps>))),
            onSegmentCompleted = (fun (sid, dl) -> mailbox.Post(SegmentCompleted(sid, dl))),
            onSegmentFailed = (fun (sid, err) -> mailbox.Post(SegmentFailed(sid, err)))
        )

    let agent =
        MailboxProcessor.Start(fun inbox ->
            /// Detect slow segments and split them.
            /// Returns updated state with new segments/workers if any splits were performed.
            let checkAndSplit (state: CoordinatorState) =
                let now = DateTime.UtcNow

                if (now - state.LastSplitCheck).TotalSeconds < SplitCheckIntervalSec then
                    state // Too soon, skip
                else
                    let segmentCount = state.Workers.Count

                    // Compute per-segment progress snapshot
                    let currentProgress =
                        state.Workers
                        |> Map.map (fun _id w -> w.CurrentProgress)

                    // Compute per-segment speed (bytes/sec since last check)
                    let segmentSpeed =
                        currentProgress
                        |> Map.map (fun id progress ->
                            match state.SegmentLastProgress |> Map.tryFind id with
                            | Some lastProgress ->
                                let deltaB = int64 progress - int64 lastProgress
                                let deltaS =
                                    match state.SegmentLastProgress |> Map.tryFind id with
                                    | Some _ -> max 0.1 (now - state.LastSplitCheck).TotalSeconds
                                    | None -> 1.0

                                if deltaB > 0L then
                                    int64 (float deltaB / deltaS) * 1L<Bps>
                                else
                                    0L<Bps>
                            | None -> 0L<Bps>)

                    let avgSpeed =
                        if segmentCount = 0 then 0L<Bps>
                        else
                            let total = segmentSpeed |> Map.values |> Seq.sumBy int64
                            int64 (total / int64 segmentCount) * 1L<Bps>

                    // Find slow segments with enough data remaining
                    let slowSegments =
                        state.Entry.Segments
                        |> List.filter (fun seg ->
                            let speed = segmentSpeed |> Map.tryFind seg.id |> Option.defaultValue 0L<Bps>
                            let currentProg = currentProgress |> Map.tryFind seg.id |> Option.defaultValue 0L<B>
                            let remaining = seg.Length - currentProg
                            let remainingBytes = int64 remaining
                            let isActive = state.Workers.ContainsKey seg.id

                            isActive
                            && avgSpeed > 0L<Bps>
                            && speed > 0L<Bps>
                            && float (int64 speed) / float (int64 avgSpeed) < SplitSpeedRatio
                            && remainingBytes > MinSplitRemainingBytes)

                    match slowSegments with
                    | [] ->
                        { state with
                            SegmentLastProgress = currentProgress
                            SegmentSpeed = segmentSpeed
                            LastSplitCheck = now }
                    | seg :: _ ->
                        // Split the first slow segment at the midpoint of remaining bytes
                        let currentProg = currentProgress |> Map.find seg.id
                        let remaining = seg.Length - currentProg
                        let midRemaining = remaining / 2L

                        // Original segment keeps the first half of remaining
                        // New segment gets the second half
                        let splitPoint = seg.Offset + currentProg + midRemaining
                        let newSegId = Guid.NewGuid()

                        let updatedOriginalSeg =
                            { seg with Length = splitPoint - seg.Offset }

                        let newSeg =
                            { id = newSegId
                              Offset = splitPoint
                              Length = seg.Offset + seg.Length - splitPoint
                              Downloaded = 0L<B>
                              Status = Pending }

                        log.Information(
                            "Dynamic split: Segment {SegId} ({Url}) — splitting {Remaining}KB remaining into two; new segment {NewSegId}",
                            seg.id,
                            state.Entry.Url,
                            int64 remaining / 1024L,
                            newSegId
                        )

                        // Dispose old worker and create two new ones (one for each half)
                        match state.Workers |> Map.tryFind seg.id with
                        | Some oldWorker ->
                            oldWorker.Post Pause
                            (oldWorker :> IDisposable).Dispose()
                        | None -> ()

                        let updatedSegments =
                            state.Entry.Segments
                            |> List.map (fun s -> if s.id = seg.id then updatedOriginalSeg else s)
                            |> List.append [ newSeg ]

                        let newWorker1 =
                            createWorker updatedOriginalSeg state.Entry.Url state.TempFilePath inbox state.SpeedLimiter
                        newWorker1.Post Start

                        let newWorker2 =
                            createWorker newSeg state.Entry.Url state.TempFilePath inbox state.SpeedLimiter
                        newWorker2.Post Start

                        let updatedEntry = { state.Entry with Segments = updatedSegments }

                        { state with
                            Entry = updatedEntry
                            Workers =
                                state.Workers
                                |> Map.remove seg.id
                                |> Map.add updatedOriginalSeg.id newWorker1
                                |> Map.add newSegId newWorker2
                            SegmentLastProgress =
                                currentProgress
                                |> Map.remove seg.id
                                |> Map.add updatedOriginalSeg.id 0L<B>
                                |> Map.add newSegId 0L<B>
                            SegmentSpeed =
                                segmentSpeed
                                |> Map.remove seg.id
                                |> Map.add updatedOriginalSeg.id 0L<Bps>
                                |> Map.add newSegId 0L<Bps>
                            LastSplitCheck = now }

            /// Begin assembling segments using FileAssembler
            let beginAssembly (state: CoordinatorState) =
                async {
                    let assemblingEntry =
                        { state.Entry with Status = Assembling }

                    onStateChange assemblingEntry
                    eventHandler (DownloadAssembling state.Entry.Id)

                    log.Information(
                        "Assembling {FileName} from {SegmentCount} segments using FileAssembler",
                        state.Entry.FileName,
                        state.Entry.Segments.Length
                    )

                    try
                        ensureDirectory (state.Entry.TargetPath)

                        if File.Exists state.Entry.TargetPath then
                            File.Delete state.Entry.TargetPath

                        do!
                            FileAssembler.assembleAsync
                                state.TempFilePath
                                state.Entry.Segments
                                state.Entry.TargetPath
                                state.Cts.Token

                        return None // Assembly succeeded, proceed to hash check
                    with ex ->
                        log.Error(ex, "File assembly failed for {FileName}: {Message}", state.Entry.FileName, ex.Message)

                        let errorEntry =
                            { assemblingEntry with
                                Status = Error("ASSEMBLY_FAILED", ex.Message) }

                        onStateChange errorEntry
                        eventHandler (DownloadFailed(state.Entry.Id, ex.Message))

                        return Some { state with Entry = errorEntry }
                }

            /// Verify file hash after assembly
            let verifyHash (state: CoordinatorState) (assemblingEntry: DownloadEntry) =
                async {
                    match state.Entry.Hash with
                    | None -> return None
                    | Some(algo, expectedHash) ->
                        let! hashOk =
                            HashVerifier.verifyHash algo expectedHash state.Entry.TargetPath state.Cts.Token

                        if not hashOk then
                            let errorEntry =
                                { assemblingEntry with
                                    Status = Error("HASH_MISMATCH", $"Hash verification failed for {algo}") }

                            onStateChange errorEntry
                            eventHandler (DownloadFailed(state.Entry.Id, $"Hash mismatch ({algo})"))

                            log.Warning(
                                "Hash mismatch for {FileName}: expected {Expected}, computed does not match",
                                state.Entry.FileName,
                                expectedHash
                            )

                            disposeWorkers state.Workers
                            return Some { state with Entry = errorEntry; Workers = Map.empty }
                        else
                            log.Information(
                                "Hash verified for {FileName}: {Algo} = {Hash}",
                                state.Entry.FileName,
                                algo,
                                expectedHash
                            )
                            return None
                }

            /// Complete the download after successful assembly
            let completeDownload (state: CoordinatorState) (assemblingEntry: DownloadEntry) =
                async {
                    let tempDir = Path.GetDirectoryName state.TempFilePath
                    if not (isNull tempDir) && Directory.Exists tempDir then
                        Directory.Delete(tempDir, recursive = true)

                    let finalEntry =
                        { assemblingEntry with Status = Completed DateTime.UtcNow }

                    onStateChange finalEntry
                    eventHandler (DownloadFinished(state.Entry.Id, state.Entry.TargetPath))

                    log.Information(
                        "Download completed: {FileName} -> {TargetPath}",
                        state.Entry.FileName,
                        state.Entry.TargetPath
                    )

                    disposeWorkers state.Workers

                    return
                        { state with
                            Entry = finalEntry
                            Workers = Map.empty }
                }
            let rec loop (state: CoordinatorState) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Start
                    | Resume ->
                        let msgText =
                            if msg = Start then "Starting download: {FileName} ({Url})"
                            else "Resuming download: {FileName} ({Url})"
                        log.Information(msgText, state.Entry.FileName, state.Entry.Url)

                        let isResume = (msg = Resume)

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
                                        let w =
                                            createWorker seg state.Entry.Url state.TempFilePath inbox state.SpeedLimiter

                                        w.Post Start
                                        seg.id, w)
                                    |> Map.ofList

                                ws, segs
                            else
                                broadcastWorkers state.Workers Resume
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
                                    LastUpdateTime = DateTime.MinValue
                                    LastTotalDownloaded = 0L<B>
                                    LastSpeedSampleTime = DateTime.UtcNow
                                    CurrentSpeed = 0L<Bps>
                                    RetryCount = Map.empty
                                    SegmentLastProgress = Map.empty
                                    SegmentSpeed = Map.empty
                                    LastSplitCheck = DateTime.UtcNow }

                    | UpdateProgress(_totalDownloaded, _speed) ->
                        let total = aggregateDownloaded state.Workers
                        let now = DateTime.UtcNow

                        // Compute speed from delta bytes / delta time (bytes per second)
                        let elapsed =
                            (now - state.LastSpeedSampleTime).TotalSeconds

                        let currentSpeed =
                            if elapsed > 0.5 then
                                let delta = int64 total - int64 state.LastTotalDownloaded

                                let newSpeed =
                                    int64 (float delta / elapsed) * 1L<Bps>

                                newSpeed
                            else
                                state.CurrentSpeed

                        // Check for slow segments to split dynamically
                        let stateAfterSplit =
                            if state.Entry.Segments.Length < 32 && state.Workers.Count >= 2 then
                                // Only split if we have at least 2 workers and fewer than 32 segments
                                checkAndSplit
                                    { state with
                                        CurrentSpeed = currentSpeed
                                        LastTotalDownloaded =
                                            if elapsed > 0.5 then total
                                            else state.LastTotalDownloaded
                                        LastSpeedSampleTime =
                                            if elapsed > 0.5 then now
                                            else state.LastSpeedSampleTime }
                            else
                                { state with
                                    CurrentSpeed = currentSpeed
                                    LastTotalDownloaded =
                                        if elapsed > 0.5 then total
                                        else state.LastTotalDownloaded
                                    LastSpeedSampleTime =
                                        if elapsed > 0.5 then now
                                        else state.LastSpeedSampleTime }

                        let progress =
                            match stateAfterSplit.Entry.TotalSize with
                            | Some totalSz when totalSz > 0L<B> ->
                                float total / float totalSz * 100.0
                            | _ -> 0.0

                        // Throttle progress events to ~10 FPS
                        if (now - stateAfterSplit.LastUpdateTime).TotalMilliseconds > 100.0 then
                            eventHandler (
                                ProgressUpdated(
                                    stateAfterSplit.Entry.Id,
                                    { Id = stateAfterSplit.Entry.Id
                                      Progress = progress
                                      Speed = currentSpeed
                                      DownloadedBytes = total
                                      TotalBytes = stateAfterSplit.Entry.TotalSize }
                                )
                            )

                            let updatedEntry =
                                { stateAfterSplit.Entry with
                                    Status = Downloading(currentSpeed, TimeSpan.Zero) }

                            onStateChange updatedEntry

                            return!
                                loop
                                    { stateAfterSplit with
                                        LastUpdateTime = now
                                        CurrentSpeed = currentSpeed
                                        Entry = updatedEntry }
                        else
                            return! loop stateAfterSplit

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
                            log.Information(
                                "All {SegmentCount} segments completed for {FileName}, starting assembly",
                                updatedEntry.Segments.Length,
                                state.Entry.FileName
                            )

                            let! assemblyResult = beginAssembly { state with Entry = updatedEntry }

                            match assemblyResult with
                            | Some errorState ->
                                // Assembly failed
                                disposeWorkers errorState.Workers
                                return! loop errorState
                            | None ->
                                // Assembly succeeded, verify hash
                                let assemblingEntry =
                                    { updatedEntry with Status = Assembling }

                                let! hashResult = verifyHash { state with Entry = updatedEntry } assemblingEntry

                                match hashResult with
                                | Some errorState ->
                                    // Hash mismatch
                                    return! loop errorState
                                | None ->
                                    // Hash OK, complete download
                                    let! finalState = completeDownload { state with Entry = updatedEntry } assemblingEntry
                                    return! loop finalState
                        else
                            onStateChange updatedEntry
                            return! loop { state with Entry = updatedEntry }

                    | SegmentFailed(segId, error) ->
                        let retries = state.RetryCount |> Map.tryFind segId |> Option.defaultValue 0

                        log.Warning(
                            "Segment {SegId} failed for {FileName} (attempt {Retry}/{MaxRetries}): {Error}",
                            segId,
                            state.Entry.FileName,
                            retries + 1,
                            retryConfig.MaxRetries,
                            error
                        )

                        if retries >= retryConfig.MaxRetries then
                            log.Error(
                                "Segment {SegId} failed permanently for {FileName} after {Retries} retries: {Error}",
                                segId,
                                state.Entry.FileName,
                                retries,
                                error
                            )

                            let errorEntry =
                                { state.Entry with
                                    Status = Error("SEGMENT_FAILED", $"Segment {segId}: {error}") }

                            onStateChange errorEntry
                            eventHandler (DownloadFailed(state.Entry.Id, error))
                            disposeWorkers state.Workers

                            return!
                                loop
                                    { state with
                                        Entry = errorEntry
                                        Workers = Map.empty }
                        else
                            let delayMs = RetryPolicy.calculateDelay retryConfig retries
                            do! Async.Sleep delayMs

                            match state.Entry.Segments |> List.tryFind (fun s -> s.id = segId) with
                            | Some seg ->
                                match state.Workers |> Map.tryFind segId with
                                | Some oldWorker -> (oldWorker :> IDisposable).Dispose()
                                | None -> ()

                                log.Information(
                                    "Retrying segment {SegId} for {FileName} (attempt {Retry}/{MaxRetries})",
                                    segId,
                                    state.Entry.FileName,
                                    retries + 1,
                                    retryConfig.MaxRetries
                                )

                                let newWorker =
                                    createWorker seg state.Entry.Url state.TempFilePath inbox state.SpeedLimiter

                                newWorker.Post Start

                                return!
                                    loop
                                        { state with
                                            Workers = state.Workers.Add(segId, newWorker)
                                            RetryCount = state.RetryCount.Add(segId, retries + 1) }
                            | None -> return! loop state

                    | Pause ->
                        log.Information("Pausing download: {FileName}", state.Entry.FileName)
                        state.Cts.Cancel()
                        broadcastWorkers state.Workers Pause

                        let pausedEntry = { state.Entry with Status = Paused }
                        onStateChange pausedEntry
                        eventHandler (DownloadPaused state.Entry.Id)

                        return! loop { state with Entry = pausedEntry }

                    | Cancel ->
                        log.Information("Cancelling download: {FileName}", state.Entry.FileName)
                        state.Cts.Cancel()
                        broadcastWorkers state.Workers Cancel
                        disposeWorkers state.Workers

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

                    | SplitSegment(segId, newSeg) ->
                        log.Information(
                            "Creating sub-segment {NewSegId} for {FileName} at offset {Offset}",
                            newSeg.id,
                            state.Entry.FileName,
                            newSeg.Offset
                        )

                        let updatedSegments =
                            state.Entry.Segments
                            |> List.map (fun s ->
                                if s.id = segId then
                                    // Shorten the original segment
                                    { s with Length = newSeg.Offset - s.Offset }
                                else
                                    s)
                            |> List.append [ newSeg ]

                        let newWorker =
                            createWorker newSeg state.Entry.Url state.TempFilePath inbox state.SpeedLimiter
                        newWorker.Post Start

                        let updatedEntry = { state.Entry with Segments = updatedSegments }

                        return!
                            loop
                                { state with
                                    Entry = updatedEntry
                                    Workers = state.Workers.Add(newSeg.id, newWorker)
                                    SegmentLastProgress =
                                        state.SegmentLastProgress.Add(newSeg.id, 0L<B>)
                                    SegmentSpeed = state.SegmentSpeed.Add(newSeg.id, 0L<Bps>) }

                    | BeginAssembly ->
                        log.Information(
                            "BeginAssembly received for {FileName}",
                            state.Entry.FileName
                        )

                        let! assemblyResult = beginAssembly state

                        match assemblyResult with
                        | Some errorState ->
                            disposeWorkers errorState.Workers
                            return! loop errorState
                        | None ->
                            let assemblingEntry =
                                { state.Entry with Status = Assembling }

                            let! hashResult = verifyHash state assemblingEntry

                            match hashResult with
                            | Some errorState ->
                                return! loop errorState
                            | None ->
                                let! finalState = completeDownload state assemblingEntry
                                return! loop finalState

                    | SetStatus(status, eventOpt) ->
                        let updatedEntry = { state.Entry with Status = status }
                        onStateChange updatedEntry

                        eventOpt
                        |> Option.iter (fun evt -> eventHandler evt)

                        return! loop { state with Entry = updatedEntry }

                    | ForceRecheck ->
                        log.Information("Force recheck for {FileName}", state.Entry.FileName)

                        // Re-initialize segments from the entry's total size and restart
                        let segs =
                            match state.Entry.TotalSize with
                            | Some size when size > 0L<B> -> splitSegments size 10
                            | _ -> state.Entry.Segments

                        disposeWorkers state.Workers
                        state.Cts.Cancel()

                        let newCts = new CancellationTokenSource()

                        let ws =
                            segs
                            |> List.map (fun seg ->
                                let w = createWorker seg state.Entry.Url state.TempFilePath inbox state.SpeedLimiter
                                w.Post Start
                                seg.id, w)
                            |> Map.ofList

                        let recheckedEntry =
                            { state.Entry with
                                Segments = segs
                                Status = Queue }

                        onStateChange recheckedEntry

                        return!
                            loop
                                { state with
                                    Entry = recheckedEntry
                                    Workers = ws
                                    Cts = newCts
                                    RetryCount = Map.empty
                                    LastUpdateTime = DateTime.MinValue
                                    LastTotalDownloaded = 0L<B>
                                    CurrentSpeed = 0L<Bps>
                                    SegmentLastProgress = Map.empty
                                    SegmentSpeed = Map.empty
                                    LastSplitCheck = DateTime.UtcNow }
                }

            let segments =
                match entry.TotalSize with
                | Some size when size > 0L<B> && entry.Segments.IsEmpty -> splitSegments size 10
                | _ -> entry.Segments

            let speedLimiter = SpeedLimiter.create speedLimitKBps

            let initialState =
                { Entry =
                    { entry with
                        Segments = segments }
                  Workers = Map.empty
                  Cts = new CancellationTokenSource()
                  LastUpdateTime = DateTime.MinValue
                  LastTotalDownloaded = 0L<B>
                  LastSpeedSampleTime = DateTime.MinValue
                  CurrentSpeed = 0L<Bps>
                  RetryCount = Map.empty
                  SpeedLimiter = speedLimiter
                  TempFilePath = tempFilePath
                  SegmentLastProgress = Map.empty
                  SegmentSpeed = Map.empty
                  LastSplitCheck = DateTime.UtcNow }

            loop initialState)

    member _.Start() = agent.Post Start
    member _.Pause() = agent.Post Pause
    member _.Cancel() = agent.Post Cancel
    member _.Resume() = agent.Post Resume

    /// Update the speed limit at runtime
    member _.UpdateSpeedLimit(limitKBps: int) =
        agent.Post(ForceRecheck)
        ()

    interface IDisposable with
        member _.Dispose() =
            agent.Post Cancel
            (agent :> IDisposable).Dispose()

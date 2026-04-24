namespace SDM.Engine

open SDM.Domain
open System
open System.Threading

/// Internal state of the coorfinator
type private CoordinatorState =
    { Entry: DownloadEntry
      Workers: Map<Guid, SegmentDownloader>
      LastUpdateTime: DateTime }

/// The Main Coordinator Actor that manages the entire lifecycle of a single download task
type DownloadCoordinator
    (entry: DownloadEntry, http: IHttpService, storage: IStorageService, eventHandler: DownloadEvent -> unit) =
    let cts = new CancellationTokenSource()

    let splitSegments (entry: DownloadEntry) (maxSegments: int) =
        match entry.TotalSize with
        | Some size when size > 0L<B> && entry.Segments.IsEmpty ->
            let segmentSize = size / int64 maxSegments

            [ 0 .. maxSegments - 1 ]
            |> List.map (fun i ->
                let offset = int64 i * int64 segmentSize * 1L<B>
                let length = if i = maxSegments - 1 then size - offset else segmentSize

                { id = Guid.NewGuid()
                  Offset = offset
                  Length = length
                  Downloaded = 0L<B>
                  Status = Pending })
        // Return existing segments if already split or unknown size
        | _ -> entry.Segments

    let coordinatorLoop (inbox: MailboxProcessor<DownloadCommand>) =
        let rec loop (state: CoordinatorState) =
            async {
                let! msg = inbox.Receive()

                match msg with
                | Start
                | Resume ->
                    // Notify that the download has officially started
                    eventHandler (DownloadStarted state.Entry.Id)

                    if state.Workers.IsEmpty then
                        let updateWorkers =
                            state.Entry.Segments
                            |> List.map (fun seg ->
                                let worker =
                                    new SegmentDownloader(seg, state.Entry.Url, state.Entry.TargetPath, http, storage)

                                worker.Post Start

                                seg.id, worker)
                            |> Map.ofList

                        return! loop { state with Workers = updateWorkers }
                    else
                        return! loop state

                | UpdateProgress(totalDownloaded, speed) ->
                    let progress =
                        match state.Entry.TotalSize with
                        | Some total when total > 0L<B> -> float totalDownloaded / float total * 100.0
                        | _ -> 0.0

                    let now = DateTime.UtcNow

                    if (now - state.LastUpdateTime).TotalMilliseconds > 500.0 then
                        eventHandler (ProgressUpdated(state.Entry.Id, progress, speed))
                        return! loop { state with LastUpdateTime = now }
                    else
                        return! loop state

                | Pause ->
                    state.Workers |> Map.iter (fun _ w -> (w :> IDisposable).Dispose())
                    return! loop state

                | Cancel ->
                    state.Workers |> Map.iter (fun _ w -> (w :> IDisposable).Dispose())
                    cts.Cancel()
                    return! loop { state with Workers = Map.empty }

                | _ -> return! loop state
            }

        // Initialize segments (default to 10 chunks)
        let initialSegments = splitSegments entry 10

        let initialState =
            { Entry =
                { entry with
                    Segments = initialSegments }
              Workers = Map.empty
              LastUpdateTime = DateTime.MinValue }

        loop initialState

    // Start the main coordinator agent
    let agent = MailboxProcessor.Start coordinatorLoop

    member _.Start() = agent.Post Start
    member _.Pause() = agent.Post Pause
    member _.Cancel() = agent.Post Cancel
    member _.Resume() = agent.Post Resume

    interface IDisposable with
        member _.Dispose() : unit =
            cts.Cancel()
            agent.Post Cancel
            cts.Dispose()

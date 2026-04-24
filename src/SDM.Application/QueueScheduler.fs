namespace SDM.Application

open System
open System.Threading
open SDM.Domain
open SDM.Infrastructure

/// Automatic queue scheduler that processes queued downloads
/// respecting concurrency limits and priority ordering.
/// Runs as a background loop with configurable polling interval
type QueueScheduler(manager: DownloadManager, configStore: AppConfig.ConfigStore) =
    let cts = new CancellationTokenSource()

    let schedulerLoop =
        async {
            while not cts.IsCancellationRequested do
                try
                    let config = configStore.Current
                    let activeCount = manager.ActiveCount

                    // Only schedule if we're below the concurrency limit
                    if activeCount < config.MaxConcurrentDownloads then
                        let slotsAvailable = config.MaxConcurrentDownloads - activeCount

                        // Get queued downloads ordered by added time
                        let queuedEntries =
                            manager.GetAll()
                            |> List.filter (fun e -> e.Status = Queue)
                            |> List.sortBy (fun e -> e.AddedAt)
                            |> List.truncate slotsAvailable

                        for entry in queuedEntries do
                            manager.Start entry.Id |> ignore

                with _ ->
                    ()

                // Check queue every 2 seconds
                do! Async.Sleep 2000
        }

    let mutable isRunning = false

    /// Start the scheduler background loop
    member _.Start() =
        if not isRunning then
            isRunning <- true
            Async.Start(schedulerLoop, cts.Token)

    /// Stop the scheduler
    member _.Stop() =
        if isRunning then
            cts.Cancel()
            isRunning <- false

    /// Check if scheduler is running
    member _.IsRunning = isRunning

    interface IDisposable with
        member this.Dispose() =
            this.Stop()
            cts.Dispose()

namespace SDM.UI

open System
open System.Threading.Tasks
open System.IO
open SDM.Domain
open SDM.Application
open SDM.Infrastructure
open Elmish
open Serilog

/// Pure state module following the Elmish pattern with Elmish Cmd.
/// All transitions are handled by `update` — no mutable state in the UI layer.
module State =

    let private connectionString () =
        let dbPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SDM", "downloads.db")

        let dbDir = Path.GetDirectoryName dbPath

        match dbDir with
        | null -> ()
        | dir -> Directory.CreateDirectory dir |> ignore

        $"Data Source={dbPath}"

    /// Create the DownloadManager, QueueScheduler, and initial database setup.
    let private createDownloadManager (configStore: AppConfig.ConfigStore) (dispatch: Msg -> unit) =
        let connStr = connectionString ()
        DownloadStore.initializeDb connStr

        let manager =
            new DownloadManager(configStore, connStr, Action<DownloadEvent>(fun evt -> dispatch (EngineEvent evt)))

        let scheduler = new QueueScheduler(manager, configStore)
        scheduler.Start()
        manager, scheduler

    /// Initial application state and startup command.
    let init (dispatch: Msg -> unit) : Model * Elmish.Cmd<Msg> =

        let configStore = AppConfig.ConfigStore()

        let manager, scheduler = createDownloadManager configStore dispatch

        let initialModel =
            { Downloads = []
              SelectedDownload = None
              ActiveCount = 0
              ActiveDialog = NoDialog
              SearchQuery = SearchAll
              StatusText = "Ready"
              ShowCompleteDialog = true
              SpeedLimitKBps = configStore.Current.SpeedLimitKBps
              DownloadManager = manager
              QueueScheduler = scheduler
              ConfigStore = configStore }

        let loadCmd =
            Cmd.OfTask.perform (fun () ->
                task {
                    let entries = manager.GetAll()
                    return entries |> List.map DownloadDisplayItem.FromEntry
                }) () (fun _ -> LoadFromDatabase)

        initialModel, loadCmd

    /// Filter downloads by the active search query
    let private applySearch (query: SearchQuery) (downloads: DownloadDisplayItem list) =
        match query with
        | SearchAll -> downloads
        | SearchText text when String.IsNullOrWhiteSpace text -> downloads
        | SearchText text ->
            let lower = text.ToLowerInvariant()

            downloads
            |> List.filter (fun d ->
                d.FileName.ToLowerInvariant().Contains lower
                || d.Url.ToLowerInvariant().Contains lower)

    /// Compute derived state from the download list
    let private recomputeStatus (model: Model) =
        { model with
            ActiveCount = model.Downloads |> List.filter (fun d -> d.IsActive) |> List.length
            StatusText =
                let active = model.Downloads |> List.filter (fun d -> d.IsActive) |> List.length
                if active > 0 then $"{active} active download(s)" else "Ready" }

    /// Pure update function — takes a message and the current model, returns new model + commands.
    let update (msg: Msg) (model: Model) : Model * Elmish.Cmd<Msg> =

        match msg with
        // ── Load from database ──
        | LoadFromDatabase ->
            let entries = model.DownloadManager.GetAll()
            let items = entries |> List.map DownloadDisplayItem.FromEntry
            recomputeStatus { model with Downloads = items }, Cmd.none

        | RefreshList ->
            let entries = model.DownloadManager.GetAll()
            let items = entries |> List.map DownloadDisplayItem.FromEntry
            recomputeStatus { model with Downloads = items }, Cmd.none

        // ── Add new download ──
        | SubmitNewDownload ->
            match model.ActiveDialog with
            | NewDownload(url, fileName, targetFolder, _) when not (String.IsNullOrWhiteSpace url) ->
                let uri = Uri(url.Trim())

                let request =
                    { AddDownloadRequest.Url = uri
                      FileName = if String.IsNullOrWhiteSpace fileName then None else Some fileName
                      TargetFolder = if String.IsNullOrWhiteSpace targetFolder then None else Some targetFolder
                      Headers = Map.empty
                      Cookies = None
                      Auth = NoAuth
                      Hash = None
                      StartImmediately = true }

                let newModel =
                    { model with
                        ActiveDialog = NoDialog
                        StatusText = "Adding download..." }

                newModel,
                Cmd.OfTask.perform
                    (fun () -> model.DownloadManager.AddAsync(request))
                    ()
                    (fun _ -> RefreshList)
            | _ -> model, Cmd.none

        | AddNewDownload url ->
            let uri = Uri(url.Trim())

            let request =
                { AddDownloadRequest.Url = uri
                  FileName = None
                  TargetFolder = None
                  Headers = Map.empty
                  Cookies = None
                  Auth = NoAuth
                  Hash = None
                  StartImmediately = true }

            model,
            Cmd.OfTask.perform
                (fun () -> model.DownloadManager.AddAsync(request))
                ()
                (fun _ -> RefreshList)

        // ── Download controls ──
        | StartDownload id ->
            model.DownloadManager.Start(id) |> ignore
            recomputeStatus model, Cmd.none

        | PauseDownload id ->
            model.DownloadManager.Pause(id) |> ignore

            let downloads =
                model.Downloads
                |> List.map (fun d ->
                    if d.Id = id then { d with StatusText = "Paused"; IsActive = false } else d)

            recomputeStatus { model with Downloads = downloads }, Cmd.none

        | CancelDownload id ->
            model.DownloadManager.Cancel(id) |> ignore

            let downloads =
                model.Downloads
                |> List.map (fun d ->
                    if d.Id = id then { d with StatusText = "Cancelled"; IsActive = false } else d)

            recomputeStatus { model with Downloads = downloads }, Cmd.none

        | RemoveDownload(id, deleteFiles) ->
            model.DownloadManager.Remove(id, deleteFiles)

            let downloads = model.Downloads |> List.filter (fun d -> d.Id <> id)

            recomputeStatus
                { model with
                    Downloads = downloads
                    SelectedDownload = None
                    ActiveDialog = NoDialog },
            Cmd.none

        // ── Dialog: New Download ──
        | OpenNewDownloadDialog ->
            { model with ActiveDialog = NewDownload("", "", "", false) }, Cmd.none

        | CloseNewDownloadDialog -> { model with ActiveDialog = NoDialog }, Cmd.none

        | UpdateNewDownloadUrl text ->
            match model.ActiveDialog with
            | NewDownload(_, fn, folder, _) ->
                { model with ActiveDialog = NewDownload(text, fn, folder, false) }, Cmd.none
            | _ -> model, Cmd.none

        | UpdateNewDownloadFileName text ->
            match model.ActiveDialog with
            | NewDownload(url, _, folder, _) ->
                { model with ActiveDialog = NewDownload(url, text, folder, false) }, Cmd.none
            | _ -> model, Cmd.none

        // ── Dialog: Delete Confirm ──
        | OpenDeleteConfirm(id, fileName) ->
            { model with ActiveDialog = DeleteConfirm(id, fileName, false) }, Cmd.none

        | CloseDeleteConfirm -> { model with ActiveDialog = NoDialog }, Cmd.none

        | ToggleDeleteFiles ->
            match model.ActiveDialog with
            | DeleteConfirm(id, fn, df) ->
                { model with ActiveDialog = DeleteConfirm(id, fn, not df) }, Cmd.none
            | _ -> model, Cmd.none

        // ── Dialog: Speed Limiter ──
        | OpenSpeedLimiter ->
            { model with ActiveDialog = SpeedLimiter(model.SpeedLimitKBps > 0, model.SpeedLimitKBps) }, Cmd.none

        | CloseSpeedLimiter -> { model with ActiveDialog = NoDialog }, Cmd.none

        | ToggleSpeedLimit ->
            match model.ActiveDialog with
            | SpeedLimiter(enabled, limit) ->
                let newEnabled = not enabled

                { model with
                    ActiveDialog = SpeedLimiter(newEnabled, if newEnabled && limit = 0 then 100 else limit) },
                Cmd.none
            | _ -> model, Cmd.none

        | UpdateSpeedLimit kbps ->
            match model.ActiveDialog with
            | SpeedLimiter(enabled, _) ->
                { model with ActiveDialog = SpeedLimiter(enabled, kbps) }, Cmd.none
            | _ -> model, Cmd.none

        | ApplySpeedLimit ->
            let finalLimit =
                match model.ActiveDialog with
                | SpeedLimiter(enabled, limit) -> if enabled then limit else 0
                | _ -> model.SpeedLimitKBps

            model.ConfigStore.Update(fun cfg -> { cfg with SpeedLimitKBps = finalLimit })

            { model with
                ActiveDialog = NoDialog
                SpeedLimitKBps = finalLimit },
            Cmd.none

        | ApplySpeedLimitWithValue kbps ->
            model.ConfigStore.Update(fun cfg -> { cfg with SpeedLimitKBps = kbps })

            { model with
                ActiveDialog = NoDialog
                SpeedLimitKBps = kbps },
            Cmd.none

        // ── Dialog: Download Complete ──
        | OpenDownloadComplete(filePath, folderPath) ->
            if model.ShowCompleteDialog then
                { model with ActiveDialog = DownloadComplete(filePath, folderPath, false) }, Cmd.none
            else
                model, Cmd.none

        | CloseDownloadComplete -> { model with ActiveDialog = NoDialog }, Cmd.none

        | DontShowCompleteDialog ->
            { model with
                ShowCompleteDialog = false
                ActiveDialog = NoDialog },
            Cmd.none

        // ── Selection ──
        | SelectDownload id ->
            let downloads = model.Downloads |> List.map (fun d -> { d with IsSelected = (Some d.Id = id) })

            { model with
                SelectedDownload = id
                Downloads = downloads },
            Cmd.none

        | ToggleSelectDownload id ->
            let downloads =
                model.Downloads
                |> List.map (fun d -> if d.Id = id then { d with IsSelected = not d.IsSelected } else d)

            { model with Downloads = downloads }, Cmd.none

        // ── Search ──
        | UpdateSearchQuery text ->
            let query = if String.IsNullOrWhiteSpace text then SearchAll else SearchText text
            recomputeStatus { model with SearchQuery = query }, Cmd.none

        | ClearSearch -> recomputeStatus { model with SearchQuery = SearchAll }, Cmd.none

        // ── Engine events ──
        | EngineEvent engineEvent ->
            match engineEvent with
            | ProgressUpdated(id, info) ->
                let speed = int64 info.Speed
                let speedStr = Helpers.FormatHelper.formatSpeed speed
                let etaText =
                    if speed > 0L && speed <= 1_000_000_000L then
                        match info.TotalBytes with
                        | Some total when total > info.DownloadedBytes && total > 0L<B> ->
                            let remaining = int64 total - int64 info.DownloadedBytes
                            let etaSec = float remaining / float speed
                            Helpers.FormatHelper.formatEta (TimeSpan.FromSeconds etaSec)
                        | _ -> ""
                    else ""

                let downloads =
                    model.Downloads
                    |> List.map (fun d ->
                        if d.Id = id then
                            { d with
                                Progress = info.Progress
                                ProgressInt = info.Progress |> int |> min 100 |> max 0
                                SpeedText = if speed > 0L then speedStr else ""
                                EtaText = etaText
                                StatusText = $"Downloading — {speedStr}"
                                IsActive = true }
                        else
                            d)

                recomputeStatus { model with Downloads = downloads }, Cmd.none

            | DownloadStarted id ->
                let downloads =
                    model.Downloads
                    |> List.map (fun d ->
                        if d.Id = id then { d with StatusText = "Starting"; IsActive = true } else d)

                recomputeStatus { model with Downloads = downloads }, Cmd.none

            | DownloadFinished(id, finalPath) ->
                let folderPath = Path.GetDirectoryName finalPath |> Option.ofObj |> Option.defaultValue ""

                let downloads =
                    model.Downloads
                    |> List.map (fun d ->
                        if d.Id = id then
                            { d with
                                Progress = 100.0
                                ProgressInt = 100
                                StatusText = "Completed"
                                IsActive = false
                                SpeedText = "" }
                        else
                            d)

                let newModel = recomputeStatus { model with Downloads = downloads }

                let completeCmd =
                    if model.ShowCompleteDialog then
                        Cmd.OfTask.perform
                            (fun () -> task { return (finalPath, folderPath) })
                            ()
                            (fun (fp, fol) -> OpenDownloadComplete(fp, fol))
                    else
                        Cmd.none

                newModel, completeCmd

            | DownloadFailed(id, error) ->
                let downloads =
                    model.Downloads
                    |> List.map (fun d ->
                        if d.Id = id then
                            { d with
                                StatusText = $"Error: {error}"
                                IsActive = false
                                SpeedText = "" }
                        else
                            d)

                recomputeStatus { model with Downloads = downloads }, Cmd.none

            | DownloadPaused id ->
                let downloads =
                    model.Downloads
                    |> List.map (fun d ->
                        if d.Id = id then
                            { d with
                                StatusText = "Paused"
                                IsActive = false
                                SpeedText = "" }
                        else
                            d)

                recomputeStatus { model with Downloads = downloads }, Cmd.none

            | DownloadAssembling id ->
                let downloads =
                    model.Downloads
                    |> List.map (fun d ->
                        if d.Id = id then
                            { d with
                                StatusText = "Assembling..."
                                Progress = 99.0
                                ProgressInt = 99
                                IsActive = false
                                SpeedText = "" }
                        else
                            d)

                recomputeStatus { model with Downloads = downloads }, Cmd.none

        // ── Shutdown ──
        | Shutdown ->
            let log = Log.ForContext("Source", "State")
            log.Information("Application shutdown initiated")

            model.QueueScheduler.Stop()

            // Fire-and-forget graceful shutdown with async manager shutdown
            // The Elmish loop is synchronous, so we start the async shutdown in background
            task {
                try
                    do! model.DownloadManager.ShutdownAsync()
                with ex ->
                    log.Error(ex, "Error during async shutdown")
            }
            |> ignore

            (model.QueueScheduler :> IDisposable).Dispose()
            (model.DownloadManager :> IDisposable).Dispose()

            log.Information("Application shutdown complete")
            model, Cmd.none

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

    let private dummyDownloads: DownloadDisplayItem list =
        [ { Id = Guid.NewGuid()
            FileName = "ubuntu-24.04-desktop-amd64.iso"
            Url = "https://releases.ubuntu.com/24.04/ubuntu-24.04-desktop-amd64.iso"
            SizeText = "4.1 GB"
            TotalSizeBytes = 4398046512L
            SpeedText = "12.4 MB/s"
            SpeedBps = 12400000L
            EtaText = "00:05:30"
            EtaSeconds = 330.0
            DateText = "Jul 05"
            AddedAt = DateTime.Now
            Progress = 45.0
            ProgressInt = 45
            StatusText = "Downloading — 12.4 MB/s"
            FileCategory = "program"
            CategoryName = "Programs"
            IsActive = true
            IsPaused = false
            IsCompleted = false
            IsSelected = false
            IsError = false
            TargetPath = "C:\\Downloads\\ubuntu-24.04-desktop-amd64.iso" }

          { Id = Guid.NewGuid()
            FileName = "sample-movie-1080p.mp4"
            Url = "https://example.com/movies/sample-movie-1080p.mp4"
            SizeText = "1.2 GB"
            TotalSizeBytes = 1200000000L
            SpeedText = ""
            SpeedBps = 0L
            EtaText = ""
            EtaSeconds = Double.MaxValue
            DateText = "Jul 04"
            AddedAt = DateTime.Now.AddDays(-1)
            Progress = 100.0
            ProgressInt = 100
            StatusText = "Completed"
            FileCategory = "video"
            CategoryName = "Videos"
            IsActive = false
            IsPaused = false
            IsCompleted = true
            IsSelected = false
            IsError = false
            TargetPath = "C:\\Downloads\\sample-movie-1080p.mp4" }

          { Id = Guid.NewGuid()
            FileName = "project-assets.zip"
            Url = "https://example.com/files/assets.zip"
            SizeText = "245 MB"
            TotalSizeBytes = 245000000L
            SpeedText = ""
            SpeedBps = 0L
            EtaText = ""
            EtaSeconds = Double.MaxValue
            DateText = "Jul 03"
            AddedAt = DateTime.Now.AddDays(-2)
            Progress = 62.0
            ProgressInt = 62
            StatusText = "Paused"
            FileCategory = "archive"
            CategoryName = "Compressed"
            IsActive = false
            IsPaused = true
            IsCompleted = false
            IsSelected = false
            IsError = false
            TargetPath = "C:\\Downloads\\project-assets.zip" } ]


    /// Initial application state and startup command.
    let init (dispatch: Msg -> unit) : Model * Elmish.Cmd<Msg> =

        let configStore = AppConfig.ConfigStore()

        let manager, scheduler = createDownloadManager configStore dispatch

        let monitor = new Helpers.BrowserMonitor(manager, configStore)
        monitor.Start()

        let initialModel =
            { Downloads = dummyDownloads
              //Downloads = []
              SelectedDownload = None
              SelectedCategory = "ALL_UNFINISHED"
              ActiveCount = 0
              ActiveDialog = NoDialog
              SearchQuery = SearchAll
              StatusText = "Ready"
              ShowCompleteDialog = true
              SpeedLimitKBps = configStore.Current.SpeedLimitKBps
              DownloadManager = manager
              QueueScheduler = scheduler
              ConfigStore = configStore
              BrowserMonitor = monitor
              ExpandedCategories = Set.ofList [ "All"; "Queues" ]
              SortColumn = "Date Added"
              SortAscending = false }

        let loadCmd =
            Cmd.OfTask.perform
                (fun () ->
                    task {
                        let entries = manager.GetAll()
                        return entries |> List.map DownloadDisplayItem.FromEntry
                    })
                ()
                (fun _ -> LoadFromDatabase)

        initialModel, loadCmd

    /// Sort downloads by the active sort column and direction
    let private sortDownloads (model: Model) (items: DownloadDisplayItem list) =
        let sorted =
            match model.SortColumn with
            | "Name" -> items |> List.sortBy (fun d -> d.FileName.ToLowerInvariant())
            | "Size" -> items |> List.sortBy (fun d -> d.TotalSizeBytes)
            | "Status" -> items |> List.sortBy (fun d -> d.StatusText)
            | "Speed" -> items |> List.sortBy (fun d -> d.SpeedBps)
            | "Time Left" -> items |> List.sortBy (fun d -> d.EtaSeconds)
            | "Date Added" -> items |> List.sortBy (fun d -> d.AddedAt)
            | _ -> items

        if model.SortAscending then sorted else List.rev sorted

    /// Filter and sort downloads by the active search query, selected category, and sort settings
    let applyFilters (model: Model) (downloads: DownloadDisplayItem list) =
        downloads
        |> List.filter (fun d ->
            // Category filter
            match model.SelectedCategory with
            | "ALL" -> true
            | "ALL_UNFINISHED" -> not d.IsCompleted && not d.IsError
            | "ALL_FINISHED" -> d.IsCompleted || d.IsError
            | "CAT_COMPRESSED" -> d.FileCategory = "archive"
            | "CAT_PROGRAMS" -> d.FileCategory = "program"
            | "CAT_VIDEOS" -> d.FileCategory = "video"
            | "CAT_MUSIC" -> d.FileCategory = "audio" || d.FileCategory = "music"
            | "CAT_PICTURES" -> d.FileCategory = "image"
            | "CAT_DOCUMENTS" -> d.FileCategory = "document"
            | "QUEUE_MAIN" -> true
            | cat -> d.CategoryName = cat)
        |> fun filtered ->
            match model.SearchQuery with
            | SearchAll -> filtered
            | SearchText text when String.IsNullOrWhiteSpace text -> filtered
            | SearchText text ->
                let lower = text.ToLowerInvariant()

                filtered
                |> List.filter (fun d ->
                    d.FileName.ToLowerInvariant().Contains lower
                    || d.Url.ToLowerInvariant().Contains lower)
        |> sortDownloads model

    /// Compute derived state from the download list
    let private recomputeStatus (model: Model) =
        { model with
            ActiveCount = model.Downloads |> List.filter (fun d -> d.IsActive) |> List.length
            StatusText =
                let active = model.Downloads |> List.filter (fun d -> d.IsActive) |> List.length

                if active > 0 then
                    $"{active} active download(s)"
                else
                    "Ready" }

    /// Pure update function — takes a message and the current model, returns new model + commands.
    let update (msg: Msg) (model: Model) : Model * Elmish.Cmd<Msg> =

        match msg with
        // ── Load from database ──
        | LoadFromDatabase ->
            let entries = model.DownloadManager.GetAll()
            let items = entries |> List.map DownloadDisplayItem.FromEntry
            // Merge actual database items with the dummy items
            let itemsWithDummy = dummyDownloads @ items

            recomputeStatus
                { model with
                    Downloads = itemsWithDummy },
            Cmd.none
        // recomputeStatus { model with Downloads = items }, Cmd.none

        | RefreshList ->
            let entries = model.DownloadManager.GetAll()
            let items = entries |> List.map DownloadDisplayItem.FromEntry
            let itemsWithDummy = dummyDownloads @ items

            recomputeStatus
                { model with
                    Downloads = itemsWithDummy },
            Cmd.none
        //recomputeStatus { model with Downloads = items }, Cmd.none

        // ── Add new download ──
        | SubmitNewDownload ->
            match model.ActiveDialog with
            | NewDownload(url, fileName, targetFolder, _) when not (String.IsNullOrWhiteSpace url) ->
                let uri = Uri(url.Trim())

                let request =
                    { AddDownloadRequest.Url = uri
                      FileName =
                        if String.IsNullOrWhiteSpace fileName then
                            None
                        else
                            Some fileName
                      TargetFolder =
                        if String.IsNullOrWhiteSpace targetFolder then
                            None
                        else
                            Some targetFolder
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
                Cmd.OfTask.perform (fun () -> model.DownloadManager.AddAsync(request)) () (fun _ -> RefreshList)
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

            model, Cmd.OfTask.perform (fun () -> model.DownloadManager.AddAsync(request)) () (fun _ -> RefreshList)

        // ── Download controls ──
        | StartDownload id ->
            model.DownloadManager.Start(id) |> ignore
            recomputeStatus model, Cmd.none

        | PauseDownload id ->
            model.DownloadManager.Pause(id) |> ignore

            let downloads =
                model.Downloads
                |> List.map (fun d ->
                    if d.Id = id then
                        { d with
                            StatusText = "Paused"
                            IsActive = false }
                    else
                        d)

            recomputeStatus { model with Downloads = downloads }, Cmd.none

        | CancelDownload id ->
            model.DownloadManager.Cancel(id) |> ignore

            let downloads =
                model.Downloads
                |> List.map (fun d ->
                    if d.Id = id then
                        { d with
                            StatusText = "Cancelled"
                            IsActive = false }
                    else
                        d)

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

        | RemoveDownloads(ids, deleteFiles) ->
            for id in ids do
                model.DownloadManager.Remove(id, deleteFiles)

            let downloads =
                model.Downloads |> List.filter (fun d -> not (List.contains d.Id ids))

            recomputeStatus
                { model with
                    Downloads = downloads
                    SelectedDownload = None
                    ActiveDialog = NoDialog },
            Cmd.none

        // ── Dialog: New Download ──
        | OpenNewDownloadDialog ->
            { model with
                ActiveDialog = NewDownload("", "", "", false) },
            Cmd.none

        | CloseNewDownloadDialog -> { model with ActiveDialog = NoDialog }, Cmd.none

        | UpdateNewDownloadUrl text ->
            match model.ActiveDialog with
            | NewDownload(_, fn, folder, _) ->
                { model with
                    ActiveDialog = NewDownload(text, fn, folder, false) },
                Cmd.none
            | _ -> model, Cmd.none

        | UpdateNewDownloadFileName text ->
            match model.ActiveDialog with
            | NewDownload(url, _, folder, _) ->
                { model with
                    ActiveDialog = NewDownload(url, text, folder, false) },
                Cmd.none
            | _ -> model, Cmd.none

        // ── Dialog: Delete Confirm ──
        | OpenDeleteConfirm(id, fileName) ->
            { model with
                ActiveDialog = DeleteConfirm(id, fileName, false) },
            Cmd.none

        | OpenDeleteConfirmMultiple(ids, displayNames) ->
            { model with
                ActiveDialog = DeleteConfirmMultiple(ids, displayNames, false) },
            Cmd.none

        | CloseDeleteConfirm -> { model with ActiveDialog = NoDialog }, Cmd.none

        | SetDeleteFiles df ->
            match model.ActiveDialog with
            | DeleteConfirm(id, fn, _) ->
                { model with
                    ActiveDialog = DeleteConfirm(id, fn, df) },
                Cmd.none
            | DeleteConfirmMultiple(ids, displayNames, _) ->
                { model with
                    ActiveDialog = DeleteConfirmMultiple(ids, displayNames, df) },
                Cmd.none
            | _ -> model, Cmd.none

        // ── Dialog: Speed Limiter ──
        | OpenSpeedLimiter ->
            { model with
                ActiveDialog = SpeedLimiter(model.SpeedLimitKBps > 0, model.SpeedLimitKBps) },
            Cmd.none

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
                { model with
                    ActiveDialog = SpeedLimiter(enabled, kbps) },
                Cmd.none
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
                { model with
                    ActiveDialog = DownloadComplete(filePath, folderPath, false) },
                Cmd.none
            else
                model, Cmd.none

        | CloseDownloadComplete -> { model with ActiveDialog = NoDialog }, Cmd.none

        | DontShowCompleteDialog ->
            { model with
                ShowCompleteDialog = false
                ActiveDialog = NoDialog },
            Cmd.none

        // ── Selection & Sorting & Folding ──
        | SelectDownload id ->
            let downloads =
                model.Downloads |> List.map (fun d -> { d with IsSelected = (Some d.Id = id) })

            { model with
                SelectedDownload = id
                Downloads = downloads },
            Cmd.none

        | SetSelectDownload(id, isSelected) ->
            let downloads =
                model.Downloads
                |> List.map (fun d ->
                    if d.Id = id then
                        { d with IsSelected = isSelected }
                    else
                        d)

            { model with Downloads = downloads }, Cmd.none

        | SetSelectAll isSelected ->
            let filtered = applyFilters model model.Downloads

            let downloads =
                model.Downloads
                |> List.map (fun d ->
                    let isFiltered = filtered |> List.exists (fun f -> f.Id = d.Id)
                    if isFiltered then { d with IsSelected = isSelected } else d)

            { model with Downloads = downloads }, Cmd.none

        | PauseSelected ->
            let selectedActive =
                model.Downloads |> List.filter (fun d -> d.IsSelected && d.IsActive)

            for d in selectedActive do
                model.DownloadManager.Pause(d.Id) |> ignore

            let downloads =
                model.Downloads
                |> List.map (fun d ->
                    let isSelActive = selectedActive |> List.exists (fun sa -> sa.Id = d.Id)

                    if isSelActive then
                        { d with
                            StatusText = "Paused"
                            IsActive = false }
                    else
                        d)

            recomputeStatus { model with Downloads = downloads }, Cmd.none

        | ResumeSelected ->
            let selectedInactive =
                model.Downloads
                |> List.filter (fun d -> d.IsSelected && not d.IsActive && not d.IsCompleted)

            for d in selectedInactive do
                model.DownloadManager.Start(d.Id) |> ignore

            let downloads =
                model.Downloads
                |> List.map (fun d ->
                    let isSelInactive = selectedInactive |> List.exists (fun si -> si.Id = d.Id)

                    if isSelInactive then
                        { d with
                            StatusText = "Starting"
                            IsActive = true }
                    else
                        d)

            recomputeStatus { model with Downloads = downloads }, Cmd.none

        | SelectCategory cat -> recomputeStatus { model with SelectedCategory = cat }, Cmd.none

        | ToggleCategoryFolder parent ->
            let expanded =
                if model.ExpandedCategories.Contains parent then
                    model.ExpandedCategories.Remove parent
                else
                    model.ExpandedCategories.Add parent

            { model with
                ExpandedCategories = expanded },
            Cmd.none

        | ToggleSort col ->
            let asc =
                if model.SortColumn = col then
                    not model.SortAscending
                else
                    true

            recomputeStatus
                { model with
                    SortColumn = col
                    SortAscending = asc },
            Cmd.none

        // ── Search ──
        | UpdateSearchQuery text ->
            let query =
                if String.IsNullOrWhiteSpace text then
                    SearchAll
                else
                    SearchText text

            recomputeStatus { model with SearchQuery = query }, Cmd.none

        | ClearSearch -> recomputeStatus { model with SearchQuery = SearchAll }, Cmd.none

        // ── Engine events ──
        | EngineEvent engineEvent ->
            match engineEvent with
            | ProgressUpdated(id, info) ->
                let speed = int64 info.Speed
                let speedStr = Helpers.FormatHelper.formatSpeed speed

                let etaSec, etaText =
                    if speed > 0L && speed <= 1_000_000_000L then
                        match info.TotalBytes with
                        | Some total when total > info.DownloadedBytes && total > 0L<B> ->
                            let remaining = int64 total - int64 info.DownloadedBytes
                            let sec = float remaining / float speed
                            sec, Helpers.FormatHelper.formatEta (TimeSpan.FromSeconds sec)
                        | _ -> Double.MaxValue, ""
                    else
                        Double.MaxValue, ""

                let downloads =
                    model.Downloads
                    |> List.map (fun d ->
                        if d.Id = id then
                            { d with
                                Progress = info.Progress
                                ProgressInt = info.Progress |> int |> min 100 |> max 0
                                SpeedText = if speed > 0L then speedStr else ""
                                SpeedBps = speed
                                EtaText = etaText
                                EtaSeconds = etaSec
                                StatusText = $"Downloading — {speedStr}"
                                IsActive = true }
                        else
                            d)

                recomputeStatus { model with Downloads = downloads }, Cmd.none

            | DownloadStarted id ->
                let downloads =
                    model.Downloads
                    |> List.map (fun d ->
                        if d.Id = id then
                            { d with
                                StatusText = "Starting"
                                IsActive = true }
                        else
                            d)

                recomputeStatus { model with Downloads = downloads }, Cmd.none

            | DownloadFinished(id, finalPath) ->
                let folderPath =
                    Path.GetDirectoryName finalPath |> Option.ofObj |> Option.defaultValue ""

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
                        Cmd.OfTask.perform (fun () -> task { return (finalPath, folderPath) }) () (fun (fp, fol) ->
                            OpenDownloadComplete(fp, fol))
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
            (model.BrowserMonitor :> IDisposable).Dispose()

            log.Information("Application shutdown complete")
            model, Cmd.none

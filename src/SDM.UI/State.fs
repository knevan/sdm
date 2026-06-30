namespace SDM.UI

open System
open System.Threading.Tasks
open System.IO
open SDM.Domain
open SDM.Application
open SDM.Infrastructure

/// Commands represent side effects that the update function cannot perform directly.
/// They are executed by the runtime (e.g., the Avalonia dispatcher or a background thread)
/// and their results are fed back as new messages.
type Cmd =
    | CmdNone
    | CmdAsyncTask of (unit -> Task<Msg>)
    | CmdBatch of Cmd list
    | CmdSubscribe of ((Msg -> unit) -> IDisposable)

module Cmd =
    let none = CmdNone

    let ofTask (t: unit -> Task<Msg>) = CmdAsyncTask t

    let batch (cmds: Cmd list) = CmdBatch cmds

    let ofSub (subscribe: (Msg -> unit) -> IDisposable) = CmdSubscribe subscribe

    let rec map (f: Msg -> Msg) (cmd: Cmd) =
        match cmd with
        | CmdNone -> CmdNone
        | CmdAsyncTask t ->
            CmdAsyncTask(fun () ->
                task {
                    let! r = t ()
                    return f r
                })
        | CmdBatch cs -> CmdBatch(cs |> List.map (map f))
        | CmdSubscribe sub -> CmdSubscribe(fun dispatch -> sub (f >> dispatch))

/// Pure state module following the Elmish pattern.
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
    /// Loads the download list from SQLite and returns the command to subscribe to engine events.
    let init () : Model * Cmd =

        let configStore = AppConfig.ConfigStore()

        // Create a temporary dispatch for bootstrapping
        let mutable dispatchRef: (Msg -> unit) option = None

        let getDispatch () =
            match dispatchRef with
            | Some d -> d
            | None -> failwith "dispatch not initialized"

        // Placeholder that will be replaced after init returns
        let manager, scheduler = createDownloadManager configStore (fun _ -> ())

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
            Cmd.ofTask (fun () ->
                task {
                    let entries = manager.GetAll()

                    return
                        entries
                        |> List.map DownloadDisplayItem.FromEntry
                        |> fun items -> LoadFromDatabase
                })

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
        let filtered = applySearch model.SearchQuery model.Downloads

        { model with
            ActiveCount = model.Downloads |> List.filter (fun d -> d.IsActive) |> List.length
            StatusText =
                let active = model.Downloads |> List.filter (fun d -> d.IsActive) |> List.length

                if active > 0 then
                    $"{active} active download(s)"
                else
                    "Ready" }

    /// Pure update function — takes a message and the current model, returns new model + commands.
    let update (msg: Msg) (model: Model) : Model * Cmd =

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

                let cmd =
                    Cmd.ofTask (fun () ->
                        task {
                            let! result = model.DownloadManager.AddAsync(request)

                            return
                                match result with
                                | Added id ->
                                    let entry = model.DownloadManager.TryGet(id)

                                    match entry with
                                    | None -> RefreshList
                                    | Some e -> RefreshList

                                | _ -> RefreshList
                        })

                let newModel =
                    { model with
                        ActiveDialog = NoDialog
                        StatusText = "Adding download..." }

                newModel, Cmd.batch [ cmd; Cmd.ofTask (fun () -> task { return CloseNewDownloadDialog }) ]
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

            let cmd =
                Cmd.ofTask (fun () ->
                    task {
                        let! result = model.DownloadManager.AddAsync(request)
                        return RefreshList
                    })

            model, cmd

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

        // ── Dialog: New Download ──
        | OpenNewDownloadDialog ->
            { model with
                ActiveDialog = NewDownload("", "", "", false) },
            Cmd.none

        | CloseNewDownloadDialog -> { model with ActiveDialog = NoDialog }, Cmd.none

        | UpdateNewDownloadUrl text ->
            match model.ActiveDialog with
            | NewDownload(_url, fn, folder, _) ->
                { model with
                    ActiveDialog = NewDownload(text, fn, folder, false) },
                Cmd.none
            | _ -> model, Cmd.none

        | UpdateNewDownloadFileName text ->
            match model.ActiveDialog with
            | NewDownload(url, _fn, folder, _) ->
                { model with
                    ActiveDialog = NewDownload(url, text, folder, false) },
                Cmd.none
            | _ -> model, Cmd.none

        // ── Dialog: Delete Confirm ──
        | OpenDeleteConfirm(id, fileName) ->
            { model with
                ActiveDialog = DeleteConfirm(id, fileName, false) },
            Cmd.none

        | CloseDeleteConfirm -> { model with ActiveDialog = NoDialog }, Cmd.none

        | ToggleDeleteFiles ->
            match model.ActiveDialog with
            | DeleteConfirm(id, fileName, df) ->
                { model with
                    ActiveDialog = DeleteConfirm(id, fileName, not df) },
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
            match model.ActiveDialog with
            | SpeedLimiter(enabled, limitKBps) ->
                let newLimit = if enabled then limitKBps else 0

                model.ConfigStore.Update(fun cfg -> { cfg with SpeedLimitKBps = newLimit })

                { model with
                    ActiveDialog = NoDialog
                    SpeedLimitKBps = newLimit },
                Cmd.none
            | _ -> model, Cmd.none

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

        // ── Selection ──
        | SelectDownload id ->
            let downloads =
                model.Downloads |> List.map (fun d -> { d with IsSelected = (Some d.Id = id) })

            { model with
                SelectedDownload = id
                Downloads = downloads },
            Cmd.none

        | ToggleSelectDownload id ->
            let downloads =
                model.Downloads
                |> List.map (fun d ->
                    if d.Id = id then
                        { d with IsSelected = not d.IsSelected }
                    else
                        d)

            { model with Downloads = downloads }, Cmd.none

        // ── Search ──
        | UpdateSearchQuery text ->
            let query =
                if String.IsNullOrWhiteSpace text then
                    SearchAll
                else
                    SearchText text

            recomputeStatus { model with SearchQuery = query }, Cmd.none

        | ClearSearch -> recomputeStatus { model with SearchQuery = SearchAll }, Cmd.none

        // ── Engine events (from background threads) ──
        | EngineEvent engineEvent ->
            match engineEvent with
            | ProgressUpdated(id, info) ->
                let downloads =
                    model.Downloads
                    |> List.map (fun d ->
                        if d.Id = id then
                            let speedStr = Helpers.FormatHelper.formatSpeed (int64 info.Speed)

                            { d with
                                Progress = info.Progress
                                ProgressInt = info.Progress |> int |> min 100 |> max 0
                                SpeedText = if info.Speed > 0L<Bps> then speedStr else ""
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
                        Cmd.ofTask (fun () -> task { return OpenDownloadComplete(finalPath, folderPath) })
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

        // ── Shutdown ──
        | Shutdown ->
            model.QueueScheduler.Stop()
            (model.QueueScheduler :> IDisposable).Dispose()
            (model.DownloadManager :> IDisposable).Dispose()
            model, Cmd.none

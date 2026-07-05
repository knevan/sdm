namespace SDM.Application

open System
open System.Collections.Concurrent
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open SDM.Domain
open SDM.Engine
open SDM.Infrastructure
open Serilog

/// Immutable request to add a new download to the manager
type AddDownloadRequest =
    { Url: Uri
      FileName: string option
      TargetFolder: string option
      Headers: Map<string, string>
      Cookies: string option
      Auth: AuthInfo
      Hash: (HashAlgorithm * string) option
      StartImmediately: bool }

    static member Create(url: Uri) =
        { Url = url
          FileName = None
          TargetFolder = None
          Headers = Map.empty
          Cookies = None
          Auth = NoAuth
          Hash = None
          StartImmediately = true }

/// Result of adding a download
type AddDownloadResult =
    | Added of id: Guid
    | AlreadyExists of id: Guid
    | InvalidUrl of message: string

/// Manages the full lifecycle of all downloads.
type DownloadManager(configStore: AppConfig.ConfigStore, connectionString: string, eventHandler: Action<DownloadEvent>)
    =
    // Single shared SocketsHttpHandler for all downloads — connection pooling
    let sharedHandler =
        HttpClientService.createSharedHandler HttpClientService.defaultConfig

    let log = Log.ForContext<DownloadManager>()

    // Per-download HttpClient instances (reused for probing)
    let probeHttpClient =
        let client = new HttpClient(sharedHandler, disposeHandler = false)
        client.Timeout <- Timeout.InfiniteTimeSpan

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36"
        )

        client

    // Active coordinators keyed by download ID
    let activeDownloads = ConcurrentDictionary<Guid, DownloadCoordinator>()

    let storageService = DiskStorage.create ()

    /// Create an IHttpService for a specific download entry's auth/headers, backed by the shared handler
    let createHttpService (entry: DownloadEntry) =
        HttpClientService.createWithHandler sharedHandler entry.Auth entry.Headers entry.Cookies

    /// Invoke user-facing event handler
    let raiseEvent (evt: DownloadEvent) =
        try
            eventHandler.Invoke evt
        with ex ->
            log.Warning(ex, "Error dispatching event {EventType}", evt.GetType().Name)
            ()

    /// Derive file name from URL
    let deriveFileName (url: Uri) =
        let lastSegment = url.Segments |> Array.last
        let decoded = Uri.UnescapeDataString lastSegment

        if String.IsNullOrWhiteSpace decoded || decoded = "/" then
            $"download_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
        else
            Path.GetFileName decoded |> Option.ofObj |> Option.defaultValue decoded

    /// Resolve file name conflicts according to the configured mode.
    /// Ask mode returns the input path unchanged — conflict resolution is handled at the UI layer.
    let resolveConflict (path: string) (mode: FileConflictMode) =
        match mode with
        | Overwrite -> path
        | Ask -> path
        | AutoRename ->
            if not (File.Exists path) then
                path
            else
                let dir = Path.GetDirectoryName path |> Option.ofObj |> Option.defaultValue ""

                let name =
                    Path.GetFileNameWithoutExtension path
                    |> Option.ofObj
                    |> Option.defaultValue "download"

                let ext = Path.GetExtension path |> Option.ofObj |> Option.defaultValue ""

                let rec findAvailable (n: int) =
                    let candidate = Path.Combine(dir, $"{name} ({n}){ext}")

                    if File.Exists candidate then
                        findAvailable (n + 1)
                    else
                        candidate

                findAvailable 1

    /// Build a DownloadEntry from an AddDownloadRequest
    let buildEntry (request: AddDownloadRequest) =
        let config = configStore.Current

        let fileName =
            request.FileName |> Option.defaultWith (fun () -> deriveFileName request.Url)

        let targetFolder =
            request.TargetFolder |> Option.defaultValue config.DefaultDownloadFolder

        let targetPath =
            Path.Combine(targetFolder, fileName)
            |> fun p -> resolveConflict p config.FileConflictMode

        let tempFolder = Path.Combine(config.TempFolder, Guid.NewGuid().ToString("N"))
        let tempFilePath = Path.Combine(tempFolder, $"{fileName}.sdm.part")

        { Id = Guid.NewGuid()
          Url = request.Url
          FileName = Path.GetFileName fileName |> Option.ofObj |> Option.defaultValue fileName
          TargetPath = targetPath
          TempFolderPath = tempFolder
          TotalSize = None
          AddedAt = DateTime.UtcNow
          Status = Queue
          Segments = []
          Headers = request.Headers
          Cookies = request.Cookies
          Auth = request.Auth
          Hash = request.Hash }

    /// Probe the URL and update the entry with server metadata
    let probeAndInput (entry: DownloadEntry) =
        async {
            let! probe = Networking.probeUrlSimple probeHttpClient entry.Url entry.Auth

            let inputFileName = probe.FileName |> Option.defaultValue entry.FileName
            let inputUrl = if probe.IsRedirected then probe.FinalUri else entry.Url

            let targetDir =
                Path.GetDirectoryName entry.TargetPath
                |> Option.ofObj
                |> Option.defaultValue "."

            return
                { entry with
                    Url = inputUrl
                    FileName = inputFileName
                    TotalSize = probe.Size
                    TargetPath =
                        Path.Combine(targetDir, inputFileName)
                        |> fun p -> resolveConflict p configStore.Current.FileConflictMode }
        }

    /// Internal state change callback from DownloadCoordinator when entry state changes
    let onCoordinatorStateChange (entry: DownloadEntry) =
        DownloadStore.upsert connectionString entry

    /// Internal event handler that wraps user handler + persistence + lifecycle cleanup
    let createCoordinatorEventHandler (entryRef: DownloadEntry ref) (event: DownloadEvent) =
        match event with
        | DownloadStarted _ ->
            log.Information("Download started: {FileName}", entryRef.Value.FileName)

            let updated =
                { entryRef.Value with
                    Status = Downloading(0L<Bps>, TimeSpan.Zero) }

            DownloadStore.upsert connectionString updated
            entryRef.Value <- updated

        | ProgressUpdated(id, info) ->
            let updated =
                { entryRef.Value with
                    Status = Downloading(info.Speed, TimeSpan.Zero) }

            DownloadStore.upsert connectionString updated
            entryRef.Value <- updated

        | DownloadAssembling id ->
            log.Information("Download assembling: {FileName}", entryRef.Value.FileName)

            let updated =
                { entryRef.Value with
                    Status = Assembling }

            DownloadStore.upsert connectionString updated
            entryRef.Value <- updated

        | DownloadFinished(id, finalPath) ->
            log.Information("Download finished: {FileName} -> {Path}", entryRef.Value.FileName, finalPath)

            let updated =
                { entryRef.Value with
                    Status = Completed DateTime.UtcNow }

            DownloadStore.upsert connectionString updated
            entryRef.Value <- updated
            activeDownloads.TryRemove id |> ignore

        | DownloadFailed(id, error) ->
            log.Warning("Download failed: {FileName} — {Error}", entryRef.Value.FileName, error)

            let updated =
                { entryRef.Value with
                    Status = Error("DOWNLOAD_FAILED", error) }

            DownloadStore.upsert connectionString updated
            entryRef.Value <- updated
            activeDownloads.TryRemove id |> ignore

        | DownloadPaused id ->
            log.Information("Download paused: {FileName}", entryRef.Value.FileName)

            let updated = { entryRef.Value with Status = Paused }
            DownloadStore.upsert connectionString updated
            entryRef.Value <- updated

        raiseEvent event

    /// Start a coordinator for the given download entry
    let startCoordinator (entry: DownloadEntry) =
        let entryRef = ref entry
        let httpService = createHttpService entry

        // Create temp directory and derive temp file path
        ensureDirectory entry.TempFolderPath
        let tempFilePath = Path.Combine(entry.TempFolderPath, $"{entry.FileName}.sdm.part")

        let coordinator =
            new DownloadCoordinator(
                entry,
                tempFilePath,
                httpService,
                storageService,
                createCoordinatorEventHandler entryRef,
                onCoordinatorStateChange,
                configStore.Current.SpeedLimitKBps
            )

        if activeDownloads.TryAdd(entry.Id, coordinator) then
            coordinator.Start()
            true
        else
            (coordinator :> IDisposable).Dispose()
            false

    /// Add a new download
    member _.AddAsync(request: AddDownloadRequest) : Task<AddDownloadResult> =
        task {
            try
                let entry = buildEntry request

                log.Information("Adding download: {FileName} from {Url}", entry.FileName, entry.Url)

                let! input =
                    async {
                        try
                            return! probeAndInput entry
                        with ex ->
                            log.Warning(ex, "Probe failed for {Url}, proceeding with defaults", entry.Url)
                            return entry
                    }

                DownloadStore.upsert connectionString input

                if request.StartImmediately then
                    startCoordinator input |> ignore

                return Added input.Id

            with ex ->
                log.Error(ex, "Failed to add download from {Url}", request.Url)
                return InvalidUrl ex.Message
        }

    /// Start or resume a download by ID
    member _.Start(id: Guid) =
        match activeDownloads.TryGetValue id with
        | true, coordinator ->
            log.Information("Resuming download: {Id}", id)
            coordinator.Resume()
            true
        | false, _ ->
            match DownloadStore.tryGet connectionString id with
            | Some entry ->
                log.Information("Starting download from DB: {FileName}", entry.FileName)
                startCoordinator entry
            | None ->
                log.Warning("Download not found for start: {Id}", id)
                false

    /// Pause an active download
    member _.Pause(id: Guid) =
        match activeDownloads.TryGetValue id with
        | true, coordinator ->
            log.Information("Pausing download: {Id}", id)
            coordinator.Pause()

            DownloadStore.tryGet connectionString id
            |> Option.iter (fun entry -> DownloadStore.upsert connectionString { entry with Status = Paused })

            true
        | false, _ ->
            log.Warning("Cannot pause — download not active: {Id}", id)
            false

    /// Cancel and remove an active download from the active list
    member _.Cancel(id: Guid) =
        match activeDownloads.TryRemove id with
        | true, coordinator ->
            log.Information("Cancelling download: {Id}", id)
            coordinator.Cancel()
            (coordinator :> IDisposable).Dispose()

            DownloadStore.tryGet connectionString id
            |> Option.iter (fun entry ->
                DownloadStore.upsert
                    connectionString
                    { entry with
                        Status = Error("CANCELLED", "Cancelled by user") })

            true
        | false, _ ->
            log.Warning("Cannot cancel — download not found: {Id}", id)
            false

    /// Remove a download completely (from DB + optional file cleanup)
    member this.Remove(id: Guid, deleteFiles: bool) =
        log.Information("Removing download: {Id} (deleteFiles={DeleteFiles})", id, deleteFiles)
        this.Cancel id |> ignore

        if deleteFiles then
            DownloadStore.tryGet connectionString id
            |> Option.iter (fun entry ->
                try
                    if Directory.Exists entry.TempFolderPath then
                        Directory.Delete(entry.TempFolderPath, recursive = true)
                with ex ->
                    log.Warning(ex, "Failed to delete temp folder for {Id}", id)

                try
                    if File.Exists entry.TargetPath then
                        File.Delete entry.TargetPath
                with ex ->
                    log.Warning(ex, "Failed to delete target file for {Id}", id))

        DownloadStore.delete connectionString id

    /// Get a list of all downloads from DB
    member _.GetAll() = DownloadStore.listAll connectionString

    /// Get a specific download by ID
    member _.TryGet(id: Guid) =
        DownloadStore.tryGet connectionString id

    /// Check if a download is currently active
    member _.IsActive(id: Guid) = activeDownloads.ContainsKey id

    /// Get the count of active downloads
    member _.ActiveCount = activeDownloads.Count

    /// Graceful shutdown: pause all active downloads, save state, wait for workers, then dispose.
    member _.ShutdownAsync() : Task =
        task {
            let count = activeDownloads.Count
            log.Information("Shutting down {Count} active download(s) gracefully...", count)

            if count > 0 then
                // Step 1: Pause all coordinators — this triggers onStateChange -> DB upsert
                for kvp in activeDownloads do
                    try
                        kvp.Value.Pause()
                    with ex ->
                        log.Warning(ex, "Error pausing download {Id} during shutdown", kvp.Key)

                // Step 2: Wait for in-flight writes to flush (2s grace period)
                log.Information("Waiting 2s for workers to flush pending writes...")
                do! Task.Delay 2000

                // Step 3: Dispose all coordinators
                for kvp in activeDownloads do
                    try
                        (kvp.Value :> IDisposable).Dispose()
                    with ex ->
                        log.Warning(ex, "Error disposing coordinator {Id} during shutdown", kvp.Key)

                activeDownloads.Clear()

            log.Information("Shutdown complete — {Count} downloads handled", count)
        }

    /// Stop all active downloads immediately (synchronous, for use in Dispose).
    member _.StopAll() =
        for kvp in activeDownloads do
            try
                kvp.Value.Pause()
            with _ ->
                ()

        // Brief blocking sleep to allow DB flush
        Thread.Sleep 500

        for kvp in activeDownloads do
            try
                (kvp.Value :> IDisposable).Dispose()
            with _ ->
                ()

        activeDownloads.Clear()

    interface IDisposable with
        member this.Dispose() =
            log.Information("DownloadManager disposing...")
            this.StopAll()
            probeHttpClient.Dispose()
            sharedHandler.Dispose()
            log.Information("DownloadManager disposed")

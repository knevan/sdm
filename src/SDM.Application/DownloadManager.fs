namespace SDM.Application

open System
open System.Collections.Concurrent
open System.IO
open System.Net.Http
open System.Threading
open SDM.Domain
open SDM.Engine
open SDM.Infrastructure
open System.Threading.Tasks

/// Request to add a new download to the manager
type AddDownloadRequest(url: Uri) =
    new() = AddDownloadRequest(Unchecked.defaultof<Uri>)

    member val Url: Uri = url with get, set
    member val FileName: string = null with get, set
    member val TargetFolder: string = null with get, set
    member val Headers: Collections.Generic.IReadOnlyDictionary<string, string> = null with get, set
    member val Cookies: string = null with get, set
    member val Auth: AuthInfo = NoAuth with get, set
    member val Hash: Nullable<struct (HashAlgorithm * string)> = Nullable() with get, set
    member val StartImmediately: bool = true with get, set
    static member Create(url: Uri) = AddDownloadRequest(url)


/// Result of adding a download
type AddDownloadResult =
    | Added of id: Guid
    | AlreadyExists of id: Guid
    | InvalidUrl of message: string

/// Manages the full lifecycle of all downloads.
type DownloadManager(configStore: AppConfig.ConfigStore, connectionString: string, eventHandler: Action<DownloadEvent>)
    =
    // Active coordinators keyed by download ID
    let activeDownloads = ConcurrentDictionary<Guid, DownloadCoordinator>()

    let httpHandler =
        let config = configStore.Current
        let handler = new SocketsHttpHandler()
        handler.ConnectTimeout <- TimeSpan.FromSeconds(float config.NetworkTimeoutSeconds)
        handler.MaxConnectionsPerServer <- 16
        handler.AllowAutoRedirect <- true
        handler.MaxAutomaticRedirections <- 10
        handler.PooledConnectionLifetime <- TimeSpan.FromMinutes 5.0
        handler.PooledConnectionIdleTimeout <- TimeSpan.FromMinutes 2.0
        handler.AutomaticDecompression <- Net.DecompressionMethods.All
        handler

    let httpClient =
        let client = new HttpClient(httpHandler)
        client.Timeout <- Timeout.InfiniteTimeSpan
        // [TODO] Temporary User Agent
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36"
        )

        client

    let storageService = DiskStorage.create ()

    /// Create an IHttpService for a specific download entry's auth/headers
    let createHttpService (entry: DownloadEntry) =
        HttpClientService.create HttpClientService.defaultConfig entry.Auth entry.Headers entry.Cookies

    /// Invoke event handler
    let raiseEvent (evt: DownloadEvent) =
        try
            eventHandler.Invoke evt
        with _ ->
            ()

    /// Derive the file name from the URL if not provided
    let deriveFileName (url: Uri) =
        let lastSegment = url.Segments |> Array.last
        let decoded = Uri.UnescapeDataString lastSegment

        if String.IsNullOrWhiteSpace decoded || decoded = "/" then
            $"download_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
        else
            Path.GetFileName decoded |> Option.ofObj |> Option.defaultValue decoded

    /// Resolve file name conflicts by appending
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

                let rec findAvailable n =
                    let candidate = Path.Combine(dir, $"{name} ({n}){ext}")

                    if File.Exists candidate then
                        findAvailable (n + 1)
                    else
                        candidate

                findAvailable 1

    /// Build a DownloadEntry from an add request
    let buildEntry (request: AddDownloadRequest) =
        let config = configStore.Current

        let fileName =
            request.FileName
            |> Option.ofObj
            |> Option.defaultWith (fun () -> deriveFileName request.Url)

        let targetFolder =
            request.TargetFolder
            |> Option.ofObj
            |> Option.defaultValue config.DefaultDownloadFolder

        let targetPath =
            Path.Combine(targetFolder, fileName)
            |> fun p -> resolveConflict p config.FileConflictMode

        let tempFolder = Path.Combine(config.TempFolder, Guid.NewGuid().ToString("N"))

        { Id = Guid.NewGuid()
          Url = request.Url
          FileName = Path.GetFileName fileName |> Option.ofObj |> Option.defaultValue fileName
          TargetPath = targetPath
          TempFolderPath = tempFolder
          TotalSize = None
          AddedAt = DateTime.UtcNow
          Status = Queue
          Segments = []
          Headers =
            match request.Headers with
            | null -> Map.empty
            | h ->
                h
                |> Seq.fold (fun (m: Map<string, string>) kv -> m.Add(kv.Key, kv.Value)) Map.empty
          Cookies = request.Cookies |> Option.ofObj
          Auth = request.Auth
          Hash =
            if request.Hash.HasValue then
                Some(request.Hash.Value |> fun struct (a, h) -> (a, h))
            else
                None }

    /// Probe the URL and input the entry with server metadata
    let probeAndInput (entry: DownloadEntry) =
        async {
            let! probe = Networking.probeUrl httpClient entry.Url entry.Auth

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

    /// Internal event handler that wraps user handler + persistence side effects
    let internalEventHandler (entry: DownloadEntry ref) (event: DownloadEvent) =
        match event with
        | DownloadFinished(id, finalPath) ->
            let updated =
                { entry.Value with
                    Status = Completed DateTime.UtcNow }

            DownloadStore.upsert connectionString updated
            entry.Value <- updated
            activeDownloads.TryRemove id |> ignore

        | DownloadFailed(id, error) ->
            let updated =
                { entry.Value with
                    Status = Error("DOWNLOAD_FAILED", error) }

            DownloadStore.upsert connectionString updated
            entry.Value <- updated
            activeDownloads.TryRemove id |> ignore

        | _ -> ()

        raiseEvent event

    /// Start a coordinator for the given entry
    let startCoordinator (entry: DownloadEntry) =
        let entryRef = ref entry
        let httpService = createHttpService entry

        storageService.EnsureDirectory(Path.Combine(entry.TempFolderPath, "placeholder"))

        let coordinator =
            new DownloadCoordinator(entry, httpService, storageService, internalEventHandler entryRef)

        if activeDownloads.TryAdd(entry.Id, coordinator) then
            coordinator.Start()
            true
        else
            (coordinator :> IDisposable).Dispose()
            false

    /// Add a new download.
    member _.AddAsync(request: AddDownloadRequest) : Task<AddDownloadResult> =
        task {
            try
                let entry = buildEntry request

                let! input =
                    async {
                        try
                            return! probeAndInput entry
                        with _ ->
                            return entry
                    }

                DownloadStore.upsert connectionString input

                if request.StartImmediately then
                    startCoordinator input |> ignore

                return Added input.Id

            with ex ->
                return InvalidUrl ex.Message
        }

    /// Start or resume a download by ID
    member _.Start(id: Guid) =
        match activeDownloads.TryGetValue id with
        | true, coordinator ->
            coordinator.Resume()
            true
        | false, _ ->
            match DownloadStore.tryGet connectionString id with
            | Some entry -> startCoordinator entry
            | None -> false

    /// Pause an active download
    member _.Pause(id: Guid) =
        match activeDownloads.TryGetValue id with
        | true, coordinator ->
            coordinator.Pause()

            DownloadStore.tryGet connectionString id
            |> Option.iter (fun entry -> DownloadStore.upsert connectionString { entry with Status = Paused })

            true
        | false, _ -> false

    /// Cancel and remove an active download from the active list
    member _.Cancel(id: Guid) =
        match activeDownloads.TryRemove id with
        | true, coordinator ->
            coordinator.Cancel()
            (coordinator :> IDisposable).Dispose()

            DownloadStore.tryGet connectionString id
            |> Option.iter (fun entry ->
                DownloadStore.upsert
                    connectionString
                    { entry with
                        Status = Error("CANCELLED", "Cancelled by user") })

            true
        | false, _ -> false

    /// Remove a download completely (from DB + option file cleanup)
    member this.Remove(id: Guid, deleteFiles: bool) =
        this.Cancel id |> ignore

        if deleteFiles then
            DownloadStore.tryGet connectionString id
            |> Option.iter (fun entry ->
                try
                    if Directory.Exists entry.TempFolderPath then
                        Directory.Delete(entry.TempFolderPath, recursive = true)
                with _ ->
                    ()

                try
                    if File.Exists entry.TargetPath then
                        File.Delete entry.TargetPath
                with _ ->
                    ())

        DownloadStore.delete connectionString id

    /// Get a list of all downloads from DB
    member _.GetAll() = DownloadStore.listAll connectionString

    /// Get a specific download by ID
    member _.TryGet(id: Guid) =
        DownloadStore.tryGet connectionString id

    /// Check if a download is currently active
    member _.IsActive(id: Guid) = activeDownloads.ContainsKey id

    /// Get the count of active download
    member _.ActiveCount = activeDownloads.Count

    /// Stop all active downloads gracefully
    member _.StopAll() =
        for kvp in activeDownloads do
            kvp.Value.Pause()
            (kvp.Value :> IDisposable).Dispose()

        activeDownloads.Clear()

    interface IDisposable with
        member this.Dispose() =
            this.StopAll()
            httpClient.Dispose()
            httpHandler.Dispose()

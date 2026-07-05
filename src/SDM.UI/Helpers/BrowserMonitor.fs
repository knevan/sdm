namespace SDM.UI.Helpers

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent
open SDM.Domain
open SDM.Application
open SDM.Infrastructure
open Serilog

/// Video item tracking information for the offloaded media grabber
type VideoItem =
    { Id: string
      Text: string
      Info: string
      TabId: string
      TabUrl: string
      Url: string
      Headers: Map<string, string list>
      Cookies: string option }

/// DTO payload for trigger video download requests
[<CLIMutable>]
type VidPayload = { Vid: string; silentDownload: bool }

/// DTO payload for updating browser tab information
[<CLIMutable>]
type TabUpdatePayload = { TabUrl: string; TabTitle: string }

/// JSON DTO received from the SDM browser extension
[<CLIMutable>]
type DownloadRequestDto =
    { url: string
      fileName: string
      cookies: string
      requestHeaders: Map<string, string list>
      responseHeaders: Map<string, string list>
      referrer: string
      mimeType: string
      tabUrl: string
      tabId: string
      silentDownload: bool }

/// Helpers for mapping nullable CLR types to safe F# types
module Sanitizer =
    let sanitizeDto (dto: DownloadRequestDto) : DownloadRequestDto =
        { url = if isNull (box dto.url) then "" else dto.url
          fileName = if isNull (box dto.fileName) then "" else dto.fileName
          cookies = if isNull (box dto.cookies) then "" else dto.cookies
          requestHeaders =
            if isNull (box dto.requestHeaders) then
                Map.empty
            else
                dto.requestHeaders
          responseHeaders =
            if isNull (box dto.responseHeaders) then
                Map.empty
            else
                dto.responseHeaders
          referrer = if isNull (box dto.referrer) then "" else dto.referrer
          mimeType = if isNull (box dto.mimeType) then "" else dto.mimeType
          tabUrl = if isNull (box dto.tabUrl) then "" else dto.tabUrl
          tabId = if isNull (box dto.tabId) then "" else dto.tabId
          silentDownload = dto.silentDownload }

    let sanitizeVideoItem (item: VideoItem) : VideoItem =
        { Id = if isNull (box item.Id) then "" else item.Id
          Text = if isNull (box item.Text) then "" else item.Text
          Info = if isNull (box item.Info) then "" else item.Info
          TabId = if isNull (box item.TabId) then "" else item.TabId
          TabUrl = if isNull (box item.TabUrl) then "" else item.TabUrl
          Url = if isNull (box item.Url) then "" else item.Url
          Headers =
            if isNull (box item.Headers) then
                Map.empty
            else
                item.Headers
          Cookies = item.Cookies }

/// Lightweight F# HttpListener-based IPC server to communicate with the browser extension.
/// Supports zero-configuration port auto-scanning and asynchronous HLS/media offloaded parsing.
type BrowserMonitor(manager: DownloadManager, configStore: AppConfig.ConfigStore) =
    let log = Log.ForContext<BrowserMonitor>()
    let listener = new HttpListener()
    let videoRegistry = ConcurrentDictionary<string, VideoItem>()
    let mutable activePort = 8597
    let cts = new CancellationTokenSource()

    let jsonOptions = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

    // Helper to format file sizes for human readability
    let formatSize (bytes: int64) : string =
        if bytes < 1024L then
            $"{bytes} B"
        else
            let kb = float bytes / 1024.0

            if kb < 1024.0 then
                $"{kb:F2} KB"
            else
                let mb = kb / 1024.0

                if mb < 1024.0 then
                    $"{mb:F2} MB"
                else
                    let gb = mb / 1024.0
                    $"{gb:F2} GB"

    // Simple and robust line-by-line M3U8 Master Playlist parser
    let parseM3u8 (text: string) (baseUrl: string) : (string * string) list =
        if String.IsNullOrEmpty text then
            []
        else
            let lines = text.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            let mutable result = []
            let mutable i = 0

            while i < lines.Length do
                let line = lines.[i].Trim()

                if line.StartsWith("#EXT-X-STREAM-INF:") then
                    // Parse RESOLUTION and BANDWIDTH from attribute list
                    let info = line.Substring("#EXT-X-STREAM-INF:".Length)

                    let resolution =
                        let idx = info.IndexOf("RESOLUTION=")

                        if idx <> -1 then
                            let sub = info.Substring(idx + "RESOLUTION=".Length)
                            let endIdx = sub.IndexOf(",")
                            let resVal = if endIdx <> -1 then sub.Substring(0, endIdx) else sub
                            resVal + " "
                        else
                            ""

                    let bandwidth =
                        let idx = info.IndexOf("BANDWIDTH=")

                        if idx <> -1 then
                            let sub = info.Substring(idx + "BANDWIDTH=".Length)
                            let endIdx = sub.IndexOf(",")
                            let bwVal = if endIdx <> -1 then sub.Substring(0, endIdx) else sub

                            match Int64.TryParse(bwVal) with
                            | true, bw -> $"{bw / 1000L} Kbps "
                            | _ -> ""
                        else
                            ""

                    let quality = $"[HLS] {resolution}{bandwidth}".Trim()

                    // Read next non-comment line for the absolute/relative stream URL
                    if i + 1 < lines.Length then
                        let nextLine = lines.[i + 1].Trim()

                        if not (nextLine.StartsWith("#")) && not (String.IsNullOrEmpty nextLine) then
                            let absoluteUrl =
                                try
                                    Uri(Uri(baseUrl), nextLine).AbsoluteUri
                                with _ ->
                                    nextLine

                            result <- (quality, absoluteUrl) :: result
                            i <- i + 1

                i <- i + 1

            result |> List.rev

    // Fetch and parse HLS playlist or normal video headers asynchronously
    let processMediaRequest (dto: DownloadRequestDto) =
        task {
            let url = dto.url
            let tabId = dto.tabId
            let tabUrl = dto.tabUrl

            let nameFromUrl =
                try
                    let path = Uri(url).AbsolutePath

                    match Path.GetFileNameWithoutExtension(path) with
                    | null -> ""
                    | fn -> fn
                with _ ->
                    ""

            let title =
                if String.IsNullOrEmpty nameFromUrl then
                    "video"
                else
                    nameFromUrl

            if url.Contains(".m3u8") then
                use client = new HttpClient()

                // Propagate original request headers to bypass security checks
                for kv in dto.requestHeaders do
                    let key = kv.Key

                    if
                        not (
                            key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
                            || key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                            || key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                        )
                    then
                        let valHead = kv.Value |> List.tryHead |> Option.defaultValue ""

                        if not (String.IsNullOrEmpty valHead) then
                            client.DefaultRequestHeaders.TryAddWithoutValidation(key, valHead) |> ignore

                // Propagate User-Agent header
                dto.requestHeaders
                |> Map.tryFind "User-Agent"
                |> Option.bind List.tryHead
                |> Option.iter (fun v -> client.DefaultRequestHeaders.UserAgent.ParseAdd(v))

                try
                    let! text = client.GetStringAsync(url)
                    let variants = parseM3u8 text url

                    if not (List.isEmpty variants) then
                        for (quality, variantUrl) in variants do
                            let id = Guid.NewGuid().ToString("N")

                            let item =
                                { Id = id
                                  Text = title
                                  Info = quality
                                  TabId = tabId
                                  TabUrl = tabUrl
                                  Url = variantUrl
                                  Headers = dto.requestHeaders
                                  Cookies =
                                    if String.IsNullOrEmpty dto.cookies then
                                        None
                                    else
                                        Some dto.cookies }

                            videoRegistry.TryAdd(id, item) |> ignore
                    else
                        // Fallback: single variant stream
                        let id = Guid.NewGuid().ToString("N")

                        let item =
                            { Id = id
                              Text = title
                              Info = "[HLS TS]"
                              TabId = tabId
                              TabUrl = tabUrl
                              Url = url
                              Headers = dto.requestHeaders
                              Cookies =
                                if String.IsNullOrEmpty dto.cookies then
                                    None
                                else
                                    Some dto.cookies }

                        videoRegistry.TryAdd(id, item) |> ignore
                with ex ->
                    log.Warning("Failed to download or parse HLS playlist {Url}: {Message}", url, ex.Message)
            else
                // Normal Video detection
                let ext =
                    try
                        let path = Uri(url).AbsolutePath

                        match Path.GetExtension(path) with
                        | null -> "mp4"
                        | e -> e.Replace(".", "")
                    with _ ->
                        "mp4"

                let extDisplay =
                    if String.IsNullOrEmpty ext then
                        "MP4"
                    else
                        ext.ToUpperInvariant()

                let size =
                    dto.responseHeaders
                    |> Map.tryFind "content-length"
                    |> Option.bind List.tryHead
                    |> Option.bind (fun v ->
                        match Int64.TryParse(v) with
                        | true, sz -> Some sz
                        | _ -> None)

                let sizeStr =
                    match size with
                    | Some sz -> formatSize sz
                    | None -> ""

                let id = Guid.NewGuid().ToString("N")

                let item =
                    { Id = id
                      Text = title
                      Info = $"[{extDisplay}] {sizeStr}".Trim()
                      TabId = tabId
                      TabUrl = tabUrl
                      Url = url
                      Headers = dto.requestHeaders
                      Cookies =
                        if String.IsNullOrEmpty dto.cookies then
                            None
                        else
                            Some dto.cookies }

                videoRegistry.TryAdd(id, item) |> ignore
        }

    // Handles the /sync response by encoding the app configuration and active video list
    let handleSync (response: HttpListenerResponse) =
        task {
            let config = configStore.Current

            let videoList =
                videoRegistry.Values
                |> Seq.map (fun item ->
                    {| id = item.Id
                       text = item.Text
                       info = item.Info
                       tabId = item.TabId |})
                |> Seq.toList

            let syncResponse =
                {| enabled = true
                   fileExts = config.FileExtensions
                   blockedHosts = config.BlockedHosts
                   requestFileExts = config.VideoExtensions
                   videoList = videoList |}

            let json = JsonSerializer.Serialize(syncResponse, jsonOptions)
            let bytes = Encoding.UTF8.GetBytes(json)
            response.ContentType <- "application/json"
            response.ContentLength64 <- int64 bytes.Length
            do! response.OutputStream.WriteAsync(bytes, 0, bytes.Length)
            response.Close()
        }

    // Handles capture download requests
    let handleDownload (request: HttpListenerRequest) (response: HttpListenerResponse) =
        task {
            use reader = new StreamReader(request.InputStream, Encoding.UTF8)
            let! body = reader.ReadToEndAsync()
            let rawDto = JsonSerializer.Deserialize<DownloadRequestDto>(body, jsonOptions)

            match rawDto with
            | null -> ()
            | dto ->
                let dto = Sanitizer.sanitizeDto dto

                let mappedHeaders =
                    dto.requestHeaders
                    |> Map.map (fun _ v -> v |> List.tryHead |> Option.defaultValue "")

                let addReq =
                    { AddDownloadRequest.Url = Uri(dto.url)
                      FileName =
                        if String.IsNullOrEmpty dto.fileName then
                            None
                        else
                            Some dto.fileName
                      TargetFolder = None
                      Headers = mappedHeaders
                      Cookies =
                        if String.IsNullOrEmpty dto.cookies then
                            None
                        else
                            Some dto.cookies
                      Auth = NoAuth
                      Hash = None
                      StartImmediately = dto.silentDownload }

                let! _ = manager.AddAsync(addReq)
                ()

            do! handleSync response
        }

    // Handles media Sniff/Capture requests (offloaded HLS & Progressive Video)
    let handleMedia (request: HttpListenerRequest) (response: HttpListenerResponse) =
        task {
            use reader = new StreamReader(request.InputStream, Encoding.UTF8)
            let! body = reader.ReadToEndAsync()
            let rawDto = JsonSerializer.Deserialize<DownloadRequestDto>(body, jsonOptions)

            match rawDto with
            | null -> ()
            | dto ->
                let dto = Sanitizer.sanitizeDto dto
                // Asynchronously process the media request in the background
                Task.Run(fun () -> processMediaRequest dto :> Task) |> ignore

            do! handleSync response
        }

    // Handles trigger video download requests
    let handleVid (request: HttpListenerRequest) (response: HttpListenerResponse) =
        task {
            use reader = new StreamReader(request.InputStream, Encoding.UTF8)
            let! body = reader.ReadToEndAsync()
            let payload = JsonSerializer.Deserialize<VidPayload>(body, jsonOptions)

            match payload with
            | null -> response.StatusCode <- int HttpStatusCode.BadRequest
            | p ->
                if not (String.IsNullOrEmpty p.Vid) then
                    match videoRegistry.TryGetValue(p.Vid) with
                    | true, rawItem ->
                        let item = Sanitizer.sanitizeVideoItem rawItem

                        let mappedHeaders =
                            item.Headers |> Map.map (fun _ v -> v |> List.tryHead |> Option.defaultValue "")

                        let ext =
                            try
                                let path = Uri(item.Url).AbsolutePath
                                // AbsolutePath is non-nullable string, match only on GetExtension
                                match Path.GetExtension(path) with
                                | null -> ".mp4"
                                | e -> e
                            with _ ->
                                ".mp4"

                        let targetExt = if item.Info.Contains("[HLS") then ".ts" else ext

                        let addReq =
                            { AddDownloadRequest.Url = Uri(item.Url)
                              FileName = Some(item.Text + targetExt)
                              TargetFolder = None
                              Headers = mappedHeaders
                              Cookies = item.Cookies
                              Auth = NoAuth
                              Hash = None
                              StartImmediately = p.silentDownload }

                        let! _ = manager.AddAsync(addReq)
                        response.StatusCode <- int HttpStatusCode.OK
                    | false, _ -> response.StatusCode <- int HttpStatusCode.NotFound
                else
                    response.StatusCode <- int HttpStatusCode.BadRequest

            response.Close()
        }

    // Update video display text (titles) when browser tabs are updated
    let handleTabUpdate (request: HttpListenerRequest) (response: HttpListenerResponse) =
        task {
            use reader = new StreamReader(request.InputStream, Encoding.UTF8)
            let! body = reader.ReadToEndAsync()

            let payload = JsonSerializer.Deserialize<TabUpdatePayload>(body, jsonOptions)

            match payload with
            | null -> response.StatusCode <- int HttpStatusCode.BadRequest
            | p ->
                if not (String.IsNullOrEmpty p.TabUrl) && not (String.IsNullOrEmpty p.TabTitle) then
                    for kv in videoRegistry do
                        let item = kv.Value

                        if item.TabUrl = p.TabUrl then
                            let updated = { item with Text = p.TabTitle }
                            videoRegistry.TryUpdate(kv.Key, updated, item) |> ignore

                        response.StatusCode <- int HttpStatusCode.OK
                else
                    response.StatusCode <- int HttpStatusCode.BadRequest

            response.Close()
        }

    // Main request dispatcher routing endpoints to correct handlers
    let handleRequestContext (context: HttpListenerContext) =
        task {
            let request = context.Request
            let response = context.Response

            // Inject CORS headers
            response.Headers.Add("Access-Control-Allow-Origin", "*")
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type")

            if request.HttpMethod = "OPTIONS" then
                response.StatusCode <- int HttpStatusCode.OK
                response.Close()
            else
                match request.Url with
                | null ->
                    response.StatusCode <- int HttpStatusCode.BadRequest
                    response.Close()
                | url ->
                    match url.AbsolutePath with
                    | "/sync" -> do! handleSync response
                    | "/download" -> do! handleDownload request response
                    | "/media" -> do! handleMedia request response
                    | "/vid" -> do! handleVid request response
                    | "/tab-update" -> do! handleTabUpdate request response
                    | _ ->
                        response.StatusCode <- int HttpStatusCode.NotFound
                        response.Close()
        }

    // Binding server port dynamically
    let startListener () =
        let mutable port = 8597
        let mutable bound = false

        while port <= 8600 && not bound do
            try
                let prefix = $"http://127.0.0.1:{port}/"
                listener.Prefixes.Clear()
                listener.Prefixes.Add(prefix)
                listener.Start()
                activePort <- port
                bound <- true
                log.Information("Browser monitor listening on {Prefix}", prefix)
            with ex ->
                log.Warning("Failed to bind to port {Port}: {Message}", port, ex.Message)
                port <- port + 1

        if not bound then
            log.Error("Could not bind BrowserMonitor HttpListener to any port in range 8597-8600")

    // Async polling loop for HttpListener
    let rec loop () =
        task {
            try
                let! context = listener.GetContextAsync()
                // Process request concurrently
                Task.Run(fun () -> handleRequestContext context :> Task) |> ignore
                return! loop ()
            with ex ->
                if not cts.Token.IsCancellationRequested then
                    log.Error(ex, "HttpListener loop error, restarting listener loop")
                    return! loop ()
        }

    /// Exposes the active listening port
    member _.ActivePort = activePort

    /// Starts the asynchronous HttpListener loop
    member this.Start() =
        startListener ()

        if listener.IsListening then
            Task.Run(fun () -> loop () :> Task) |> ignore

    /// Safely terminates the HttpListener loop
    member _.Stop() =
        cts.Cancel()

        if listener.IsListening then
            listener.Stop()

        listener.Close()

    interface IDisposable with
        member this.Dispose() = this.Stop()

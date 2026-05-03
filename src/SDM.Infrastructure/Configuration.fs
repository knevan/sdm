namespace SDM.Infrastructure

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization

/// Shared JSON serialization options for all F# domain types.
/// Guarantee consistent serialization behavior
/// across Configuration and DownloadStore.
[<RequireQualifiedAccess>]
module JsonConfig =
    let options =
        let opts = JsonSerializerOptions()
        opts.WriteIndented <- true
        opts.Converters.Add(JsonFSharpConverter())
        opts

/// Application configuration with sensible defaults.
/// Replaces the monolithic Config.cs with immutable records
/// and atomic file-based persistence.
[<CLIMutable>]
type AppConfig =
    {
        /// Maximum number of concurrent download tasks
        MaxConcurrentDownloads: int
        /// Maximum number of segments (chunks) per download
        MaxSegmentsPerDownload: int
        /// Default download directory
        DefaultDownloadFolder: string
        /// Temporary files directory
        TempFolder: string
        /// Speed limit in KB/s (0 = unlimited)
        SpeedLimitKBps: int
        /// Minimum disk space threshold in MB before warning
        MinDiskSpaceMB: int64
        /// Whether to monitor clipboard for URLs
        MonitorClipboard: bool
        /// File extensions to auto-capture from browser
        FileExtensions: string list
        /// Video extensions to auto-capture
        VideoExtensions: string list
        /// Blocked hosts that should not be intercepted
        BlockedHosts: string list
        /// Proxy configuration (None = direct)
        Proxy: ProxyConfig option
        /// Whether to auto-start queued downloads on app launch
        AutoStartQueue: bool
        /// File conflict resolution strategy
        FileConflictMode: FileConflictMode
        /// Network timeout in seconds
        NetworkTimeoutSeconds: int
        /// Retry policy
        MaxRetries: int
        RetryDelaySeconds: int
    }

/// Proxy configuration
and [<CLIMutable>] ProxyConfig =
    { Host: string
      Port: int
      ProxyType: ProxyType
      Username: string option
      Password: string option }

and [<Struct>] ProxyType =
    | Http
    | Socks5
    | System

and [<Struct>] FileConflictMode =
    | AutoRename
    | Overwrite
    | Ask

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AppConfig =
    /// Default configuration values
    let defaults =
        { MaxConcurrentDownloads = 8
          MaxSegmentsPerDownload = 16
          DefaultDownloadFolder =
            Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, "Downloads")
          TempFolder = Path.Combine(Path.GetTempPath(), "SDM")
          SpeedLimitKBps = 0
          MinDiskSpaceMB = 100L
          MonitorClipboard = true
          FileExtensions =
            [ "zip"
              "exe"
              "msi"
              "7z"
              "rar"
              "tar"
              "gz"
              "iso"
              "dmg"
              "deb"
              "rpm"
              "pdf"
              "doc"
              "docx"
              "xls"
              "xlsx" ]
          VideoExtensions = [ "mp4"; "mkv"; "webm"; "avi"; "mov"; "flv"; "m4v"; "3gp" ]
          BlockedHosts = [ "update.googleapis.com"; "safebrowsing.googleapis.com" ]
          Proxy = None
          AutoStartQueue = true
          FileConflictMode = AutoRename
          NetworkTimeoutSeconds = 30
          MaxRetries = 3
          RetryDelaySeconds = 5 }

    /// Configuration file path (relative to app data directory)
    let private configPath () =
        let appData =
            Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ApplicationData, "SDM")

        Directory.CreateDirectory appData |> ignore
        Path.Combine(appData, "config.json")

    /// Load configuration from disk with fallback to defaults
    let load () =
        let path = configPath ()

        if File.Exists path then
            try
                let json = File.ReadAllText path

                match JsonSerializer.Deserialize<AppConfig>(json, JsonConfig.options) with
                | null -> defaults
                | config -> config
            with _ ->
                defaults
        else
            defaults

    /// Atomically save configuration to disk (write-rename pattern)
    let save (config: AppConfig) =
        let path = configPath ()
        let tempPath = path + ".tmp"
        let json = JsonSerializer.Serialize(config, JsonConfig.options)
        File.WriteAllText(tempPath, json)
        File.Move(tempPath, path, overwrite = true)

    /// Thread-safe mutable configuration holder
    type ConfigStore() =
        let mutable current = load ()
        let lockObj = obj ()

        /// Get current configuration snapshot
        member _.Current = current

        member _.Update(updater: AppConfig -> AppConfig) =
            lock lockObj (fun () ->
                let updated = updater current
                save updated
                current <- updated)

namespace SDM.UI

open System
open SDM.Domain
open SDM.Infrastructure

/// UI display wrapper for a DownloadEntry.
/// Decouples the domain record from view logic — computed properties
/// are derived once and stored, avoiding redundant formatting in the view.
type DownloadDisplayItem =
    { Id: Guid
      FileName: string
      Url: string
      SizeText: string
      TotalSizeBytes: int64
      SpeedText: string
      SpeedBps: int64
      EtaText: string
      EtaSeconds: float
      DateText: string
      AddedAt: DateTime
      Progress: float
      ProgressInt: int
      StatusText: string
      FileCategory: string
      CategoryName: string
      IsActive: bool
      IsPaused: bool
      IsCompleted: bool
      IsSelected: bool
      IsError: bool
      TargetPath: string }

    /// Create a display item from a Domain DownloadEntry
    static member FromEntry(entry: DownloadEntry) =
        let statusText, progress =
            match entry.Status with
            | Queue -> "Queued", 0.0
            | Starting -> "Starting", 0.0
            | Pausing -> "Pausing", -1.0
            | Paused -> "Paused", 0.0
            | Assembling -> "Assembling...", 99.0
            | Downloading(speed, _eta) ->
                let speedStr = Helpers.FormatHelper.formatSpeed (int64 speed)
                $"Downloading — {speedStr}", -1.0
            | Completed _ -> "Completed", 100.0
            | Error(_code, msg) -> $"Error: {msg}", -1.0

        let totalSize =
            match entry.TotalSize with
            | Some s -> int64 s
            | None -> 0L

        { Id = entry.Id
          FileName = entry.FileName
          Url = string entry.Url
          SizeText = Helpers.FormatHelper.formatSize totalSize
          TotalSizeBytes = totalSize
          SpeedText =
            match entry.Status with
            | Downloading(s, _) when s > 0L<Bps> -> Helpers.FormatHelper.formatSpeed (int64 s)
            | _ -> ""
          SpeedBps =
            match entry.Status with
            | Downloading(s, _) -> int64 s
            | _ -> 0L
          EtaText = ""
          EtaSeconds = Double.MaxValue
          DateText = entry.AddedAt.ToString("MMM dd")
          AddedAt = entry.AddedAt
          Progress = progress
          ProgressInt = progress |> int |> min 100 |> max 0
          StatusText = statusText
          FileCategory = Helpers.FormatHelper.getFileCategory entry.FileName
          CategoryName = Theme.CategoryDefs.categoryOf entry
          IsActive = statusText.StartsWith "Downloading" || statusText = "Starting"
          IsPaused = statusText = "Paused"
          IsCompleted = statusText = "Completed"
          IsSelected = false
          IsError = statusText.StartsWith "Error"
          TargetPath = entry.TargetPath }

/// Modal dialog types that can be open simultaneously or exclusively
type DialogState =
    | NoDialog
    | NewDownload of url: string * fileName: string * targetFolder: string * isSubmitting: bool
    | DeleteConfirm of id: Guid * fileName: string * deleteFiles: bool
    | DeleteConfirmMultiple of ids: Guid list * displayNames: string * deleteFiles: bool
    | SpeedLimiter of isEnabled: bool * limitKBps: int
    | DownloadComplete of filePath: string * folderPath: string * dontShowAgain: bool

/// Search query for filtering the download list
type SearchQuery =
    | SearchAll
    | SearchText of text: string

/// Top-level UI state — single immutable record for the entire application.
/// Follows the Elmish pattern: one Model, one Msg DU, one update function.
type Model =
    { Downloads: DownloadDisplayItem list
      SelectedDownload: Guid option
      SelectedCategory: string
      ActiveCount: int
      ActiveDialog: DialogState
      SearchQuery: SearchQuery
      StatusText: string
      ShowCompleteDialog: bool
      SpeedLimitKBps: int
      DownloadManager: SDM.Application.DownloadManager
      QueueScheduler: SDM.Application.QueueScheduler
      ConfigStore: AppConfig.ConfigStore
      BrowserMonitor: Helpers.BrowserMonitor
      ExpandedCategories: Set<string>
      SortColumn: string
      SortAscending: bool }

/// All user interactions and system events — the single message type driving the update loop.
type Msg =
    // ── Download lifecycle ──
    | AddNewDownload of url: string
    | StartDownload of id: Guid
    | PauseDownload of id: Guid
    | CancelDownload of id: Guid
    | RemoveDownload of id: Guid * deleteFiles: bool
    | RemoveDownloads of ids: Guid list * deleteFiles: bool
    | PauseSelected
    | ResumeSelected
    // ── UI / Dialogs ──
    | OpenNewDownloadDialog
    | CloseNewDownloadDialog
    | UpdateNewDownloadUrl of text: string
    | UpdateNewDownloadFileName of text: string
    | SubmitNewDownload
    | OpenDeleteConfirm of id: Guid * fileName: string
    | OpenDeleteConfirmMultiple of ids: Guid list * displayNames: string
    | CloseDeleteConfirm
    | SetDeleteFiles of deleteFiles: bool
    | OpenSpeedLimiter
    | CloseSpeedLimiter
    | ToggleSpeedLimit
    | UpdateSpeedLimit of kbPerSec: int
    | ApplySpeedLimit
    | ApplySpeedLimitWithValue of kbPerSec: int
    | OpenDownloadComplete of filePath: string * folderPath: string
    | CloseDownloadComplete
    | DontShowCompleteDialog
    // ── Selection & Sorting & Folding ──
    | SelectDownload of id: Guid option
    | SetSelectDownload of id: Guid * isSelected: bool
    | SetSelectAll of isSelected: bool
    | SelectCategory of category: string
    | ToggleCategoryFolder of parent: string
    | ToggleSort of column: string
    // ── Search ──
    | UpdateSearchQuery of text: string
    | ClearSearch
    // ── Engine events (from background threads, dispatched to UI) ──
    | EngineEvent of DownloadEvent
    // ── Lifecycle ──
    | LoadFromDatabase
    | RefreshList
    | Shutdown

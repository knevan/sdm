namespace SDM.Domain

open System
open System.Net

/// Units of measure
[<Measure>]
type B // Represents Bytes

[<Measure>]
type Bps // Represents Bytes per second

[<Measure>]
type s // Represents Seconds

/// Represents different hashing algorithms for file integrity verification
type HashAlgorithm =
    | MD5
    | SHA1
    | SHA256
    | SHA512

/// Status of an individual segment (chunk) of a download
type SegmentStatus =
    | Pending
    | Downloading of progress: float
    | Finished
    | Failed of reason: string

/// Status of the entire download task
type DownloadStatus =
    | Queue
    | Starting
    | Downloading of speed: int64<Bps> * eta: TimeSpan
    | Pausing
    | Paused
    | Assembling
    | Completed of finishedAt: DateTime
    | Error of code: string * message: string

/// Authentication info for protected resources
type AuthInfo =
    | Basic of user: string * pass: string
    | Bearer of token: string
    | NoAuth

/// Core metadata for a download segment
[<Struct>]
type Segment =
    { id: Guid
      Offset: int64<B>
      Length: int64<B>
      Downloaded: int64<B>
      Status: SegmentStatus }

/// The main domain record representing a Download Task
type DownloadEntry =
    { Id: Guid
      Url: Uri
      FileName: string
      TargetPath: string
      TempFolderPath: string
      TotalSize: int64<B> option
      AddedAt: DateTime
      Status: DownloadStatus
      Segments: Segment list
      Headers: Map<string, string>
      Cookies: string option
      Auth: AuthInfo
      Hash: (HashAlgorithm * string) option }

/// Commands for the Download Coordinator Actor
type DownloadCommand =
    | Start
    | Pause
    | Resume
    | Cancel
    | ForceRecheck
    | UpdateProgress of totalDownloaded: int64<B> * speed: int64<Bps>

/// Events emitted by the Engine to notify the UI or other systems
type DownloadEvent =
    | DownloadStarted of id: Guid
    | ProgressUpdated of id: Guid * progress: float * speed: int64<Bps>
    | DownloadFinished of id: Guid * finalPath: string
    | DownloadFailed of id: Guid * error: string

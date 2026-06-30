module SDM.UI.Helpers.FormatHelper

open System
open System.IO

let private sizeUnits = [| "B"; "KB"; "MB"; "GB"; "TB" |]
let private speedUnits = [| "B/s"; "KB/s"; "MB/s"; "GB/s" |]

/// Format byte count to human-readable size string (e.g. "1.5 MB")
let formatSize (bytes: int64) =
    if bytes <= 0L then "0 B"
    else
        let mutable order = 0
        let mutable size = float bytes

        while size >= 1024.0 && order < sizeUnits.Length - 1 do
            order <- order + 1
            size <- size / 1024.0

        $"{size:F2} {sizeUnits[order]}"

/// Format bytes-per-second to speed string (e.g. "2.5 MB/s")
let formatSpeed (bytesPerSecond: int64) =
    if bytesPerSecond <= 0L then "0 B/s"
    else
        let mutable order = 0
        let mutable speed = float bytesPerSecond

        while speed >= 1024.0 && order < speedUnits.Length - 1 do
            order <- order + 1
            speed <- speed / 1024.0

        $"{speed:F2} {speedUnits[order]}"

/// Format a TimeSpan as a human-readable remaining time
let formatEta (eta: TimeSpan) =
    if eta = TimeSpan.Zero || eta = TimeSpan.MaxValue then "∞"
    elif eta.TotalDays >= 1.0 then $"{int eta.TotalDays}d {eta.Hours}h"
    elif eta.TotalHours >= 1.0 then $"{int eta.TotalHours}h {eta.Minutes}m"
    elif eta.TotalMinutes >= 1.0 then $"{int eta.TotalMinutes}m {eta.Seconds}s"
    else $"{eta.Seconds}s"

/// Format progress percentage
let formatProgress (progress: float) =
    if progress >= 0.0 then $"{progress:F1}%%" else "-"

/// Extract file extension category for icon mapping
let getFileCategory (fileName: string) =
    let ext =
        Path.GetExtension(fileName)
        |> Option.ofObj
        |> Option.map (fun e -> e.ToLowerInvariant())
        |> Option.defaultValue ""

    match ext with
    | ".mp4"
    | ".mkv"
    | ".avi"
    | ".mov"
    | ".webm"
    | ".flv" -> "video"
    | ".mp3"
    | ".flac"
    | ".wav"
    | ".aac"
    | ".ogg"
    | ".m4a" -> "audio"
    | ".zip"
    | ".rar"
    | ".7z"
    | ".tar"
    | ".gz"
    | ".bz2" -> "archive"
    | ".exe"
    | ".msi"
    | ".dmg"
    | ".deb"
    | ".rpm"
    | ".appimage" -> "program"
    | ".pdf"
    | ".doc"
    | ".docx"
    | ".xls"
    | ".xlsx"
    | ".ppt" -> "document"
    | ".jpg"
    | ".jpeg"
    | ".png"
    | ".gif"
    | ".bmp"
    | ".svg"
    | ".webp" -> "image"
    | ".iso"
    | ".img" -> "disk"
    | _ -> "file"

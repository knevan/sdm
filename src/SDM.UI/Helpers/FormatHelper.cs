using System;
using System.IO;

namespace SDM.UI.Helpers;

/// <summary>
/// Formatting utilities for displaying download metrics.
/// </summary>
public static class FormatHelper
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];
    private static readonly string[] SpeedUnits = ["B/s", "KB/s", "MB/s", "GB/s"];

    /// <summary>
    /// Format byte count to size string
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";

        var order = 0;
        double size = bytes;

        while (size >= 1024 && order < SizeUnits.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {SizeUnits[order]}";
    }

    /// <summary>
    /// Format bytes-per-second to speed string
    /// </summary>
    public static string FormatSpeed(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "0 B/s";

        var order = 0;
        double speed = bytesPerSecond;

        while (speed >= 1024 && order < SpeedUnits.Length - 1)
        {
            order++;
            speed /= 1024;
        }

        return $"{speed:0.##} {SpeedUnits[order]}";
    }

    /// <summary>
    /// Format time span to human-readable time string
    /// </summary>
    public static string FormatEta(TimeSpan eta)
    {
        if (eta == TimeSpan.Zero || eta == TimeSpan.MaxValue)
            return "∞";

        if (eta.TotalDays >= 1)
            return $"{(int)eta.TotalDays}d {eta.Hours}h";

        if (eta.TotalHours >= 1)
            return $"{(int)eta.TotalHours}h {eta.Minutes}m";

        if (eta.TotalMinutes >= 1)
            return $"{(int)eta.TotalMinutes}m {eta.Seconds}s";

        return $"{eta.Seconds}s";
    }

    /// <summary>
    /// Format progress percentage with fixed width
    /// </summary>
    public static string FormatProgress(double progress)
        => progress >= 0 ? $"{progress:0.0}%" : "-";

    /// <summary>
    /// Extract file extension category for icon mapping
    /// </summary>
    public static string GetFileCategory(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".flv" => "video",
            ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".m4a" => "audio",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => "archive",
            ".exe" or ".msi" or ".dmg" or ".deb" or ".rpm" or ".appimage" => "program",
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" => "document",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".webp" => "image",
            ".iso" or ".img" => "disk",
            _ => "file"
        };
    }
}
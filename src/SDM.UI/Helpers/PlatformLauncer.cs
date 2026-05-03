
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SDM.UI.Helpers;

/// <summary>
/// Cross-platform utility for launching files and revealing folders.
/// Using Process.Start with shell execution for platform-agnostic behavior.
/// </summary>
public static class PlatformLauncher
{
    /// <summary>
    /// Open a file using the OS default application.
    /// Returns false if the file doesn't exist or the launch failed.
    /// </summary>
    public static bool OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Open the containing folder and optionally select the file.
    /// On Windows, uses explorer /select, on macOS open -R, on Linux xdg-open.
    /// </summary>
    public static bool OpenFolder(string folderPath, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var target = !string.IsNullOrEmpty(fileName)
                    ? Path.Combine(folderPath, fileName)
                    : folderPath;

                if (File.Exists(target))
                {
                    Process.Start("explorer.exe", $"/select,\"{target}\"");
                }
                else if (Directory.Exists(folderPath))
                {
                    Process.Start("explorer.exe", $"\"{folderPath}\"");
                }
                else
                {
                    return false;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var target = !string.IsNullOrEmpty(fileName)
                    ? Path.Combine(folderPath, fileName)
                    : folderPath;

                if (File.Exists(target))
                {
                    Process.Start("open", $"-R \"{target}\"");
                }
                else if (Directory.Exists(folderPath))
                {
                    Process.Start("open", $"\"{folderPath}\"");
                }
                else
                {
                    return false;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var target = !string.IsNullOrEmpty(fileName)
                    ? Path.Combine(folderPath, fileName)
                    : folderPath;

                if (File.Exists(target))
                {
                    Process.Start("xdg-open", $"\"{target}\"");
                }
                else if (Directory.Exists(folderPath))
                {
                    Process.Start("xdg-open", $"\"{folderPath}\"");
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (Directory.Exists(folderPath))
                {
                    Process.Start("xdg-open", $"\"{folderPath}\"");
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
};
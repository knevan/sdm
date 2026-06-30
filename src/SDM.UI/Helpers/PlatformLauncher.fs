namespace SDM.UI.Helpers

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices

/// Cross-platform utility for launching files and revealing folders
module PlatformLauncher =

    /// Open a file using the OS default application
    let openFile (filePath: string) =
        if String.IsNullOrWhiteSpace filePath || not (File.Exists filePath) then
            false
        else
            try
                Process.Start(
                    ProcessStartInfo(FileName = filePath, UseShellExecute = true)
                )
                |> ignore

                true
            with _ ->
                false

    /// Open the containing folder and optionally select the file
    let openFolder (folderPath: string) (fileName: string option) =
        if String.IsNullOrWhiteSpace folderPath then
            false
        else

            try
                if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                    let target =
                        match fileName with
                        | Some fn -> Path.Combine(folderPath, fn)
                        | None -> folderPath

                    if File.Exists target then
                        Process.Start("explorer.exe", $"/select,\"{target}\"") |> ignore
                        true
                    elif Directory.Exists folderPath then
                        Process.Start("explorer.exe", $"\"{folderPath}\"") |> ignore
                        true
                    else
                        false
                elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then
                    let target =
                        match fileName with
                        | Some fn -> Path.Combine(folderPath, fn)
                        | None -> folderPath

                    if File.Exists target then
                        Process.Start("open", $"-R \"{target}\"") |> ignore
                        true
                    elif Directory.Exists folderPath then
                        Process.Start("open", $"\"{folderPath}\"") |> ignore
                        true
                    else
                        false
                elif RuntimeInformation.IsOSPlatform OSPlatform.Linux then
                    let target =
                        match fileName with
                        | Some fn -> Path.Combine(folderPath, fn)
                        | None -> folderPath

                    if File.Exists target || Directory.Exists target then
                        Process.Start("xdg-open", $"\"{target}\"") |> ignore
                        true
                    else
                        false
                else
                    if Directory.Exists folderPath then
                        Process.Start("xdg-open", $"\"{folderPath}\"") |> ignore
                        true
                    else
                        false
            with _ ->
                false

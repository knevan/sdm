namespace SDM.UI

open System
open System.IO
open Avalonia
open Serilog

/// F# entry point for SDM — replaces the deleted Program.cs.
/// Uses the Avalonia AppBuilder pattern configured for desktop usage.
module Program =

    [<STAThread>]
    [<EntryPoint>]
    let main (args: string[]) =
        // Configure structured logging before anything else
        let logDir =
            Path.Combine(
                Environment.GetFolderPath Environment.SpecialFolder.ApplicationData,
                "SDM",
                "logs"
            )

        Directory.CreateDirectory logDir |> ignore

        Log.Logger <-
            LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(logDir, "sdm-.log"),
                    rollingInterval = RollingInterval.Day,
                    retainedFileCountLimit = 14,
                    buffered = true
                )
                .Destructure.FSharpTypes()
                .CreateLogger()

        Log.Information("SDM starting — version {Version}", "1.0.0")

        try
            AppBuilder
                .Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args)
        finally
            Log.Information("SDM shutting down")
            Log.CloseAndFlush()

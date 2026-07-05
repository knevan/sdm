namespace SDM.UI

open System
open System.IO
open System.Threading
open Avalonia
open Serilog

/// F# entry point for SDM — replaces the deleted Program.cs.
/// Uses the Avalonia AppBuilder pattern configured for desktop usage.
module Program =

    /// Named mutex name for single-instance guard
    [<Literal>]
    let private MutexName = "Global\SDM-Singleton-Mutex-4F3E2C1A"

    [<STAThread>]
    [<EntryPoint>]
    let main (args: string[]) =
        // Single-instance guard using named mutex
        let mutable createdNew = false
        let mutable mutex: Mutex option = None

        try
            mutex <- Some(new Mutex(true, MutexName, &createdNew))
        with ex ->
            Log.Warning(ex, "Failed to create singleton mutex, allowing instance")

        if createdNew && mutex.IsSome then
            // Configure structured logging before anything else
            let logDir =
                Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ApplicationData, "SDM", "logs")

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

                // Release the mutex so other instances can detect we've exited
                try
                    match mutex with
                    | Some m ->
                        m.ReleaseMutex()
                        m.Dispose()
                    | None -> ()
                with _ ->
                    ()
        else
            // ── Second instance — notify and exit ──
            Log.Warning("SDM is already running — second instance exiting")
            Console.Error.WriteLine("SDM is already running.")
            1 // Return non-zero exit code

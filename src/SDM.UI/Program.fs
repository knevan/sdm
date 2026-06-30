namespace SDM.UI

open System
open Avalonia

/// F# entry point for SDM — replaces the deleted Program.cs.
/// Uses the Avalonia AppBuilder pattern configured for desktop usage.
module Program =

    [<STAThread>]
    [<EntryPoint>]
    let main (args: string[]) =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args)

namespace SDM.UI

open System
open System.Threading.Channels
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.Threading
open Avalonia.FuncUI.Hosts
open SDM.UI.Views
open SDM.UI.Helpers
open Serilog

/// Dialog window configured to hide from the taskbar and center on owner
type NewDownloadDialogWindow(parent: Window, initialUrl: string, parentDispatch: Msg -> unit) as this
    =
    inherit HostWindow()

    let mutable isClosingSelf = false

    // Local dispatch modifier to handle auto-closing when complete
    let localDispatch (msg: Msg) =
        match msg with
        | SubmitNewDownload
        | CloseNewDownloadDialog ->
            isClosingSelf <- true
            this.Close()
        | _ -> ()

        parentDispatch msg

    do
        this.Title <- "New Download"
        this.Width <- 450.0
        this.Height <- 220.0
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner

        // Settings for Taskbar exclusion:
        this.ShowInTaskbar <- true
        this.Owner <- parent

        this.UpdateView(initialUrl, "")

    // Dynamically update UI inside dialog window when state changes
    member this.UpdateView(url: string, fileName: string) =
        (this :> IViewHost).Update(Some(Shell.newDownloadWindowContent url fileName localDispatch))

    override this.OnClosing(args) =
        // Sync back with Elmish state if closed via native OS close 'X' button
        if not isClosingSelf then
            isClosingSelf <- true
            parentDispatch CloseNewDownloadDialog

        base.OnClosing(args)

/// MainWindow
type MainWindow() as this =
    inherit HostWindow()

    let log = Log.ForContext<MainWindow>()
    let mutable modelRef: Model option = None
    let mutable clipboardWatcher: ClipboardWatcher option = None

    // Reference to track active native dialog window
    let mutable activeNewDownloadDialog: NewDownloadDialogWindow option = None

    let bootDispatch, bootChannel =
        let options = BoundedChannelOptions(256)
        options.FullMode <- BoundedChannelFullMode.Wait
        let ch = Channel.CreateBounded<Msg>(options)

        let d (msg: Msg) =
            try
                ch.Writer.TryWrite(msg) |> ignore
            with _ ->
                ()

        d, ch

    let initialModel, _ = State.init bootDispatch

    let rec dispatch (msg: Msg) =
        match modelRef with
        | Some m ->
            let newModel, cmd = State.update msg m
            modelRef <- Some newModel

            // Execute Elmish commands
            cmd |> List.iter (fun run -> run dispatch)

            // Platform specific dialog orchestration
            if
                System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows
                )
            then
                match msg with
                | OpenNewDownloadDialog ->
                    let dlg = NewDownloadDialogWindow(this, "", dispatch)
                    activeNewDownloadDialog <- Some dlg
                    dlg.ShowDialog(this) |> ignore
                | CloseNewDownloadDialog
                | SubmitNewDownload ->
                    activeNewDownloadDialog |> Option.iter (fun dlg -> dlg.Close())
                    activeNewDownloadDialog <- None
                | _ -> ()

            // Propagate updated states to the active native dialog if open
            activeNewDownloadDialog
            |> Option.iter (fun dlg ->
                match newModel.ActiveDialog with
                | NewDownload(url, fn, _, _) -> dlg.UpdateView(url, fn)
                | _ -> ())

            Dispatcher.UIThread.Post(fun () ->
                (this :> IViewHost).Update(Some(Shell.mainView newModel dispatch)))
        | None -> ()

    do
        base.Title <- "SDM — S Download Manager"
        base.Width <- 800.0
        base.Height <- 500.0
        base.MinWidth <- 600.0
        base.MinHeight <- 350.0
        base.WindowStartupLocation <- WindowStartupLocation.CenterScreen

        let rec flush () =
            match bootChannel.Reader.TryRead() with
            | true, _ -> flush ()
            | false, _ -> ()

        flush ()

        modelRef <- Some initialModel
        (this :> IViewHost).Update(Some(Shell.mainView initialModel dispatch))

        // Start clipboard monitoring (polls every 2s for URLs)
        let config = initialModel.ConfigStore.Current

        if config.MonitorClipboard then
            let watcher =
                new ClipboardWatcher(
                    this,
                    fun url ->
                        log.Information(
                            "Clipboard URL detected, opening new download dialog: {Url}",
                            url
                        )

                        dispatch (AddNewDownload url)
                )

            watcher.Start()
            clipboardWatcher <- Some watcher
            log.Information("Clipboard monitoring started")

    override this.OnClosing(args) =
        log.Information("Window closing — dispatching Shutdown")
        // Stop clipboard watcher first
        clipboardWatcher |> Option.iter (fun w -> (w :> IDisposable).Dispose())
        clipboardWatcher <- None
        dispatch Shutdown
        base.OnClosing args

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        // Load shared button / toolbar / column-header style classes
        let btnStyles =
            Avalonia.Markup.Xaml.Styling.StyleInclude(Uri("avares://SDM.UI/"))
        btnStyles.Source <- Uri("avares://SDM.UI/Styles/ButtonStyles.axaml")
        this.Styles.Add(btnStyles)


    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as d -> d.MainWindow <- MainWindow()
        | _ -> ()

        base.OnFrameworkInitializationCompleted()

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
open Serilog

/// MainWindow with Elmish-style dispatch loop.
type MainWindow() as this =
    inherit HostWindow()

    let log = Log.ForContext<MainWindow>()

    let mutable modelRef: Model option = None

    let bootDispatch, bootChannel =
        let options = BoundedChannelOptions(256)
        options.FullMode <- BoundedChannelFullMode.Wait
        let ch = Channel.CreateBounded<Msg>(options)
        let d (msg: Msg) = try ch.Writer.TryWrite(msg) |> ignore with _ -> ()
        d, ch

    let initialModel, _ = State.init bootDispatch

    let rec dispatch (msg: Msg) =
        match modelRef with
        | Some m ->
            let newModel, _ = State.update msg m
            modelRef <- Some newModel
            Dispatcher.UIThread.Post(fun () -> this.Content <- Shell.mainView newModel dispatch)
        | None -> ()

    do
        base.Title <- "SDM — S Download Manager"
        base.Width <- 1200.0; base.Height <- 550.0
        base.MinWidth <- 700.0; base.MinHeight <- 400.0
        base.WindowStartupLocation <- WindowStartupLocation.CenterScreen

        let rec flush () =
            match bootChannel.Reader.TryRead() with
            | true, _ -> flush () | false, _ -> ()
        flush ()

        modelRef <- Some initialModel
        this.Content <- Shell.mainView initialModel dispatch

    override this.OnClosing(args) =
        log.Information("Window closing — dispatching Shutdown")
        dispatch Shutdown
        base.OnClosing args

type App() =
    inherit Application()
    override this.Initialize() = this.Styles.Add(FluentTheme())
    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as d -> d.MainWindow <- MainWindow()
        | _ -> ()
        base.OnFrameworkInitializationCompleted()

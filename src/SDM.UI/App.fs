namespace SDM.UI

open System
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.Threading
open SDM.UI.Views

type App() =
    inherit Application()

    let mutable modelRef: Model option = None
    let mutable mainWindow: MainWindow option = None

    let rec executeCmd (cmd: Cmd) =
        match cmd with
        | CmdNone -> ()
        | CmdAsyncTask t ->
            let computation = Func<Task<Msg>>(fun () -> t ())

            Task.Run<Msg>(computation).ContinueWith(fun (t: Task<Msg>) ->
                if t.IsCompletedSuccessfully then
                    Dispatcher.UIThread.Post(fun () -> msgDispatch t.Result))
            |> ignore
        | CmdBatch cmds -> cmds |> List.iter executeCmd
        | CmdSubscribe subscribe -> subscribe msgDispatch |> ignore

    and msgDispatch (msg: Msg) =
        match modelRef with
        | None -> ()
        | Some current ->
            let newModel, cmd = State.update msg current
            modelRef <- Some newModel

            match mainWindow with
            | Some w -> w.RefreshFromModel newModel
            | None -> ()

            executeCmd cmd

    override this.Initialize() =
        this.Styles.Add(FluentTheme())

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let initialModel, initCmd = State.init ()
            modelRef <- Some initialModel

            let window = MainWindow(msgDispatch, initialModel)
            mainWindow <- Some window
            desktop.MainWindow <- window
            desktop.ShutdownRequested.Add(fun _ -> msgDispatch Shutdown)
            executeCmd initCmd

        | _ -> ()

        base.OnFrameworkInitializationCompleted()

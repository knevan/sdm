namespace SDM.UI.Helpers

open System
open System.Text.RegularExpressions
open System.Threading
open Avalonia.Threading
open Avalonia.FuncUI.Hosts
open Serilog

/// URL detection and clipboard monitoring utilities.
[<RequireQualifiedAccess>]
module ClipboardWatcher =

    let private urlPattern =
        Regex(
            @"https?://[^\s<>""']+?(?:\.[^\s<>""']+)+[^\s<>""'\]\)!,;]*",
            RegexOptions.Compiled ||| RegexOptions.IgnoreCase
        )

    /// Extract the first valid URL from text
    let extractUrl (text: string) : string option =
        if String.IsNullOrWhiteSpace text then
            None
        else
            let m = urlPattern.Match(text)

            if m.Success then
                let url = m.Value.TrimEnd('.', ',', ';', '!', '?', ')', ']')

                match Uri.TryCreate(url, UriKind.Absolute) with
                | true, uri when uri <> null && (uri.Scheme = "http" || uri.Scheme = "https") -> Some(string uri)
                | _ -> None
            else
                None

/// Clipboard polling watcher — runs on a background thread.
/// Polls clipboard text every N seconds, extracts URLs, and calls onUrlFound for each new URL.
type ClipboardWatcher(hostWindow: HostWindow, onUrlFound: string -> unit, ?pollIntervalMs: int) =

    let log = Log.ForContext<ClipboardWatcher>()
    let pollInterval = defaultArg pollIntervalMs 2000
    let cts = new CancellationTokenSource()
    let mutable lastUrl: string option = None

    /// Read clipboard text on the UI thread by posting to the dispatcher
    let getClipboardText () =
        try
            let clip = hostWindow.Clipboard

            if clip <> null then
                use waitHandle = new ManualResetEventSlim(false)
                let mutable result: string option = None

                Dispatcher.UIThread.Post(fun () ->
                    try
                        let t = clip.GetTextAsync()
                        let text = t.GetAwaiter().GetResult()
                        result <- if String.IsNullOrEmpty text then None else Some text
                    with _ ->
                        result <- None

                    waitHandle.Set())

                waitHandle.Wait(1000) |> ignore
                result
            else
                None
        with ex ->
            log.Warning(ex, "Error reading clipboard")
            None

    let pollingLoop =
        async {
            while not cts.IsCancellationRequested do
                try
                    match getClipboardText () with
                    | Some t ->
                        match ClipboardWatcher.extractUrl t with
                        | Some url when lastUrl <> Some url ->
                            log.Information("Clipboard URL detected: {Url}", url)
                            lastUrl <- Some url
                            onUrlFound url
                        | _ -> ()
                    | None -> ()
                with _ ->
                    ()

                do! Async.Sleep pollInterval
        }

    member _.Start() = Async.Start(pollingLoop, cts.Token)
    member _.Stop() = cts.Cancel()

    interface IDisposable with
        member _.Dispose() =
            cts.Cancel()
            cts.Dispose()

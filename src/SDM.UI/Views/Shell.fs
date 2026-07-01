namespace SDM.UI.Views

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open SDM.UI
open SDM.UI.Theme

module Shell =

    let private textForeground = Colors.textPrimaryBrush :> IBrush
    let private textSecondary = Colors.textSecondaryBrush :> IBrush
    let private textMuted = Colors.textMutedBrush :> IBrush
    let private surfaceBg = Colors.surfaceBrush :> IBrush
    let private surfaceBorder = Colors.surfaceBorderBrush :> IBrush
    let private primaryBg = Colors.primaryBrush :> IBrush
    let private dangerBg = Colors.dangerBrush :> IBrush
    let private successFg = Colors.successBrush :> IBrush
    let private warningFg = Colors.warningBrush :> IBrush
    let private white = Brushes.White :> IBrush

    let private modal (content: Avalonia.FuncUI.Types.IView list) =
        Grid.create [
            Grid.children [
                yield
                    Border.create [
                        Border.background "#CC0f172a"
                        Border.child (
                            Border.create [
                                Border.background surfaceBg
                                Border.cornerRadius (CornerRadius 8.0)
                                Border.padding (Thickness 24.0)
                                Border.horizontalAlignment HorizontalAlignment.Center
                                Border.verticalAlignment VerticalAlignment.Center
                                Border.child (
                                    StackPanel.create [
                                        StackPanel.spacing 12.0
                                        StackPanel.children content
                                    ]
                                )
                            ]
                        )
                    ] :> Avalonia.FuncUI.Types.IView
            ]
        ] :> Avalonia.FuncUI.Types.IView

    let private rowButtons (dispatch: Msg -> unit) (buttons: (string * IBrush * (unit -> unit)) list) =
        StackPanel.create [
            StackPanel.orientation Orientation.Horizontal
            StackPanel.horizontalAlignment HorizontalAlignment.Right
            StackPanel.spacing 8.0
            StackPanel.children [
                for (label, bg, action) in buttons do
                    Button.create [
                        Button.content label
                        Button.padding (Thickness(16.0, 6.0))
                        Button.onClick (fun _ -> action ())
                        Button.background bg
                        Button.foreground (if bg = surfaceBorder then textForeground else white)
                    ]
            ]
        ]

    let private checkbox (label: string) (isChecked: bool) (onToggle: unit -> unit) =
        CheckBox.create [
            CheckBox.content label
            CheckBox.isChecked isChecked
            CheckBox.foreground textForeground
            CheckBox.onIsCheckedChanged (fun _ -> onToggle ())
        ]

    let private label (text: string) (size: float) (weight: FontWeight) (fg: IBrush) =
        TextBlock.create [
            TextBlock.text text
            TextBlock.fontSize size
            TextBlock.fontWeight weight
            TextBlock.foreground fg
        ]

    let private caption (text: string) = label text 11.0 FontWeight.Normal textSecondary

    // ── Modals ──

    let private deleteConfirmModal (id: Guid) (fileName: string) (deleteFiles: bool) (dispatch: Msg -> unit) =
        modal [
            label $"Delete \"{fileName}\"?" 14.0 FontWeight.SemiBold textForeground
            label "This cannot be undone." 11.0 FontWeight.Normal textSecondary
            checkbox "Also delete files from disk" deleteFiles (fun () -> dispatch ToggleDeleteFiles)
            rowButtons dispatch [
                "Cancel", surfaceBorder, fun () -> dispatch CloseDeleteConfirm
                "Delete", dangerBg, fun () -> dispatch (RemoveDownload(id, deleteFiles))
            ]
        ]

    let private speedLimiterModal (enabled: bool) (limit: int) (dispatch: Msg -> unit) =
        modal [
            label "Speed Limiter" 16.0 FontWeight.Bold textForeground
            checkbox "Enable Speed Limit" enabled (fun () -> dispatch ToggleSpeedLimit)
            Slider.create [
                Slider.minimum 0.0; Slider.maximum 10000.0
                Slider.value (float limit); Slider.isEnabled enabled
                Slider.onValueChanged (fun v -> dispatch (UpdateSpeedLimit(int v)))
            ]
            label (if limit <= 0 then "Unlimited" else $"{limit} KB/s") 12.0 FontWeight.Normal textSecondary
            rowButtons dispatch [
                "Cancel", surfaceBorder, fun () -> dispatch CloseSpeedLimiter
                "Apply", primaryBg, fun () -> dispatch ApplySpeedLimit
            ]
        ]

    let private completeModal (filePath: string) (folderPath: string) (dispatch: Msg -> unit) =
        modal [
            label "Download Complete!" 16.0 FontWeight.Bold successFg
            StackPanel.create [
                StackPanel.orientation Orientation.Horizontal
                StackPanel.horizontalAlignment HorizontalAlignment.Center; StackPanel.spacing 8.0
                StackPanel.children [
                    Button.create [ Button.content "Open File"; Button.padding (Thickness(14.0,6.0)); Button.onClick (fun _ -> Helpers.PlatformLauncher.openFile filePath |> ignore; dispatch CloseDownloadComplete); Button.background primaryBg; Button.foreground white ]
                    Button.create [ Button.content "Open Folder"; Button.padding (Thickness(14.0,6.0)); Button.onClick (fun _ -> Helpers.PlatformLauncher.openFolder folderPath None |> ignore; dispatch CloseDownloadComplete); Button.background surfaceBorder; Button.foreground textForeground ]
                    Button.create [ Button.content "Close"; Button.padding (Thickness(14.0,6.0)); Button.onClick (fun _ -> dispatch CloseDownloadComplete); Button.background surfaceBorder; Button.foreground textForeground ]
                ]
            ]
            checkbox "Don't show this again" false (fun () -> dispatch DontShowCompleteDialog)
        ]

    let private newDownloadModal (url: string) (fn: string) (dispatch: Msg -> unit) =
        modal [
            label "New Download" 16.0 FontWeight.Bold textForeground
            caption "URL"
            TextBox.create [ TextBox.watermark "https://..."; TextBox.text url; TextBox.onTextChanged (fun t -> dispatch (UpdateNewDownloadUrl t)) ]
            caption "File Name (optional)"
            TextBox.create [ TextBox.watermark "filename"; TextBox.text fn; TextBox.onTextChanged (fun t -> dispatch (UpdateNewDownloadFileName t)) ]
            rowButtons dispatch [
                "Cancel", surfaceBorder, fun () -> dispatch CloseNewDownloadDialog
                "Download Now", primaryBg, fun () -> dispatch SubmitNewDownload
            ]
        ]

    let private modalOverlay (model: Model) (dispatch: Msg -> unit) =
        match model.ActiveDialog with
        | NoDialog -> Panel.create [] :> Avalonia.FuncUI.Types.IView
        | DeleteConfirm(id, fn, df) -> deleteConfirmModal id fn df dispatch
        | SpeedLimiter(enabled, limit) -> speedLimiterModal enabled limit dispatch
        | DownloadComplete(fp, fol, _) -> completeModal fp fol dispatch
        | NewDownload(url, fn, _, _) -> newDownloadModal url fn dispatch

    // ── Toolbar ──

    let private toolbar (model: Model) (dispatch: Msg -> unit) =
        Border.create [
            Border.padding Spacing.xs
            Border.background surfaceBg
            Border.borderBrush surfaceBorder
            Border.borderThickness (Thickness(0.0,0.0,0.0,1.0))
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        StackPanel.create [
                            StackPanel.dock Dock.Left
                            StackPanel.orientation Orientation.Horizontal; StackPanel.spacing 4.0
                            StackPanel.children [
                                for (label, bg, action) in [
                                    ("+ New", primaryBg, fun () -> dispatch OpenNewDownloadDialog)
                                    ("Pause", surfaceBorder, fun () -> match model.SelectedDownload with Some id -> dispatch (PauseDownload id) | None -> ())
                                    ("Resume", surfaceBorder, fun () -> match model.SelectedDownload with Some id -> dispatch (StartDownload id) | None -> ())
                                    ("Cancel", surfaceBorder, fun () -> match model.SelectedDownload with Some id -> dispatch (CancelDownload id) | None -> ())
                                    ("Remove", surfaceBorder, fun () -> match model.SelectedDownload with Some id -> dispatch (OpenDeleteConfirm(id, "download")) | None -> ())
                                    ("Speed", surfaceBorder, fun () -> dispatch OpenSpeedLimiter)
                                ] do
                                    Button.create [
                                        Button.content label
                                        Button.padding (if label = "+ New" then Thickness(12.0,6.0) else Thickness(8.0,6.0))
                                        Button.onClick (fun _ -> action ())
                                        Button.background bg
                                        Button.foreground (if bg = surfaceBorder then textForeground else white)
                                    ]
                            ]
                        ]
                        TextBox.create [
                            TextBox.dock Dock.Right
                            TextBox.watermark "Search..."
                            TextBox.width 180.0
                            TextBox.verticalAlignment VerticalAlignment.Center
                            TextBox.onTextChanged (fun t -> dispatch (UpdateSearchQuery(t |> Option.ofObj |> Option.defaultValue "")))
                        ]
                    ]
                ]
            )
        ]

    // ── Status Bar ──

    let private statusBar (model: Model) =
        Border.create [
            Border.padding Spacing.sm
            Border.background surfaceBg
            Border.borderBrush surfaceBorder
            Border.borderThickness (Thickness(0.0,1.0,0.0,0.0))
            Border.child (label model.StatusText 11.0 FontWeight.Normal textSecondary)
        ]

    // ── Download Row (single item in the virtualized list) ──

    let private downloadRow (item: DownloadDisplayItem) (dispatch: Msg -> unit) =
        let rowBg =
            if item.IsSelected then
                SolidColorBrush(Color.Parse "#1a3b82f6") :> IBrush
            else Brushes.Transparent :> IBrush

        let statusFg =
            if item.IsError then dangerBg
            elif item.IsCompleted then successFg
            elif item.IsPaused then warningFg
            else textSecondary

        // Show ETA only for active downloads with valid ETA
        let etaDisplay =
            if item.IsActive && not (String.IsNullOrEmpty item.EtaText) then
                label $"ETA: {item.EtaText}" 10.0 FontWeight.Normal textMuted :> Avalonia.FuncUI.Types.IView
            else Panel.create [] :> Avalonia.FuncUI.Types.IView

        Border.create [
            Border.padding (Thickness(12.0, 8.0))
            Border.borderBrush surfaceBorder
            Border.borderThickness (Thickness(0.0, 0.0, 0.0, 1.0))
            Border.background rowBg
            Border.onTapped (fun _ -> dispatch (SelectDownload(Some item.Id)))
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        // Left: file name + category
                        StackPanel.create [
                            StackPanel.dock Dock.Left; StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.children [
                                label item.FileName 12.0 FontWeight.SemiBold textForeground
                                // Sub-row: category + ETA on the same line
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.children [
                                        label item.FileCategory 10.0 FontWeight.Normal textMuted
                                        etaDisplay
                                    ]
                                ]
                            ]
                        ]

                        // Right: date column
                        TextBlock.create [
                            TextBlock.dock Dock.Right; TextBlock.text item.DateText
                            TextBlock.fontSize 11.0; TextBlock.foreground textMuted
                            TextBlock.verticalAlignment VerticalAlignment.Center; TextBlock.width 80.0
                        ]

                        // Progress + status column
                        StackPanel.create [
                            StackPanel.dock Dock.Right; StackPanel.verticalAlignment VerticalAlignment.Center; StackPanel.width 180.0
                            StackPanel.children [
                                label item.StatusText 11.0 FontWeight.Normal statusFg
                                // Progress bar: only for active/downloading items
                                if item.IsActive && item.ProgressInt > 0 && item.ProgressInt < 100 then
                                    ProgressBar.create [
                                        ProgressBar.minimum 0.0; ProgressBar.maximum 100.0
                                        ProgressBar.value (float item.ProgressInt); ProgressBar.height 4.0
                                        ProgressBar.margin (Thickness(0.0, 2.0, 0.0, 0.0))
                                    ]
                            ]
                        ]

                        // Speed column: only for active downloads
                        if item.IsActive && not (String.IsNullOrEmpty item.SpeedText) then
                            TextBlock.create [
                                TextBlock.dock Dock.Right; TextBlock.text item.SpeedText
                                TextBlock.fontSize 12.0
                                TextBlock.foreground primaryBg
                                TextBlock.verticalAlignment VerticalAlignment.Center; TextBlock.width 100.0
                            ]

                        // Size column
                        TextBlock.create [
                            TextBlock.dock Dock.Right; TextBlock.text item.SizeText
                            TextBlock.fontSize 12.0; TextBlock.foreground textSecondary
                            TextBlock.verticalAlignment VerticalAlignment.Center; TextBlock.width 100.0
                        ]
                    ]
                ]
            )
        ]

    // ── Download List (ScrollViewer — FuncUI virtual DOM handles update diffing) ──

    let private downloadList (model: Model) (dispatch: Msg -> unit) =
        ScrollViewer.create [
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.children [
                        for item in model.Downloads do
                            downloadRow item dispatch
                    ]
                ]
            )
        ]

    let private emptyState =
        Border.create [
            Border.verticalAlignment VerticalAlignment.Center
            Border.horizontalAlignment HorizontalAlignment.Center
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 8.0; StackPanel.horizontalAlignment HorizontalAlignment.Center
                    StackPanel.children [
                        label "No downloads yet" 14.0 FontWeight.SemiBold textSecondary
                        label "Click +New or press Ctrl+N to get started" 11.0 FontWeight.Normal textMuted
                    ]
                ]
            )
        ]

    // ── Keyboard Shortcuts ──

    let private handleKeyDown (model: Model) (dispatch: Msg -> unit) (e: KeyEventArgs) =
        let ctrl = e.KeyModifiers.HasFlag KeyModifiers.Control
        let shift = e.KeyModifiers.HasFlag KeyModifiers.Shift

        match e.Key with
        | Key.N when ctrl ->
            e.Handled <- true
            dispatch OpenNewDownloadDialog

        | Key.P when ctrl ->
            e.Handled <- true
            match model.SelectedDownload with
            | Some id -> dispatch (PauseDownload id)
            | None -> ()

        | Key.R when ctrl ->
            e.Handled <- true
            match model.SelectedDownload with
            | Some id -> dispatch (StartDownload id)
            | None -> ()

        | Key.Delete when not ctrl && not shift ->
            e.Handled <- true
            match model.SelectedDownload with
            | Some id ->
                let fileName =
                    model.Downloads
                    |> List.tryFind (fun d -> d.Id = id)
                    |> Option.map (fun d -> d.FileName)
                    |> Option.defaultValue "download"
                dispatch (OpenDeleteConfirm(id, fileName))
            | None -> ()

        | Key.OemQuestion ->
            e.Handled <- true
            // Focus search box — dispatch clear to set search mode
            dispatch (UpdateSearchQuery "")
            // The search TextBox will need to get focus via Avalonia's focus system
            // We just trigger the search query update

        | _ -> ()

    // ── Main View ──

    let mainView (model: Model) (dispatch: Msg -> unit) =
        DockPanel.create [
            DockPanel.lastChildFill true
            DockPanel.children [
                toolbar model dispatch
                statusBar model

                Grid.create [
                    Grid.focusable true
                    Grid.onKeyDown (handleKeyDown model dispatch)
                    Grid.children [
                        if model.Downloads.IsEmpty then
                            emptyState :> Avalonia.FuncUI.Types.IView
                        else
                            downloadList model dispatch :> Avalonia.FuncUI.Types.IView

                        match model.ActiveDialog with
                        | NoDialog -> Panel.create [] :> Avalonia.FuncUI.Types.IView
                        | _ -> modalOverlay model dispatch
                    ]
                ]
            ]
        ]

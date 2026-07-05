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
open Avalonia.VisualTree
open Avalonia.Platform.Storage

/// Structure: Sidebar (190px) | Main Panel (toolbar + list + status bar)
module Shell =
    // Check if running on Windows
    let private isWindows () =
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows
        )

    let private catBg = Colors.categoryListBgBrush :> IBrush
    let private catHl = Colors.categoryHighlightBrush :> IBrush
    let private catFg = Colors.categoryNormalBrush :> IBrush
    let private catHlFg = Colors.listSelectedTextBrush :> IBrush
    let private toolFg = Colors.toolButtonFgBrush :> IBrush
    let private mainBg = Colors.mainBgBrush :> IBrush
    let private sbBg = Colors.statusBarBgBrush :> IBrush
    let private sbIcon = Colors.statusBarIconBrush :> IBrush
    let private listText = Colors.listTextBrush :> IBrush
    let private listSelBg = Colors.listSelectedBgBrush :> IBrush
    let private progFill = Colors.progressFillBrush :> IBrush
    let private progBg = Colors.progressBgBrush :> IBrush
    let private dangerBg = Colors.dangerBrush :> IBrush
    let private successFg = Colors.successBrush :> IBrush
    let private warnFg = Colors.warningBrush :> IBrush
    let private border = Colors.borderBrush :> IBrush
    let private btnBg = Colors.buttonBgBrush :> IBrush
    let private textFg = Colors.textFgBrush :> IBrush
    let private txtInpBg = Colors.textInputBgBrush :> IBrush
    let private searchB = Colors.searchBgBrush :> IBrush
    let private disabledB = SolidColorBrush(Color.Parse "#404040") :> IBrush
    let private toolbarBg = Colors.toolbarBgBrush :> IBrush
    let private rowEvenBg = Colors.rowEvenBgBrush :> IBrush
    let private rowOddBg = Colors.rowOddBgBrush :> IBrush
    let private colHdrBg = Colors.colHeaderBgBrush :> IBrush
    let private accentBg = Colors.accentPrimaryBrush :> IBrush
    let private colDiv = Colors.colDividerBrush :> IBrush

    // ── helper ──
    let private lbl (text: string) (size: float) (weight: FontWeight) (fg: IBrush) =
        TextBlock.create
            [ TextBlock.text text
              TextBlock.fontSize size
              TextBlock.fontWeight weight
              TextBlock.foreground fg ]

    let private getCbChecked (e: Avalonia.Interactivity.RoutedEventArgs) : bool =
        let rec findToggle (v: Avalonia.Visual | null) =
            match Option.ofObj v with
            | None -> None
            | Some visual ->
                match visual with
                | :? ToggleButton as tb -> Some tb
                | other -> findToggle (other.GetVisualParent())

        match e.Source with
        | :? ToggleButton as tb -> tb.IsChecked.HasValue && tb.IsChecked.Value
        | :? Avalonia.Visual as v ->
            match findToggle v with
            | Some tb -> tb.IsChecked.HasValue && tb.IsChecked.Value
            | None -> false
        | _ -> false

    // Platform-specific dialog header layout
    let private modalHeader (title: string) (onClose: unit -> unit) =
        if isWindows () then
            DockPanel.create
                [ DockPanel.children
                      [ Button.create
                            [ Button.dock Dock.Right
                              Button.content "✕"
                              Button.padding (Thickness(4.0, 2.0))
                              Button.background Brushes.Transparent
                              Button.foreground toolFg
                              Button.borderThickness (Thickness 0.0)
                              Button.fontSize 11.0
                              Button.onClick (fun _ -> onClose ()) ]
                        TextBlock.create
                            [ TextBlock.dock Dock.Left
                              TextBlock.text title
                              TextBlock.fontSize 14.0
                              TextBlock.fontWeight FontWeight.Bold
                              TextBlock.foreground listText
                              TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
            :> Avalonia.FuncUI.Types.IView
        else
            TextBlock.create
                [ TextBlock.text title
                  TextBlock.fontSize 14.0
                  TextBlock.fontWeight FontWeight.Bold
                  TextBlock.foreground listText
                  TextBlock.horizontalAlignment HorizontalAlignment.Center
                  TextBlock.margin (Thickness(0.0, 0.0, 0.0, 8.0)) ]
            :> Avalonia.FuncUI.Types.IView

    let private sidebarHeader
        (title: string)
        (catName: string)
        (isExpanded: bool)
        (model: Model)
        (dispatch: Msg -> unit)
        =
        let isSel = model.SelectedCategory = catName
        let b = if isSel then catHl else Brushes.Transparent :> IBrush
        let f = if isSel then catHlFg else catFg

        Border.create
            [ Border.padding Spacing.sidebarItem
              Border.background b
              Border.cornerRadius (CornerRadius 3.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill true
                        DockPanel.children
                            [ // Expand/collapse arrow on the right
                              Button.create
                                  [ Button.dock Dock.Right
                                    Button.content (if isExpanded then "▲" else "▼")
                                    Button.fontSize 10.0
                                    Button.background Brushes.Transparent
                                    Button.foreground catFg
                                    Button.borderThickness (Thickness 0.0)
                                    Button.onClick (fun _ -> dispatch (ToggleCategoryFolder title)) ]

                              // Left icon and text
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text "📁"; TextBlock.fontSize 12.0; TextBlock.foreground f ]
                                          lbl title 12.0 FontWeight.Bold f ] ] ] ]
              )
              Border.onTapped (fun _ -> dispatch (SelectCategory catName)) ]

    let private sidebarSubItem (title: string) (catName: string) (icon: string) (model: Model) (dispatch: Msg -> unit) =
        let isSel = model.SelectedCategory = catName
        let b = if isSel then catHl else Brushes.Transparent :> IBrush
        let f = if isSel then catHlFg else catFg
        let iconBg = Icon.forCategory (icon.ToLowerInvariant())

        Border.create
            [ Border.padding Spacing.sidebarSubItem
              Border.background b
              Border.cornerRadius (CornerRadius 3.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children
                            [ Border.create
                                  [ Border.width 14.0
                                    Border.height 14.0
                                    Border.cornerRadius (CornerRadius 2.0)
                                    Border.background iconBg ]
                              lbl title 12.0 FontWeight.Normal f ] ]
              )
              Border.onTapped (fun _ -> dispatch (SelectCategory catName)) ]

    // ── SIDEBAR ──
    let private sidebar (model: Model) (dispatch: Msg -> unit) =
        Border.create
            [ Border.width 190.0
              Border.background catBg
              Border.borderBrush border
              Border.borderThickness (Thickness(0.0, 0.0, 1.0, 0.0))
              Border.child (
                  ScrollViewer.create
                      [ ScrollViewer.content (
                            StackPanel.create
                                [ StackPanel.spacing 2.0
                                  StackPanel.children
                                      [ // Group: All
                                        let allExpanded = model.ExpandedCategories.Contains "All"
                                        sidebarHeader "All" "ALL" allExpanded model dispatch

                                        if allExpanded then
                                            sidebarSubItem "Compressed" "CAT_COMPRESSED" "archive" model dispatch
                                            sidebarSubItem "Programs" "CAT_PROGRAMS" "program" model dispatch
                                            sidebarSubItem "Videos" "CAT_VIDEOS" "video" model dispatch
                                            sidebarSubItem "Music" "CAT_MUSIC" "music" model dispatch
                                            sidebarSubItem "Pictures" "CAT_PICTURES" "image" model dispatch
                                            sidebarSubItem "Documents" "CAT_DOCUMENTS" "document" model dispatch

                                        // Group: Finished
                                        let finExpanded = model.ExpandedCategories.Contains "Finished"
                                        sidebarHeader "Finished" "ALL_FINISHED" finExpanded model dispatch

                                        // Group: Unfinished
                                        let unfinExpanded = model.ExpandedCategories.Contains "Unfinished"
                                        sidebarHeader "Unfinished" "ALL_UNFINISHED" unfinExpanded model dispatch

                                        // Group: Queues
                                        let qExpanded = model.ExpandedCategories.Contains "Queues"
                                        sidebarHeader "Queues" "QUEUE_MAIN" qExpanded model dispatch

                                        if qExpanded then
                                            sidebarSubItem "Main" "QUEUE_MAIN" "other" model dispatch ] ]
                        ) ]
              ) ]

    // ── TOOLBAR (AB Download Manager style — icon + label vertical buttons) ──
    let private toolbar (model: Model) (dispatch: Msg -> unit) =
        let selectedItems = model.Downloads |> List.filter (fun d -> d.IsSelected)
        let hasSelection = not (List.isEmpty selectedItems)
        let anyActiveSelected = selectedItems |> List.exists (fun d -> d.IsActive)

        let anyInactiveSelected =
            selectedItems |> List.exists (fun d -> not d.IsActive && not d.IsCompleted)

        // Helper: icon+label vertical button — uses .toolbarIconBtn CSS class
        let iconBtn (icon: string) (label: string) (enabled: bool) (action: unit -> unit) =
            Button.create
                [ Button.classes [ "toolbarIconBtn" ]
                  Button.isEnabled enabled
                  Button.onClick (fun _ ->
                      if enabled then
                          action ())
                  Button.content (
                      StackPanel.create
                          [ StackPanel.orientation Orientation.Vertical
                            StackPanel.horizontalAlignment HorizontalAlignment.Center
                            StackPanel.spacing 3.0
                            StackPanel.children
                                [ TextBlock.create
                                      [ TextBlock.text icon
                                        TextBlock.fontSize 17.0
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.foreground toolFg ]
                                  TextBlock.create
                                      [ TextBlock.text label
                                        TextBlock.fontSize 10.0
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.foreground toolFg ] ] ]
                      :> Avalonia.FuncUI.Types.IView
                  ) ]

        // Helper: accent primary button ("New Download" in purple like AB)
        let primaryBtn (icon: string) (label: string) (action: unit -> unit) =
            Button.create
                [ Button.classes [ "toolbarIconBtnPrimary" ]
                  Button.onClick (fun _ -> action ())
                  Button.content (
                      StackPanel.create
                          [ StackPanel.orientation Orientation.Vertical
                            StackPanel.horizontalAlignment HorizontalAlignment.Center
                            StackPanel.spacing 3.0
                            StackPanel.children
                                [ TextBlock.create
                                      [ TextBlock.text icon
                                        TextBlock.fontSize 17.0
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.foreground Brushes.White ]
                                  TextBlock.create
                                      [ TextBlock.text label
                                        TextBlock.fontSize 10.0
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                                        TextBlock.foreground Brushes.White ] ] ]
                      :> Avalonia.FuncUI.Types.IView
                  ) ]

        // Thin vertical separator between button groups
        let sep () =
            Border.create
                [ Border.width 1.0
                  Border.margin (Thickness(4.0, 8.0, 4.0, 8.0))
                  Border.background colDiv ]
            :> Avalonia.FuncUI.Types.IView

        Border.create
            [ Border.dock Dock.Top
              Border.padding Spacing.toolbar
              Border.background toolbarBg
              Border.borderBrush border
              Border.borderThickness (Thickness(0.0, 0.0, 0.0, 1.0))
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill false
                        DockPanel.children
                            [ // Right side: search box (pill-style, like AB)
                              Border.create
                                  [ Border.dock Dock.Right
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.margin (Thickness(0.0, 0.0, 4.0, 0.0))
                                    Border.cornerRadius (CornerRadius 14.0)
                                    Border.background searchB
                                    Border.borderBrush border
                                    Border.borderThickness (Thickness 1.0)
                                    Border.child (
                                        DockPanel.create
                                            [ DockPanel.children
                                                  [ TextBlock.create
                                                        [ TextBlock.dock Dock.Left
                                                          TextBlock.text "🔍"
                                                          TextBlock.fontSize 11.0
                                                          TextBlock.foreground textFg
                                                          TextBlock.verticalAlignment VerticalAlignment.Center
                                                          TextBlock.margin (Thickness(10.0, 0.0, 4.0, 0.0)) ]
                                                    TextBox.create
                                                        [ TextBox.width 160.0
                                                          TextBox.height 26.0
                                                          TextBox.watermark "Search in the List"
                                                          TextBox.background Brushes.Transparent
                                                          TextBox.foreground listText
                                                          TextBox.borderThickness (Thickness 0.0)
                                                          TextBox.fontSize 11.0
                                                          TextBox.verticalContentAlignment VerticalAlignment.Center
                                                          TextBox.padding (Thickness(0.0, 0.0, 8.0, 0.0))
                                                          TextBox.onTextChanged (fun t ->
                                                              dispatch (
                                                                  UpdateSearchQuery(
                                                                      t |> Option.ofObj |> Option.defaultValue ""
                                                                  )
                                                              )) ] ] ]
                                    ) ]

                              // Left side: button groups
                              StackPanel.create
                                  [ StackPanel.dock Dock.Left
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 1.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ // Primary: New Download
                                          primaryBtn "⊕" "New Download" (fun () -> dispatch OpenNewDownloadDialog)

                                          sep ()

                                          // Group 1: Resume / Pause
                                          iconBtn "▶" "Resume" anyInactiveSelected (fun () -> dispatch ResumeSelected)
                                          iconBtn "⏸" "Pause" anyActiveSelected (fun () -> dispatch PauseSelected)

                                          sep ()

                                          // Group 2: Delete / Stop All
                                          iconBtn "🗑" "Delete" hasSelection (fun () ->
                                              let ids = selectedItems |> List.map (fun d -> d.Id)

                                              let displayNames =
                                                  if selectedItems.Length = 1 then
                                                      $"\"{selectedItems.[0].FileName}\""
                                                  else
                                                      $"{selectedItems.Length} items"

                                              dispatch (OpenDeleteConfirmMultiple(ids, displayNames)))
                                          iconBtn "⏹" "Stop All" (not (List.isEmpty model.Downloads)) (fun () ->
                                              dispatch PauseSelected)

                                          sep ()

                                          // Group 3: Settings
                                          iconBtn "⚙" "Settings" true (fun () -> dispatch OpenNewDownloadDialog) ] ] ] ]
              ) ]

    // Cache the column widths in a mutable array to persist them across renders.
    // Column layout:
    //   Col 3  Name      — 250px fixed, min 140
    //   Col 5  Size      — 72px fixed, min 54
    //   Col 7  Status    — 90px fixed, min 70
    //   Col 9  Speed     — 82px fixed, min 60
    //   Col 11 Time Left — 72px fixed, min 54
    //   Col 13 Date      — star (fills remaining space)
    let private colWidths =
        [| GridLength(180.0) // Name      (Col 3)
           GridLength(72.0) // Size      (Col 5)
           GridLength(90.0) // Status    (Col 7)
           GridLength(82.0) // Speed     (Col 9)
           GridLength(72.0) // Time Left (Col 11)
           GridLength(1.0, GridUnitType.Star) |] // Date (Col 13) — responsive star

    let private createHeaderColumns () =
        let cols =
            [ ColumnDefinition(Width = GridLength.Auto, SharedSizeGroup = "ColCheckbox") // Col 0  Checkbox
              ColumnDefinition(Width = GridLength(18.0), SharedSizeGroup = "ColGrip")
              ColumnDefinition(Width = GridLength(1.0), SharedSizeGroup = "ColSplitter0")
              ColumnDefinition(Width = colWidths.[0], MinWidth = 140.0, SharedSizeGroup = "ColName")
              ColumnDefinition(Width = GridLength(5.0), SharedSizeGroup = "ColSplitter1")
              ColumnDefinition(Width = colWidths.[1], MinWidth = 54.0, SharedSizeGroup = "ColSize")
              ColumnDefinition(Width = GridLength(5.0), SharedSizeGroup = "ColSplitter2")
              ColumnDefinition(Width = colWidths.[2], MinWidth = 70.0, SharedSizeGroup = "ColStatus")
              ColumnDefinition(Width = GridLength(5.0), SharedSizeGroup = "ColSplitter3")
              ColumnDefinition(Width = colWidths.[3], MinWidth = 60.0, SharedSizeGroup = "ColSpeed")
              ColumnDefinition(Width = GridLength(5.0), SharedSizeGroup = "ColSplitter4")
              ColumnDefinition(Width = colWidths.[4], MinWidth = 54.0, SharedSizeGroup = "ColTimeLeft") // Col 11 Time Left
              ColumnDefinition(Width = GridLength(5.0), SharedSizeGroup = "ColSplitter5")
              ColumnDefinition(Width = colWidths.[5], MinWidth = 80.0) ] // Col 13 Date

        // Subscribe to width changes so the sizes persist across Virtual-DOM re-renders
        let colIndices = [| 3; 5; 7; 9; 11; 13 |]

        for i in 0 .. colIndices.Length - 1 do
            cols[colIndices[i]].GetObservable(ColumnDefinition.WidthProperty).Subscribe(fun w -> colWidths[i] <- w)
            |> ignore

        let colDefs = ColumnDefinitions()
        cols |> List.iter colDefs.Add
        colDefs, cols

    // ── DOWNLOAD ROW (alternating rows, centered numeric columns) ──
    let private downloadRow
        (headerCols: ColumnDefinition list)
        (rowIndex: int)
        (item: DownloadDisplayItem)
        (dispatch: Msg -> unit)
        =
        // Selection overrides alternating background
        let rowBg =
            if item.IsSelected then listSelBg
            elif rowIndex % 2 = 0 then rowEvenBg
            else rowOddBg

        let stFg =
            if item.IsError then dangerBg
            elif item.IsCompleted then successFg
            elif item.IsPaused then warnFg
            else listText

        let iconBg = Icon.forCategory item.FileCategory

        let createRowGridColumns () =
            let colDefs = ColumnDefinitions()

            let addBoundCol (headerCol: ColumnDefinition) (minWidth: float) =
                let col = ColumnDefinition(MinWidth = minWidth)

                col.Bind(ColumnDefinition.WidthProperty, headerCol.GetObservable(ColumnDefinition.WidthProperty))
                |> ignore

                colDefs.Add(col)

            // Bind each row column directly to the corresponding header column by index
            addBoundCol headerCols.[0] 0.0 // Col 0: Checkbox
            addBoundCol headerCols.[1] 0.0 // Col 1: Grip
            addBoundCol headerCols.[2] 0.0 // Col 2: Splitter 0
            addBoundCol headerCols.[3] 25.0 // Col 3: Name
            addBoundCol headerCols.[4] 0.0 // Col 4: Splitter 1
            addBoundCol headerCols.[5] 25.0 // Col 5: Size
            addBoundCol headerCols.[6] 0.0 // Col 6: Splitter 2
            addBoundCol headerCols.[7] 25.0 // Col 7: Status
            addBoundCol headerCols.[8] 0.0 // Col 8: Splitter 3
            addBoundCol headerCols.[9] 25.0 // Col 9: Speed
            addBoundCol headerCols.[10] 0.0 // Col 10: Splitter 4
            addBoundCol headerCols.[11] 25.0 // Col 11: Time Left
            addBoundCol headerCols.[12] 0.0 // Col 12: Splitter 5
            addBoundCol headerCols.[13] 40.0 // Col 13: Date (Responsive Star Column)

            colDefs

        // Helper: right-aligned numeric text cell with text trimming support
        let numCell (col: int) (text: string) (fg: IBrush) =
            TextBlock.create
                [ Grid.column col
                  TextBlock.text text
                  TextBlock.fontSize 11.0
                  TextBlock.foreground fg
                  TextBlock.verticalAlignment VerticalAlignment.Center
                  TextBlock.horizontalAlignment HorizontalAlignment.Center
                  TextBlock.textAlignment TextAlignment.Center
                  TextBlock.textTrimming TextTrimming.CharacterEllipsis ]
            :> Avalonia.FuncUI.Types.IView

        Border.create
            [ Border.padding Spacing.row
              Border.background rowBg
              Border.onTapped (fun e ->
                  let isCheckboxSource =
                      match e.Source with
                      | :? CheckBox -> true
                      | :? Avalonia.Visual as v ->
                          // Traverse visual tree upwards to check if it's inside a CheckBox template
                          let rec isDescendantOfCb (curr: Avalonia.Visual | null) =
                              match Option.ofObj curr with
                              | None -> false
                              | Some visual ->
                                  match visual with
                                  | :? CheckBox -> true
                                  | other -> isDescendantOfCb (other.GetVisualParent())

                          isDescendantOfCb v
                      | _ -> false

                  if not isCheckboxSource then
                      dispatch (SelectDownload(Some item.Id)))
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (createRowGridColumns ())
                        Grid.children
                            [ // Col 0: Select checkbox
                              CheckBox.create
                                  [ Grid.column 0
                                    CheckBox.margin (Thickness(8.0, 0.0, 4.0, 0.0))
                                    CheckBox.isChecked item.IsSelected
                                    CheckBox.onTapped (fun e -> e.Handled <- true)
                                    CheckBox.onClick (fun e ->
                                        e.Handled <- true
                                        let newVal = getCbChecked e
                                        dispatch (SetSelectDownload(item.Id, newVal))) ]
                              |> View.withKey (string item.Id)

                              // Col 1: Drag-handle grip (⋮⋮)
                              TextBlock.create
                                  [ Grid.column 1
                                    TextBlock.text "⋮⋮"
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground colDiv
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.cursor (new Cursor(StandardCursorType.SizeAll))
                                    TextBlock.onPointerPressed (fun e ->
                                        if item.IsCompleted && System.IO.File.Exists item.TargetPath then
                                            match e.Source with
                                            | :? Visual as v ->
                                                match Option.ofObj (TopLevel.GetTopLevel(v)) with
                                                | Some topLevel ->
                                                    async {
                                                        // Get the file from local path asynchronously using StorageProvider
                                                        let! fileResult =
                                                            topLevel.StorageProvider.TryGetFileFromPathAsync(
                                                                item.TargetPath
                                                            )
                                                            |> Async.AwaitTask

                                                        match Option.ofObj fileResult with
                                                        | Some file ->
                                                            // Package the file item with the new DataTransfer API
                                                            let dtItem =
                                                                DataTransferItem.Create(
                                                                    DataFormat.File,
                                                                    file :> IStorageItem
                                                                )

                                                            let dataObj = new DataTransfer()
                                                            dataObj.Add(dtItem)

                                                            // Run the drag & drop operation asynchronously
                                                            let! _ =
                                                                DragDrop.DoDragDropAsync(
                                                                    e,
                                                                    dataObj,
                                                                    DragDropEffects.Copy
                                                                )
                                                                |> Async.AwaitTask

                                                            ()
                                                        | None -> ()
                                                    }
                                                    |> Async.StartImmediate
                                                | None -> ()
                                            | _ -> ()) ]

                              // Col 3: Name (icon square + filename + ETA sub-line)
                              DockPanel.create
                                  [ Grid.column 3
                                    DockPanel.lastChildFill true
                                    DockPanel.verticalAlignment VerticalAlignment.Center
                                    DockPanel.margin (Thickness(4.0, 0.0, 0.0, 0.0))
                                    DockPanel.children
                                        [ // File-type colour square
                                          Border.create
                                              [ Border.width 16.0
                                                Border.height 16.0
                                                Border.cornerRadius (CornerRadius 3.0)
                                                Border.background iconBg
                                                Border.margin (Thickness(0.0, 0.0, 7.0, 0.0)) ]

                                          StackPanel.create
                                              [ StackPanel.verticalAlignment VerticalAlignment.Center
                                                StackPanel.children
                                                    [ TextBlock.create
                                                          [ TextBlock.text item.FileName
                                                            TextBlock.fontSize 12.0
                                                            TextBlock.fontWeight FontWeight.SemiBold
                                                            TextBlock.foreground listText
                                                            TextBlock.textTrimming TextTrimming.CharacterEllipsis ]

                                                      if item.IsActive && not (String.IsNullOrEmpty item.EtaText) then
                                                          TextBlock.create
                                                              [ TextBlock.text ("ETA: " + item.EtaText)
                                                                TextBlock.fontSize 10.0
                                                                TextBlock.fontWeight FontWeight.Normal
                                                                TextBlock.foreground textFg
                                                                TextBlock.textTrimming TextTrimming.CharacterEllipsis ] ] ] ] ]

                              numCell 5 item.SizeText listText

                              numCell 7 item.StatusText stFg

                              numCell
                                  9
                                  (if item.IsActive && not (String.IsNullOrEmpty item.SpeedText) then
                                       item.SpeedText
                                   else
                                       "")
                                  (Colors.hyperlinkBrush :> IBrush)

                              numCell
                                  11
                                  (if item.IsActive && not (String.IsNullOrEmpty item.EtaText) then
                                       item.EtaText
                                   else
                                       "")
                                  listText

                              numCell 13 item.DateText textFg ] ]
              ) ]

    // ── COLUMN HEADER CELL (sort arrow inline, center alignment for numeric cols) ──
    let private headerCell
        (text: string)
        (colIdx: int)
        (colName: string)
        (isName: bool)
        (model: Model)
        (dispatch: Msg -> unit)
        =
        let isSorted = model.SortColumn = colName

        // Sort indicator: inline arrows beside column label
        let sortArrow =
            if isSorted then
                (if model.SortAscending then " ▲" else " ▼")
            else
                ""

        let halign =
            if isName then
                HorizontalAlignment.Left
            else
                HorizontalAlignment.Center

        Border.create
            [ Grid.column colIdx
              Border.classes [ "colHeaderCell" ]
              Border.padding Spacing.colHeader
              Border.onTapped (fun _ -> dispatch (ToggleSort colName))
              Border.child (
                  TextBlock.create
                      [ TextBlock.text (text + sortArrow)
                        TextBlock.fontSize 11.0
                        TextBlock.fontWeight FontWeight.SemiBold
                        TextBlock.foreground (if isSorted then listText else textFg)
                        TextBlock.verticalAlignment VerticalAlignment.Center
                        TextBlock.horizontalAlignment halign
                        TextBlock.textAlignment (if isName then TextAlignment.Left else TextAlignment.Center) ]
              ) ]

    // ── EMPTY STATE ──
    let private emptyState =
        StackPanel.create
            [ StackPanel.horizontalAlignment HorizontalAlignment.Center
              StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.spacing 8.0
              StackPanel.children
                  [ lbl "No downloads yet" 14.0 FontWeight.SemiBold listText
                    lbl "Click +New or press Ctrl+N to get started" 11.0 FontWeight.Normal textFg ] ]

    // ── DOWNLOAD LIST (header + alternating rows) ──
    let private downloadList (model: Model) (dispatch: Msg -> unit) =
        let filtered = State.applyFilters model model.Downloads

        let filteredAllSelected =
            not (List.isEmpty filtered) && filtered |> List.forall (fun d -> d.IsSelected)

        // "Select all" checkbox in the header
        let headerCheckbox =
            CheckBox.create
                [ Grid.column 0
                  CheckBox.margin (Thickness(8.0, 0.0, 4.0, 0.0))
                  CheckBox.isChecked filteredAllSelected
                  CheckBox.onTapped (fun e -> e.Handled <- true)
                  CheckBox.onClick (fun e ->
                      e.Handled <- true
                      let newVal = getCbChecked e
                      dispatch (SetSelectAll newVal)) ]

        // Thin visual column divider (not resizable — purely decorative)
        let divider (colIdx: int) =
            Border.create
                [ Grid.column colIdx
                  Border.width 1.0
                  Border.background colDiv
                  Border.horizontalAlignment HorizontalAlignment.Center
                  Border.verticalAlignment VerticalAlignment.Stretch
                  Border.isHitTestVisible false ]
            :> Avalonia.FuncUI.Types.IView

        let colDefs, headerColList = createHeaderColumns ()

        // Resizable splitter (using GridSplitter for native hover visual handle and cursor)
        let splitter (colIdx: int) (colWidthIndex: int) =
            Thumb.create
                [ Thumb.column colIdx
                  Thumb.classes [ "columnSplitter" ]
                  Thumb.width 5.0
                  Thumb.horizontalAlignment HorizontalAlignment.Center
                  Thumb.verticalAlignment VerticalAlignment.Stretch
                  Thumb.cursor (new Cursor(StandardCursorType.SizeWestEast))
                  Thumb.onDragDelta (fun e ->
                      e.Handled <- true
                      let delta = e.Vector.X
                      let currentWidth = colWidths[colWidthIndex].Value

                      let minVal =
                          if colWidthIndex = 0 then 140.0
                          elif colWidthIndex = 1 then 54.0
                          elif colWidthIndex = 2 then 70.0
                          elif colWidthIndex = 3 then 60.0
                          elif colWidthIndex = 4 then 54.0
                          else 80.0

                      let newWidth = max minVal (currentWidth + delta)

                      colWidths[colWidthIndex] <- GridLength(newWidth)
                      headerColList[3 + 2 * colWidthIndex].Width <- GridLength(newWidth)) ]

        // ── Column header row ──
        let colHeaders =
            Border.create
                [ Border.dock Dock.Top
                  Border.background colHdrBg
                  Border.borderBrush border
                  Border.borderThickness (Thickness(0.0, 0.0, 0.0, 1.0))
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions colDefs
                            Grid.children
                                [ headerCheckbox
                                  divider 2
                                  headerCell "Name" 3 "Name" true model dispatch
                                  divider 4
                                  splitter 4 0
                                  headerCell "Size" 5 "Size" false model dispatch
                                  divider 6
                                  splitter 6 1
                                  headerCell "Status" 7 "Status" false model dispatch
                                  divider 8
                                  splitter 8 2
                                  headerCell "Speed" 9 "Speed" false model dispatch
                                  divider 10
                                  splitter 10 3
                                  headerCell "Time Left" 11 "Time Left" false model dispatch
                                  divider 12
                                  splitter 12 4
                                  headerCell "Date Added" 13 "Date Added" false model dispatch ] ]
                  ) ]

        DockPanel.create
            [ DockPanel.lastChildFill true
              Grid.isSharedSizeScope true
              DockPanel.children
                  [ colHeaders
                    if List.isEmpty filtered then
                        // Empty state centred in the remaining area
                        Grid.create [ Grid.children [ emptyState :> Avalonia.FuncUI.Types.IView ] ]
                    else
                        ScrollViewer.create
                            [ ScrollViewer.content (
                                  StackPanel.create
                                      [ StackPanel.children
                                            // Pass rowIndex for alternating background
                                            [ for idx, item in filtered |> List.indexed do
                                                  downloadRow headerColList idx item dispatch ] ]
                              ) ] ] ]

    // ── STATUS BAR — replicates XDM bottom bar ──
    let private statusBar (model: Model) (dispatch: Msg -> unit) =
        Border.create
            [ Border.dock Dock.Bottom
              Border.padding Spacing.statusBar
              Border.background sbBg
              Border.borderBrush border
              Border.borderThickness (Thickness(0.0, 1.0, 0.0, 0.0))
              Border.child (
                  DockPanel.create
                      [ DockPanel.children
                            [
                              // Right: help
                              Button.create
                                  [ Button.dock Dock.Right
                                    Button.content "?"
                                    Button.padding (Thickness(6.0, 2.0))
                                    Button.background btnBg
                                    Button.foreground toolFg
                                    Button.borderBrush border
                                    Button.borderThickness (Thickness 1.0)
                                    Button.fontSize 12.0 ]
                              // Right: queue
                              Button.create
                                  [ Button.dock Dock.Right
                                    Button.content "📋"
                                    Button.padding (Thickness(6.0, 2.0))
                                    Button.background btnBg
                                    Button.foreground toolFg
                                    Button.borderBrush border
                                    Button.borderThickness (Thickness 1.0)
                                    Button.fontSize 12.0
                                    Button.margin (Thickness(0.0, 0.0, 4.0, 0.0)) ]
                              // Left: monitoring indicator
                              Button.create
                                  [ Button.dock Dock.Left
                                    Button.content "● Monitoring"
                                    Button.padding (Thickness(6.0, 2.0))
                                    Button.background btnBg
                                    Button.foreground sbIcon
                                    Button.borderBrush border
                                    Button.borderThickness (Thickness 1.0)
                                    Button.fontSize 10.0 ]
                              // Left: status text
                              TextBlock.create
                                  [ TextBlock.dock Dock.Left
                                    TextBlock.text model.StatusText
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground textFg
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.margin (Thickness(8.0, 0.0, 0.0, 0.0)) ] ] ]
              ) ]


    // ── MODALS ──
    let private modal (content: Avalonia.FuncUI.Types.IView list) =
        Grid.create
            [ Grid.children
                  [ Border.create
                        [ Border.background "#CC000000"
                          Border.child (
                              Border.create
                                  [ Border.background mainBg
                                    Border.cornerRadius (CornerRadius 6.0)
                                    Border.padding (Thickness 24.0)
                                    Border.horizontalAlignment HorizontalAlignment.Center
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.borderBrush border
                                    Border.borderThickness (Thickness 1.0)
                                    Border.child (
                                        StackPanel.create [ StackPanel.spacing 12.0; StackPanel.children content ]
                                    ) ]
                          ) ]
                    :> Avalonia.FuncUI.Types.IView ] ]
        :> Avalonia.FuncUI.Types.IView

    let private btn (text: string) (bg: IBrush) (fg: IBrush) (action: unit -> unit) =
        Button.create
            [ Button.content text
              Button.padding (Thickness(16.0, 6.0))
              Button.onClick (fun _ -> action ())
              Button.background bg
              Button.foreground fg ]

    let private rowBtns (dispatch: Msg -> unit) (buttons: (string * IBrush * (unit -> unit)) list) =
        let orderedButtons =
            if isWindows () then
                match buttons with
                | [ cancel; primary ] -> [ primary; cancel ]
                | _ -> buttons
            else
                buttons

        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.horizontalAlignment HorizontalAlignment.Right
              StackPanel.spacing 8.0
              StackPanel.children
                  [ for text, bg, action in orderedButtons do
                        btn text bg catHlFg action ] ]

    let private chk (lb: string) (isChecked: bool) (onChanged: bool -> unit) =
        CheckBox.create
            [ CheckBox.content lb
              CheckBox.isChecked isChecked
              CheckBox.foreground listText
              CheckBox.onTapped (fun e -> e.Handled <- true)
              CheckBox.onClick (fun e ->
                  e.Handled <- true
                  let newVal = getCbChecked e
                  onChanged newVal) ]

    let private deleteConfirmModal (id: Guid) (fileName: string) (deleteFiles: bool) (dispatch: Msg -> unit) =
        modal
            [ modalHeader ("Delete \"" + fileName + "\"?") (fun () -> dispatch CloseDeleteConfirm)
              lbl "This cannot be undone." 11.0 FontWeight.Normal textFg
              chk "Also delete files from disk" deleteFiles (fun newVal -> dispatch (SetDeleteFiles newVal))
              rowBtns
                  dispatch
                  [ ("Cancel", btnBg, fun () -> dispatch CloseDeleteConfirm)
                    ("Delete", dangerBg, fun () -> dispatch (RemoveDownload(id, deleteFiles))) ] ]

    let private deleteConfirmMultipleModal
        (ids: Guid list)
        (displayNames: string)
        (deleteFiles: bool)
        (dispatch: Msg -> unit)
        =
        modal
            [ modalHeader ("Delete " + displayNames + "?") (fun () -> dispatch CloseDeleteConfirm)
              lbl "This cannot be undone." 11.0 FontWeight.Normal textFg
              chk "Also delete files from disk" deleteFiles (fun newVal -> dispatch (SetDeleteFiles newVal))
              rowBtns
                  dispatch
                  [ ("Cancel", btnBg, fun () -> dispatch CloseDeleteConfirm)
                    ("Delete", dangerBg, fun () -> dispatch (RemoveDownloads(ids, deleteFiles))) ] ]

    let private speedLimiterModal (enabled: bool) (limit: int) (dispatch: Msg -> unit) =
        modal
            [ modalHeader "Speed Limiter" (fun () -> dispatch CloseSpeedLimiter)
              chk "Enable Speed Limit" enabled (fun _ -> dispatch ToggleSpeedLimit)
              Slider.create
                  [ Slider.minimum 0.0
                    Slider.maximum 10000.0
                    Slider.value (float limit)
                    Slider.isEnabled enabled
                    Slider.onValueChanged (fun v -> dispatch (UpdateSpeedLimit(int v))) ]
              lbl (if limit <= 0 then "Unlimited" else string limit + " KB/s") 12.0 FontWeight.Normal textFg
              rowBtns
                  dispatch
                  [ ("Cancel", btnBg, fun () -> dispatch CloseSpeedLimiter)
                    ("Apply", catHl, fun () -> dispatch ApplySpeedLimit) ] ]

    let private completeModal (filePath: string) (folderPath: string) (dispatch: Msg -> unit) =
        modal
            [ modalHeader "Download Complete!" (fun () -> dispatch CloseDownloadComplete)
              StackPanel.create
                  [ StackPanel.orientation Orientation.Horizontal
                    StackPanel.horizontalAlignment HorizontalAlignment.Center
                    StackPanel.spacing 8.0
                    StackPanel.children
                        [ btn "Open File" catHl catHlFg (fun () ->
                              Helpers.PlatformLauncher.openFile filePath |> ignore
                              dispatch CloseDownloadComplete)
                          btn "Open Folder" btnBg listText (fun () ->
                              Helpers.PlatformLauncher.openFolder folderPath None |> ignore
                              dispatch CloseDownloadComplete)
                          btn "Close" btnBg listText (fun () -> dispatch CloseDownloadComplete) ] ]
              chk "Don't show this again" false (fun _ -> dispatch DontShowCompleteDialog) ]

    let private newDownloadControls (url: string) (fn: string) (dispatch: Msg -> unit) =
        [ modalHeader "New Download" (fun () -> dispatch CloseNewDownloadDialog)
          lbl "URL" 11.0 FontWeight.Normal textFg
          TextBox.create
              [ TextBox.watermark "https://..."
                TextBox.text url
                TextBox.onTextChanged (fun t -> dispatch (UpdateNewDownloadUrl t))
                TextBox.background txtInpBg
                TextBox.foreground listText
                TextBox.borderBrush border
                TextBox.borderThickness (Thickness 1.0) ]
          lbl "File Name (optional)" 11.0 FontWeight.Normal textFg
          TextBox.create
              [ TextBox.watermark "filename"
                TextBox.text fn
                TextBox.onTextChanged (fun t -> dispatch (UpdateNewDownloadFileName t))
                TextBox.background txtInpBg
                TextBox.foreground listText
                TextBox.borderBrush border
                TextBox.borderThickness (Thickness 1.0) ]
          rowBtns
              dispatch
              [ ("Cancel", btnBg, fun () -> dispatch CloseNewDownloadDialog)
                ("DownloadNow", catHl, fun () -> dispatch SubmitNewDownload) ] ]

    // Overlay style for macOS and Linux
    let private newDownloadModal (url: string) (fn: string) (dispatch: Msg -> unit) =
        modal (newDownloadControls url fn dispatch)

    // Exported content container for native dialog window on Windows
    let newDownloadWindowContent (url: string) (fn: string) (dispatch: Msg -> unit) =
        Border.create
            [ Border.background mainBg
              Border.padding (Thickness 24.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 12.0
                        StackPanel.children (newDownloadControls url fn dispatch) ]
              ) ]

    let private modalOverlay (model: Model) (dispatch: Msg -> unit) =
        match model.ActiveDialog with
        | NoDialog -> Panel.create [] :> Avalonia.FuncUI.Types.IView
        | DeleteConfirm(id, fn, df) -> deleteConfirmModal id fn df dispatch
        | DeleteConfirmMultiple(ids, displayNames, df) -> deleteConfirmMultipleModal ids displayNames df dispatch
        | SpeedLimiter(enabled, limit) -> speedLimiterModal enabled limit dispatch
        | DownloadComplete(fp, fol, _) -> completeModal fp fol dispatch
        | NewDownload(url, fn, _, _) -> newDownloadModal url fn dispatch

    // ── KEYBOARD SHORTCUTS ──
    let private handleKeyDown (model: Model) (dispatch: Msg -> unit) (e: KeyEventArgs) =
        let ctrl = e.KeyModifiers.HasFlag KeyModifiers.Control

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
        | Key.Delete when not ctrl ->
            e.Handled <- true

            match model.SelectedDownload with
            | Some id ->
                let fn =
                    model.Downloads
                    |> List.tryFind (fun d -> d.Id = id)
                    |> Option.map (fun d -> d.FileName)
                    |> Option.defaultValue "download" in

                dispatch (OpenDeleteConfirm(id, fn))
            | None -> ()
        | Key.OemQuestion ->
            e.Handled <- true
            dispatch (UpdateSearchQuery "")
        | _ -> ()

    // ── MAIN VIEW (two-column grid) ──
    let mainView (model: Model) (dispatch: Msg -> unit) =
        Grid.create
            [ Grid.focusable true
              Grid.onKeyDown (handleKeyDown model dispatch)
              Grid.columnDefinitions "190,*"
              Grid.children
                  [
                    // Column 0: Sidebar
                    Grid.create [ Grid.column 0; Grid.children [ sidebar model dispatch ] ]

                    // Column 1: Main panel (Toolbar at Top, Status Bar at Bottom, Content in Center)
                    Grid.create
                        [ Grid.column 1
                          Grid.children
                              [ DockPanel.create
                                    [ DockPanel.lastChildFill true
                                      DockPanel.children
                                          [ toolbar model dispatch
                                            statusBar model dispatch
                                            Grid.create
                                                [ Grid.children
                                                      [ downloadList model dispatch :> Avalonia.FuncUI.Types.IView ] ] ] ] ] ]

                    // Overlay modal at root level spanning all columns (covers sidebar + main area)
                    match model.ActiveDialog with
                    | NoDialog -> Panel.create [] :> Avalonia.FuncUI.Types.IView
                    | NewDownload _ -> Panel.create [] :> Avalonia.FuncUI.Types.IView
                    | _ ->
                        Grid.create
                            [ Grid.column 0
                              Grid.columnSpan 2
                              Grid.children [ modalOverlay model dispatch ] ]
                        :> Avalonia.FuncUI.Types.IView ] ]

namespace SDM.UI.Views

open System.Collections.ObjectModel
open Avalonia.Controls
open Avalonia.Layout
open SDM.UI
open SDM.UI.Theme

type MainWindow(dispatch: Msg -> unit, initialModel: Model) as this =
    inherit Window()

    let mutable currentModel = initialModel

    let downloadItems = ObservableCollection<DownloadDisplayItem>()

    let dataGrid =
        DataGrid(AutoGenerateColumns = false, IsReadOnly = true, ItemsSource = downloadItems)

    let statusTextBlock =
        let tb = TextBlock()
        tb.Text <- "Ready"
        tb.FontSize <- 12.0
        tb.Opacity <- 0.7
        tb.VerticalAlignment <- VerticalAlignment.Center
        tb

    let mkButton text pad =
        let btn = Button()
        btn.Content <- text
        btn.Padding <- pad
        btn

    let btnNewDownload = mkButton "+ New Download" Spacing.md
    let btnPause = mkButton "Pause" Spacing.sm
    let btnResume = mkButton "Resume" Spacing.sm
    let btnCancel = mkButton "Cancel" Spacing.sm
    let btnRemove = mkButton "Remove" Spacing.sm

    let searchBox =
        let tb = TextBox()
        tb.PlaceholderText <- "Search downloads..."
        tb.Width <- 200.0
        tb.VerticalAlignment <- VerticalAlignment.Center
        tb

    do
        this.Title <- "SDM — S Download Manager"
        this.Width <- 1200.0
        this.Height <- 550.0
        this.MinWidth <- 700.0
        this.MinHeight <- 400.0
        this.WindowStartupLocation <- WindowStartupLocation.CenterScreen

        // Event wiring
        btnNewDownload.Click.Add(fun _ -> dispatch OpenNewDownloadDialog)
        btnPause.Click.Add(fun _ ->
            match currentModel.SelectedDownload with
            | Some id -> dispatch (PauseDownload id)
            | None -> ())
        btnResume.Click.Add(fun _ ->
            match currentModel.SelectedDownload with
            | Some id -> dispatch (StartDownload id)
            | None -> ())
        btnCancel.Click.Add(fun _ ->
            match currentModel.SelectedDownload with
            | Some id -> dispatch (CancelDownload id)
            | None -> ())
        btnRemove.Click.Add(fun _ ->
            match currentModel.SelectedDownload with
            | Some id -> dispatch (OpenDeleteConfirm(id, "download"))
            | None -> ())

        searchBox.TextChanged.Add(fun _ -> dispatch (UpdateSearchQuery(searchBox.Text |> Option.ofObj |> Option.defaultValue "")))

        dataGrid.SelectionChanged.Add(fun _ ->
            match dataGrid.SelectedItem with
            | :? DownloadDisplayItem as item -> dispatch (SelectDownload(Some item.Id))
            | _ -> dispatch (SelectDownload None))

        // Columns
        dataGrid.Columns.Add(DataGridTextColumn(Header = "Name", Binding = Avalonia.Data.Binding("FileName"), Width = DataGridLength(1.5, DataGridLengthUnitType.Star)))
        dataGrid.Columns.Add(DataGridTextColumn(Header = "Size", Binding = Avalonia.Data.Binding("SizeText"), Width = DataGridLength(80.0)))
        dataGrid.Columns.Add(DataGridTextColumn(Header = "Speed", Binding = Avalonia.Data.Binding("SpeedText"), Width = DataGridLength(90.0)))
        dataGrid.Columns.Add(DataGridTextColumn(Header = "Status", Binding = Avalonia.Data.Binding("StatusText"), Width = DataGridLength(1.0, DataGridLengthUnitType.Star)))
        dataGrid.Columns.Add(DataGridTextColumn(Header = "Added", Binding = Avalonia.Data.Binding("DateText"), Width = DataGridLength(90.0)))

        // Toolbar
        let toolbar = Border(Padding = Spacing.xs)
        let toolbarGrid = Grid()

        toolbarGrid.ColumnDefinitions.Add(ColumnDefinition(GridLength.Auto))
        toolbarGrid.ColumnDefinitions.Add(ColumnDefinition(GridLength.Star))
        toolbarGrid.ColumnDefinitions.Add(ColumnDefinition(GridLength.Auto))

        let btns = StackPanel(Orientation = Orientation.Horizontal, Spacing = 4.0)
        btns.Children.Add(btnNewDownload)
        btns.Children.Add(btnPause)
        btns.Children.Add(btnResume)
        btns.Children.Add(btnCancel)
        btns.Children.Add(btnRemove)

        Grid.SetColumn(btns, 0)
        toolbarGrid.Children.Add(btns)
        Grid.SetColumn(searchBox, 2)
        toolbarGrid.Children.Add(searchBox)
        toolbar.Child <- toolbarGrid

        // Status bar
        let statusBar = Border(Padding = Spacing.sm)
        statusBar.Child <- statusTextBlock

        // Layout
        let dock = DockPanel()
        DockPanel.SetDock(toolbar, Dock.Top)
        DockPanel.SetDock(statusBar, Dock.Bottom)
        dock.Children.Add(toolbar)
        dock.Children.Add(statusBar)
        dock.Children.Add(dataGrid)
        this.Content <- dock

        this.RefreshFromModel initialModel

    member _.RefreshFromModel(model: Model) =
        currentModel <- model
        downloadItems.Clear()

        for item in model.Downloads do
            downloadItems.Add(item)

        statusTextBlock.Text <- model.StatusText

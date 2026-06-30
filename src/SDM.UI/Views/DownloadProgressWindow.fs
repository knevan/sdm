namespace SDM.UI.Views

open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout

type DownloadProgressWindow() as this =
    inherit Window()

    do
        this.Title <- "Download Progress"
        this.Width <- 600.0
        this.Height <- 380.0
        this.MinWidth <- 440.0
        this.MinHeight <- 280.0
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner

        let dock = DockPanel()

        let header =
            Border(Padding = Avalonia.Thickness(12.0, 8.0))

        let fileNameBlock =
            TextBlock(Text = "Download Progress", FontSize = 14.0, FontWeight = Avalonia.Media.FontWeight.SemiBold)

        let urlBlock =
            TextBlock(Text = "...", FontSize = 11.0, Opacity = 0.6, Margin = Avalonia.Thickness(0.0, 4.0, 0.0, 0.0))

        let headerGrid = Grid()

        headerGrid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        headerGrid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        headerGrid.ColumnDefinitions.Add(ColumnDefinition(GridLength.Star))
        headerGrid.ColumnDefinitions.Add(ColumnDefinition(GridLength.Auto))

        Grid.SetRow(fileNameBlock, 0)
        Grid.SetRow(urlBlock, 1)
        headerGrid.Children.Add(fileNameBlock)
        headerGrid.Children.Add(urlBlock)
        header.Child <- headerGrid
        DockPanel.SetDock(header, Dock.Top)

        let progressSection =
            StackPanel(Margin = Avalonia.Thickness(16.0, 12.0), Spacing = 8.0)

        let progressBar =
            ProgressBar(Minimum = 0.0, Maximum = 100.0, Value = 0.0, Height = 10.0)

        let progressInfo = Grid(Margin = Avalonia.Thickness(0.0, 4.0, 0.0, 0.0))

        progressInfo.ColumnDefinitions.Add(ColumnDefinition(GridLength.Star))
        progressInfo.ColumnDefinitions.Add(ColumnDefinition(GridLength.Star))
        progressInfo.ColumnDefinitions.Add(ColumnDefinition(GridLength.Star))

        let progressPct = TextBlock(Text = "0%", HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12.0)
        let progressBytes = TextBlock(Text = "0 B", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12.0)
        let progressSpeed = TextBlock(Text = "0 B/s", HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12.0)

        Grid.SetColumn(progressPct, 0)
        Grid.SetColumn(progressBytes, 1)
        Grid.SetColumn(progressSpeed, 2)
        progressInfo.Children.Add(progressPct)
        progressInfo.Children.Add(progressBytes)
        progressInfo.Children.Add(progressSpeed)

        progressSection.Children.Add(progressBar)
        progressSection.Children.Add(progressInfo)

        let actionBar =
            StackPanel(
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10.0,
                Margin = Avalonia.Thickness(16.0, 8.0, 16.0, 12.0)
            )

        let btnPause = Button(Content = "Pause", Padding = Avalonia.Thickness(12.0, 5.0))
        let btnStop = Button(Content = "Stop", Padding = Avalonia.Thickness(12.0, 5.0))
        let btnOpenFolder = Button(Content = "Open Folder", Padding = Avalonia.Thickness(12.0, 5.0))
        actionBar.Children.Add(btnPause)
        actionBar.Children.Add(btnStop)
        actionBar.Children.Add(btnOpenFolder)
        DockPanel.SetDock(actionBar, Dock.Bottom)

        dock.Children.Add(header)
        dock.Children.Add(progressSection)
        dock.Children.Add(actionBar)
        this.Content <- dock

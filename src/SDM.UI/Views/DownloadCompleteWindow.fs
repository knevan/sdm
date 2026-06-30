namespace SDM.UI.Views

open Avalonia.Controls
open Avalonia.Layout

type DownloadCompleteWindow() as this =
    inherit Window()

    do
        this.Title <- "Download Complete"
        this.Width <- 400.0
        this.Height <- 160.0
        this.CanResize <- false
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.ShowInTaskbar <- false

        let grid = Grid(Margin = Avalonia.Thickness(16.0))

        grid.RowDefinitions.Add(RowDefinition(GridLength.Star))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))

        let message =
            TextBlock(Text = "Download completed successfully!", FontSize = 14.0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center)

        Grid.SetRow(message, 0)
        grid.Children.Add(message)

        let buttons =
            StackPanel(Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10.0, Margin = Avalonia.Thickness(0.0, 8.0, 0.0, 0.0))

        let btnOpenFile = Button(Content = "Open File", Padding = Avalonia.Thickness(16.0, 5.0))
        let btnOpenFolder = Button(Content = "Open Folder", Padding = Avalonia.Thickness(16.0, 5.0))
        let btnClose = Button(Content = "Close", Padding = Avalonia.Thickness(16.0, 5.0))

        buttons.Children.Add(btnOpenFile)
        buttons.Children.Add(btnOpenFolder)
        buttons.Children.Add(btnClose)
        Grid.SetRow(buttons, 1)
        grid.Children.Add(buttons)

        let dontShow =
            CheckBox(Content = "Don't show this again", Margin = Avalonia.Thickness(0.0, 8.0, 0.0, 0.0), HorizontalAlignment = HorizontalAlignment.Center)

        Grid.SetRow(dontShow, 2)
        grid.Children.Add(dontShow)

        this.Content <- grid

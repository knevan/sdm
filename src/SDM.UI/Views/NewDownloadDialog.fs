namespace SDM.UI.Views

open Avalonia.Controls
open Avalonia.Layout

type NewDownloadDialog() as this =
    inherit Window()

    do
        this.Title <- "New Download"
        this.Width <- 520.0
        this.Height <- 300.0
        this.CanResize <- false
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.ShowInTaskbar <- false

        let grid = Grid(Margin = Avalonia.Thickness(16.0))

        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Star))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))

        grid.ColumnDefinitions.Add(ColumnDefinition(GridLength.Auto))
        grid.ColumnDefinitions.Add(ColumnDefinition(GridLength.Star))

        let urlLabel = TextBlock(Text = "URL:", VerticalAlignment = VerticalAlignment.Center, Margin = Avalonia.Thickness(0.0, 0.0, 8.0, 0.0))
        Grid.SetRow(urlLabel, 0)
        Grid.SetColumn(urlLabel, 0)
        grid.Children.Add(urlLabel)

        let urlBox = TextBox(PlaceholderText = "https://...", Margin = Avalonia.Thickness(0.0, 0.0, 0.0, 8.0))
        Grid.SetRow(urlBox, 0)
        Grid.SetColumn(urlBox, 1)
        grid.Children.Add(urlBox)

        let fileLabel = TextBlock(Text = "File:", VerticalAlignment = VerticalAlignment.Center, Margin = Avalonia.Thickness(0.0, 0.0, 8.0, 0.0))
        Grid.SetRow(fileLabel, 1)
        Grid.SetColumn(fileLabel, 0)
        grid.Children.Add(fileLabel)

        let fileNameBox = TextBox(PlaceholderText = "filename", Margin = Avalonia.Thickness(0.0, 0.0, 0.0, 8.0))
        Grid.SetRow(fileNameBox, 1)
        Grid.SetColumn(fileNameBox, 1)
        grid.Children.Add(fileNameBox)

        let folderLabel = TextBlock(Text = "Folder:", VerticalAlignment = VerticalAlignment.Center, Margin = Avalonia.Thickness(0.0, 0.0, 8.0, 0.0))
        Grid.SetRow(folderLabel, 2)
        Grid.SetColumn(folderLabel, 0)
        grid.Children.Add(folderLabel)

        let folderBox = TextBox(PlaceholderText = "C:\\Downloads", Margin = Avalonia.Thickness(0.0, 0.0, 0.0, 8.0))
        Grid.SetRow(folderBox, 2)
        Grid.SetColumn(folderBox, 1)
        grid.Children.Add(folderBox)

        let buttons =
            StackPanel(Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10.0, Margin = Avalonia.Thickness(0.0, 8.0, 0.0, 0.0))

        let btnDLNow = Button(Content = "Download Now", Padding = Avalonia.Thickness(16.0, 5.0))
        let btnDLLater = Button(Content = "Download Later", Padding = Avalonia.Thickness(16.0, 5.0))
        let btnCancel = Button(Content = "Cancel", Padding = Avalonia.Thickness(16.0, 5.0))

        buttons.Children.Add(btnDLNow)
        buttons.Children.Add(btnDLLater)
        buttons.Children.Add(btnCancel)
        Grid.SetRow(buttons, 4)
        Grid.SetColumnSpan(buttons, 2)
        grid.Children.Add(buttons)

        this.Content <- grid

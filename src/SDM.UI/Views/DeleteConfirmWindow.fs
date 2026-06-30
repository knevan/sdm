namespace SDM.UI.Views

open Avalonia.Controls
open Avalonia.Layout

type DeleteConfirmWindow() as this =
    inherit Window()

    do
        this.Title <- "Delete Download"
        this.Width <- 420.0
        this.Height <- 170.0
        this.MinWidth <- 380.0
        this.MinHeight <- 150.0
        this.CanResize <- false
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.ShowInTaskbar <- false

        let grid = Grid(Margin = Avalonia.Thickness(16.0))

        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Star))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))

        let message =
            TextBlock(Text = "Are you sure you want to delete this download?", FontSize = 14.0)

        Grid.SetRow(message, 0)
        grid.Children.Add(message)

        let deleteFromDisk =
            CheckBox(Content = "Also delete file(s) from disk", Margin = Avalonia.Thickness(0.0, 8.0, 0.0, 0.0))

        Grid.SetRow(deleteFromDisk, 2)
        grid.Children.Add(deleteFromDisk)

        let buttons =
            StackPanel(Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10.0, Margin = Avalonia.Thickness(0.0, 14.0, 0.0, 0.0))

        let btnConfirm = Button(Content = "Delete", Padding = Avalonia.Thickness(16.0, 5.0))
        let btnCancel = Button(Content = "Cancel", Padding = Avalonia.Thickness(16.0, 5.0))

        buttons.Children.Add(btnConfirm)
        buttons.Children.Add(btnCancel)
        Grid.SetRow(buttons, 3)
        grid.Children.Add(buttons)

        this.Content <- grid

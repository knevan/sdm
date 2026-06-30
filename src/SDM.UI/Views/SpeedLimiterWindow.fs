namespace SDM.UI.Views

open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout

type SpeedLimiterWindow() as this =
    inherit Window()

    do
        this.Title <- "Speed Limiter"
        this.Width <- 400.0
        this.Height <- 200.0
        this.CanResize <- false
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.ShowInTaskbar <- false

        let grid = Grid(Margin = Avalonia.Thickness(16.0))

        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Star))
        grid.RowDefinitions.Add(RowDefinition(GridLength.Auto))

        let enableCheck =
            CheckBox(Content = "Enable Speed Limit", IsChecked = System.Nullable false, FontSize = 14.0)

        Grid.SetRow(enableCheck, 0)
        grid.Children.Add(enableCheck)

        let slider =
            Slider(Minimum = 0.0, Maximum = 10000.0, Value = 0.0, Margin = Avalonia.Thickness(0.0, 8.0, 0.0, 0.0))

        Grid.SetRow(slider, 1)
        grid.Children.Add(slider)

        let valueText =
            TextBlock(Text = "0 KB/s", HorizontalAlignment = HorizontalAlignment.Center, Margin = Avalonia.Thickness(0.0, 8.0, 0.0, 0.0))

        Grid.SetRow(valueText, 2)
        grid.Children.Add(valueText)

        let buttons =
            StackPanel(Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10.0, Margin = Avalonia.Thickness(0.0, 8.0, 0.0, 0.0))

        let btnApply = Button(Content = "Apply", Padding = Avalonia.Thickness(16.0, 5.0))
        let btnCancel = Button(Content = "Cancel", Padding = Avalonia.Thickness(16.0, 5.0))

        buttons.Children.Add(btnApply)
        buttons.Children.Add(btnCancel)
        Grid.SetRow(buttons, 3)
        grid.Children.Add(buttons)

        this.Content <- grid

using Avalonia.Controls;
using Avalonia.Input;
using SDM.UI.ViewModels;

namespace SDM.UI.Views;

public partial class DownloadCompleteWindow : Window
{
    public DownloadCompleteWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wire up the ViewModel CloseRequested event to close this window.
    /// Must be called after DataContext is assigned.
    /// </summary>
    public void Initialize()
    {
        if (DataContext is DownloadCompleteViewModel vm)
        {
            vm.CloseRequested += () => Close();
        }
    }
    /// <summary>
    /// Handle "Don't show again" link click.
    /// Avalonia TextBlock doesn't have a Click event, so we use PointerPressed.
    /// </summary>
    private void DontShowAgain_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is DownloadCompleteViewModel vm)
        {
            vm.DontShowAgainCommand.Execute(null);
        }
    }
}
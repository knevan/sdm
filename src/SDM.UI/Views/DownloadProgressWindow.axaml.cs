using System.ComponentModel;
using Avalonia.Controls;
using SDM.UI.ViewModels;

namespace SDM.UI.Views;

public partial class DownloadProgressWindow : Window
{
    public DownloadProgressWindow()
    {
        InitializeComponent();

    }

    /// <summary>
    /// Wire up ViewModel hide request to close the window without stopping the download.
    /// </summary>
    public void Initialize()
    {
        if (DataContext is DownloadProgressViewModel vm)
        {
            vm.HideRequested += _ =>
            {
                Closing -= OnWindowClosing;
                Close();
            };
        }
    }

    /// <summary>
    /// When user closes the window via title bar X, delegate to the ViewModel
    /// which will stop the download (same behavior as XDM).
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is DownloadProgressViewModel vm)
        {
            vm.OnWindowClosing();
        }
    }
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Closing += OnWindowClosing;
    }
}
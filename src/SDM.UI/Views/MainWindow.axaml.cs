using Avalonia.Controls;
using SDM.UI.ViewModels;

namespace SDM.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Open the New Download dialog when the toolbar button is clicked
    /// </summary>
    private async void BtnNewDownload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new NewDownloadDialog();
        var vm = (NewDownloadViewModel)dialog.DataContext!;

        vm.DownloadRequested += async url =>
        {
            dialog.Close();

            if (DataContext is MainWindowViewModel mainVm)
            {
                await mainVm.AddDownloadCommand.ExecuteAsync(url);
            }
        };

        vm.CancelRequested += () => dialog.Close();

        await dialog.ShowDialog(this);
    }
}
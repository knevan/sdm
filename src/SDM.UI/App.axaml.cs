using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using SDM.UI.ViewModels;
using SDM.UI.Views;

namespace SDM.UI;

public partial class App : Avalonia.Application
{
    private MainWindowViewModel? _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainViewModel = new MainWindowViewModel();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel,
            };

            // Dispose resources on shutdown
            desktop.ShutdownRequested += (_, _) =>
            {
                _mainViewModel?.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
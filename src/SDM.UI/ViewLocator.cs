using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SDM.UI.ViewModels;
using SDM.UI.Views;

namespace SDM.UI;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            MainWindowViewModel => new MainWindow(),
            NewDownloadViewModel => new NewDownloadDialog(),
            DownloadProgressViewModel => new DownloadProgressWindow(),
            DownloadCompleteViewModel => new DownloadCompleteWindow(),
            DeleteConfirmViewModel => new DeleteConfirmWindow(),
            SpeedLimiterViewModel => new SpeedLimiterWindow(),

            null => null,
            _ => new TextBlock { Text = $"Not Found: {param.GetType().FullName}" }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}

using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDM.UI.Helpers;

namespace SDM.UI.ViewModels;

/// <summary>
/// ViewModel for the download complete dialog.
///  </summary>
public partial class DownloadCompleteViewModel : ViewModelBase
{
    private readonly Action? _onDontShowAgain;

    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _folderPath = "";

    /// <summary>
    /// Display-friendly location text with path abbreviation for long paths.
    /// </summary>
    public string LocationDisplay => FolderPath.Length > 60
        ? $"...{FolderPath[^55..]}"
        : FolderPath;

    /// <summary>
    /// Full file path for open operations.
    /// </summary>
    public string FullPath => Path.Combine(FolderPath, FileName);

    /// <summary>
    /// Event raised when the dialog should be closed.
    /// The View subscribes to this to close its Window.
    /// </summary>
    public event Action? CloseRequested;

    /// <param name="fileName">Name of the completed download file</param>
    /// <param name="folderPath">Folder containing the downloaded file</param>
    /// <param name="onDontShowAgain">
    /// Callback invoked when user clicks "Don't show again".
    /// The caller should persist this preference via ConfigStore.
    /// </param>
    public DownloadCompleteViewModel(
        string fileName,
        string folderPath,
        Action? onDontShowAgain = null)
    {
        _onDontShowAgain = onDontShowAgain;
        _fileName = fileName;
        _folderPath = folderPath;
    }

    /// <summary>
    /// Parameterless constructor for Avalonia designer support only.
    /// </summary>
    public DownloadCompleteViewModel()
    {
        _fileName = "example_file.zip";
        _folderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>
    /// Open the downloaded file with the OS default application.
    /// </summary>
    [RelayCommand]
    private void OpenFile()
    {
        PlatformLauncher.OpenFile(FullPath);
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Open the containing folder and highlight the file.
    /// </summary>
    [RelayCommand]
    private void OpenFolder()
    {
        PlatformLauncher.OpenFolder(FolderPath, FileName);
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Suppress future download complete dialogs and close.
    /// The preference is persisted via the injected callback
    /// </summary>
    [RelayCommand]
    private void DontShowAgain()
    {
        _onDontShowAgain?.Invoke();
        CloseRequested?.Invoke();
    }

    // Notify computed properties
    partial void OnFolderPathChanged(string value) => OnPropertyChanged(nameof(LocationDisplay));
}
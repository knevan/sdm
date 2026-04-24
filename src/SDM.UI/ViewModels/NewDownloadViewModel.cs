using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SDM.UI.ViewModels;

/// <summary>
/// ViewModel for the New Download dialog. Validates URL input
/// and emits the download request to the main view model.
/// </summary>
public partial class NewDownloadViewModel : ViewModelBase
{
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _saveFolder = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isValid;

    public event Action<string>? DownloadRequested;
    public event Action? CancelRequested;

    partial void OnUrlChanged(string value)
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            IsValid = false;
            return;
        }

        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "ftp"))
        {
            IsValid = true;

            if (string.IsNullOrEmpty(FileName))
            {
                var lastSeg = uri.Segments.Length > 0
                    ? Uri.UnescapeDataString(uri.Segments[^1])
                    : "";

                if (!string.IsNullOrWhiteSpace(lastSeg) && lastSeg != "/")
                {
                    FileName = Path.GetFileName(lastSeg);
                }
            }
        }
        else
        {
            IsValid = false;
            ErrorMessage = "Please enter a valid HTTP/HTTPS/FTP URL.";
        }
    }

    [RelayCommand]
    private void StartDownload()
    {
        if (!IsValid || string.IsNullOrWhiteSpace(Url)) return;
        DownloadRequested?.Invoke(Url.Trim());
    }
    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();
}
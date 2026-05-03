using System;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDM.UI.Helpers;

namespace SDM.UI.ViewModels;

/// <summary>
/// Represents the distinct lifecycle states of a download in the progress window.
/// </summary>
public enum DownloadWindowState
{
    Downloading,
    Paused,
    Failed,
    Cancelled
}

/// <summary>
/// ViewModel for the individual download progress window.
/// Manages real-time progress display, pause/resume/stop controls,
/// speed and ETA calculation, and window title updates.
/// </summary>
public partial class DownloadProgressViewModel : ViewModelBase
{
    private readonly Action<Guid>? _pauseAction;
    private readonly Action<Guid>? _resumeAction;
    private readonly Action<Guid, bool>? _stopAction;

    [ObservableProperty] private Guid _downloadId;
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _fileSizeText = "---";
    [ObservableProperty] private string _speedText = "---";
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private DownloadWindowState _state = DownloadWindowState.Downloading;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _speedLimitText = "No Speed Limit";
    [ObservableProperty] private string _windowTitle = "Download Progress";

    /// <summary>
    /// Label for the pause/resume toggle button.
    /// </summary>
    public string PauseResumeLabel => State switch
    {
        DownloadWindowState.Downloading => "Pause",
        _ => "Resume"
    };

    /// <summary>
    /// Whether the pause/resume button should be enabled.
    /// Disabled only during cancellation (terminal state).
    /// </summary>
    public bool CanPauseResume => State != DownloadWindowState.Cancelled;

    /// <summary>
    /// Event raised when the user clicks "Hide" to dismiss the window
    /// without stopping the download.
    /// </summary>
    public event Action<Guid>? HideRequested;

    /// <summary>
    /// Event raised when the window is being closed (user pressed X).
    /// The subscriber should stop the download.
    /// </summary>
    public event Action<Guid>? WindowClosing;

    public DownloadProgressViewModel(
        Action<Guid>? pauseAction = null,
        Action<Guid>? resumeAction = null,
        Action<Guid, bool>? stopAction = null
    )
    {
        _pauseAction = pauseAction;
        _resumeAction = resumeAction;
        _stopAction = stopAction;
    }

    /// <summary>
    /// Parameterless constructor for Avalonia designer support only.
    /// Initializes mock data for the preview window.
    /// </summary>
    public DownloadProgressViewModel()
    {
        _fileName = "ubuntu-22.04.3-desktop-amd64.iso";
        _url = "https://releases.ubuntu.com/22.04/ubuntu-22.04.3-desktop-amd64.iso";
        _fileSizeText = "2.2 GB / 4.7 GB";
        _speedText = "24.5 MB/s";
        _etaText = "ETA: 2 min 15 sec";
        _progressValue = 25;
        _windowTitle = "Downloading - ubuntu-22.04.3-desktop-amd64.iso";
    }

    /// <summary>
    /// Update progress display with current download metrics.
    /// Called from the UI thread by the MainWindowViewModel event dispatcher.
    /// Computes ETA locally from speed and remaining bytes for accuracy.
    /// </summary>
    public void UpdateProgress(double progressPercent, long speedBps, long downloadedBytes, long? totalBytes)
    {
        var clampedProgress = (int)Math.Clamp(progressPercent, 0, 100);
        ProgressValue = clampedProgress;
        IsIndeterminate = progressPercent < 0 || totalBytes is null or 0;

        SpeedText = speedBps > 0 ? FormatHelper.FormatSpeed(speedBps) : "---";

        if (totalBytes is > 0)
        {
            FileSizeText = $"{FormatHelper.FormatSize(downloadedBytes)} / {FormatHelper.FormatSize(totalBytes.Value)}";
        }
        else
        {
            FileSizeText = downloadedBytes > 0 ? FormatHelper.FormatSize(downloadedBytes) : "---";
        }

        if (speedBps > 0 && totalBytes is > 0)
        {
            var remainingBytes = totalBytes.Value - downloadedBytes;
            if (remainingBytes > 0)
            {
                var etaSeconds = (double)remainingBytes / speedBps;
                EtaText = FormatHelper.FormatEta(TimeSpan.FromSeconds(etaSeconds));
            }
            else
            {
                EtaText = "";
            }
        }
        else
        {
            EtaText = speedBps > 0 ? "Calculating..." : "";
        }

        var progressPrefix = clampedProgress is > 0 and <= 100 ? $"{clampedProgress}% " : "";
        WindowTitle = $"{progressPrefix}{FileName}";

        if (State != DownloadWindowState.Downloading)
        {
            State = DownloadWindowState.Downloading;
        }
    }

    /// <summary>
    /// Transition to "started" state. Called when the engine confirms download begin.
    /// </summary>
    public void OnDownloadStarted()
    {
        State = DownloadWindowState.Downloading;
    }

    /// <summary>
    /// Transition to "failed" state with error details.
    /// Enables the Resume button so the user can retry.
    /// </summary>
    public void OnDownloadFailed(string errorMessage)
    {
        State = DownloadWindowState.Failed;
        ErrorMessage = errorMessage;
        FileSizeText = errorMessage;
        EtaText = "";
        SpeedText = "---";
    }

    /// <summary>
    /// Transition to "canceled" state. Occurs when user stops the download.
    /// </summary>
    public void OnDownloadCancelled()
    {
        State = DownloadWindowState.Cancelled;
        FileSizeText = "Download stopped";
        EtaText = "";
        SpeedText = "---";
    }

    /// <summary>
    /// Toggle pause/resume based on current state.
    /// </summary>
    [RelayCommand]
    private void TogglePauseResume()
    {
        switch (State)
        {
            case DownloadWindowState.Downloading:
                _pauseAction?.Invoke(DownloadId);
                State = DownloadWindowState.Paused;
                SpeedText = "---";
                EtaText = "";
                break;
            case DownloadWindowState.Paused:
            case DownloadWindowState.Failed:
            case DownloadWindowState.Cancelled:
                _resumeAction?.Invoke(DownloadId);
                State = DownloadWindowState.Downloading;
                break;
        }
    }

    /// <summary>
    /// Stop the download and close the progress window.
    /// Sends close=true to remove from active coordinator list.
    /// </summary>
    [RelayCommand]
    private void StopDownload()
    {
        _stopAction?.Invoke(DownloadId, true);
    }

    /// <summary>
    /// Hide the progress window without stopping the download.
    /// The download continues in background and remains in the main list.
    /// </summary>
    [RelayCommand]
    private void HideWindow()
    {
        HideRequested?.Invoke(DownloadId);
    }

    /// <summary>
    /// Called when the window is being closed via the title bar X button.
    /// Stops the download as XDM does in Window_Closing.
    /// </summary>
    public void OnWindowClosing()
    {
        WindowClosing?.Invoke(DownloadId);
        _stopAction?.Invoke(DownloadId, true);
    }

    // Notify computed properties when state changes
    partial void OnStateChanged(DownloadWindowState value)
    {
        OnPropertyChanged(nameof(PauseResumeLabel));
        OnPropertyChanged(nameof(CanPauseResume));
    }

    partial void OnFileNameChanged(string value)
    {
        // Sync window title when file name is set initially
        var progressPrefix = ProgressValue is > 0 and <= 100 ? $"{ProgressValue}% " : "";
        WindowTitle = $"{progressPrefix}{value}";
    }
}
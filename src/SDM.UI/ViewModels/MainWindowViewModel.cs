using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.FSharp.Core;
using SDM.Application;
using SDM.Domain;
using SDM.Infrastructure;
using SDM.UI.Helpers;
using SDM.UI.Models;
using SDM.UI.Views;

namespace SDM.UI.ViewModels;

/// <summary>
/// Main window view model.
/// Orchestrates download management with reactive UI updates.
/// Bridges F# backend services with Avalonia MVVM data binding.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly AppConfigModule.ConfigStore _configStore;
    private readonly DownloadManager _downloadManager;
    private readonly QueueScheduler _queueScheduler;
    // private readonly string _connectionString;

    /// <summary>
    /// Track active progress windows by download ID.
    /// Uses ConcurrentDictionary for thread-safe access from event handlers.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, (DownloadProgressViewModel Vm, DownloadProgressWindow Window)>
        _progressWindows = new();

    /// <summary>
    /// Reference to the main window for dialog ownership.
    /// Set from App.axaml.cs after window creation.
    /// </summary>
    public Window? OwnerWindow { get; set; }

    /// <summary>
    /// Whether to show the download complete dialog after a download finishes.
    /// Defaults to true; user can disable via "Don't show again" in the dialog.
    /// </summary>
    private bool _showCompleteDialog = true;

    [ObservableProperty]
    private ObservableCollection<DownloadItemModel> _downloads = [];
    [ObservableProperty]
    private DownloadItemModel? _selectedDownload;
    [ObservableProperty]
    private string _searchText = "";
    [ObservableProperty]
    private string _statusText = "Ready";
    [ObservableProperty]
    private int _activeCount;
    [ObservableProperty]
    private bool _isInProgressView = true;
    [ObservableProperty]
    private string _windowTitle = "SDM - S Download Manager";

    public MainWindowViewModel()
    {
        _configStore = new AppConfigModule.ConfigStore();

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SDM", "downloads.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var connectionString = $"Data Source={dbPath}";

        DownloadStore.initializeDb(connectionString);

        _downloadManager = new DownloadManager(
            _configStore,
            connectionString,
            OnDownloadEvent
        );

        _queueScheduler = new QueueScheduler(_downloadManager, _configStore);
        _queueScheduler.Start();

        LoadDownloads();
        
        LoadMockData();
    }

    /// <summary>
    /// Load all downloads from the database into the observable collection
    /// </summary>
    private void LoadDownloads()
    {
        var entries = _downloadManager.GetAll();

        Downloads.Clear();

        foreach (var entry in entries)
        {
            Downloads.Add(MapToModel(entry));
        }

        UpdateStatusText();
    }

    /// <summary>
    /// Injects high-quality mock data into the Downloads collection.
    /// This is used for UI demonstration and testing various lifecycle states 
    /// without requiring active network operations.
    /// </summary>
    public void LoadMockData()
    {
        var mockItems = new List<DownloadItemModel>
        {
            new()
            {
                Id = Guid.NewGuid(),
                FileName = "Fucking_Malware_Downloads.exe",
                Url = "https://abstract.com/download/Fucking_Malware_Downloads.exe",
                TotalSize = 1073741824, // 1GB
                Progress = 53.7,
                Speed = 135829122,
                StatusText = "Downloading - 110.0 MB/s",
                AddedAt = DateTime.Now.AddMinutes(-15),
                IsActive = true,
                IsSelected = false
            },
            new ()
            {
                Id = Guid.NewGuid(),
                FileName = "Fucking_Malware_Linux.iso",
                Url = "https://strange-link.com/Fucking_Malware_Linux.iso",
                TotalSize = 5153960755, // 4.8 GB
                Progress = 100,
                Speed = 0,
                StatusText = "Paused",
                AddedAt = DateTime.Now.AddHours(-2),
                IsActive = false
            },
            new()
            {
                Id = Guid.NewGuid(),
                FileName = "Not_A_Fucking_Malware.exe",
                Url = "https://not-malware.com/download/Not_A_Fucking_Malware.exe",
                TotalSize = 209715200, // 200 MB
                Progress = 22.0,
                Speed = 0,
                StatusText = "Cancelled",
                AddedAt = DateTime.Now.AddDays(-1),
                IsActive = false
            }
        };

        foreach (var item in mockItems)
        {
            Downloads.Add(item);
        }
        
        UpdateStatusText();
    }

    /// <summary>
    /// Map a domain DownloadEntry to a UI model
    /// </summary>
    private static DownloadItemModel MapToModel(DownloadEntry entry)
    {
        var (statusText, progress) = MapStatus(entry.Status);

        return new DownloadItemModel
        {
            Id = entry.Id,
            FileName = entry.FileName,
            Url = entry.Url.ToString(),
            TotalSize = entry.TotalSize is { Value: var size } ? size : 0L,
            Progress = progress,
            StatusText = statusText,
            AddedAt = entry.AddedAt,
            IsActive = statusText.StartsWith("Downloading") || statusText == "Starting"
        };
    }

    /// <summary>
    /// Map domain DownloadStatus to display text and progress value
    /// </summary>
    private static (string Text, double Progress) MapStatus(DownloadStatus status)
    {
        if (status.IsQueue) return ("Queued", 0);
        if (status.IsStarting) return ("Starting", 0);
        if (status.IsPausing) return ("Pausing", -1);
        if (status.IsPaused) return ("Paused", -1);
        if (status.IsAssembling) return ("Assembling...", 99);
        if (status.IsDownloading)
        {
            var dl = (DownloadStatus.Downloading)status;
            var speed = dl.speed;
            return ($"Downloading - {FormatHelper.FormatSpeed(speed)}", -1);
        }
        if (status.IsCompleted) return ("Completed", 100);
        if (status.IsError)
        {
            var err = (DownloadStatus.Error)status;
            return ($"Error: {err.message}", -1);
        }
        return ("Unknown", 0);
    }

    /// <summary>
    /// Handle engine events on the UI thread.
    /// Marshals to the UI thread since events fire from 
    /// F# MailboxProcessor background threads.
    /// </summary>
    private void OnDownloadEvent(DownloadEvent evt)
    {
        // Marshal all UI mutations to the Avalonia dispatcher thread.
        // Use Post (fire-and-forget) to avoid deadlocks with the F# agent.
        Dispatcher.UIThread.Post(() => ProcessDownloadEvent(evt));
    }

    /// <summary>
    /// Process a download event on the UI thread.
    /// Called exclusively from the Dispatcher to guarantee thread safety.
    /// </summary>
    private void ProcessDownloadEvent(DownloadEvent evt)
    {
        if (evt.IsDownloadStarted)
        {
            var started = (DownloadEvent.DownloadStarted)evt;
            var item = Downloads.FirstOrDefault(d => d.Id == started.id);
            if (item != null)
            {
                item.StatusText = "Downloading";
                item.IsActive = true;
            }

            if (_progressWindows.TryGetValue(started.id, out var pw))
            {
                pw.Vm.OnDownloadStarted();
            }
        }
        else if (evt.IsProgressUpdated)
        {
            var progEvent = (DownloadEvent.ProgressUpdated)evt;
            var prog = progEvent.info;
            var item = Downloads.FirstOrDefault(d => d.Id == prog.Id);

            if (item != null)
            {
                item.Progress = prog.Progress;
                item.Speed = prog.Speed;
                item.StatusText = $"Downloading - {FormatHelper.FormatSpeed(prog.Speed)}";
                item.IsActive = true;
            }
            if (_progressWindows.TryGetValue(prog.Id, out var pw))
            {
                long? totalBytes = null;
                if (prog.TotalBytes is { Value: var tb })
                {
                    totalBytes = tb;
                }
                pw.Vm.UpdateProgress(
                    prog.Progress,
                    prog.Speed,
                    prog.DownloadedBytes,
                    totalBytes);
            }
        }
        else if (evt.IsDownloadFinished)
        {
            var fin = (DownloadEvent.DownloadFinished)evt;
            var item = Downloads.FirstOrDefault(d => d.Id == fin.id);
            if (item != null)
            {
                item.Progress = 100;
                item.Speed = 0;
                item.StatusText = "Completed";
                item.IsActive = false;
            }

            CloseProgressWindow(fin.id);

            // Show download complete dialog
            if (_showCompleteDialog)
            {
                ShowDownloadCompleteDialog(fin.finalPath);
            }
        }
        else if (evt.IsDownloadFailed)
        {
            var fail = (DownloadEvent.DownloadFailed)evt;
            var item = Downloads.FirstOrDefault(d => d.Id == fail.id);
            if (item != null)
            {
                item.Speed = 0;
                item.StatusText = $"Error: {fail.error}";
                item.IsActive = false;
            }

            // Notify progress window of failure
            if (_progressWindows.TryGetValue(fail.id, out var pw))
            {
                pw.Vm.OnDownloadFailed(fail.error);
            }
        }
        UpdateStatusText();
    }

    /// <summary>
    /// Show a progress window for a specific download.
    /// Creates the ViewModel with action delegates back into the DownloadManager.
    /// </summary>
    public void ShowProgressWindow(Guid downloadId, string fileName, string url)
    {
        // If already open, just activate it
        if (_progressWindows.TryGetValue(downloadId, out var existing))
        {
            existing.Window.Activate();
            return;
        }
        var vm = new DownloadProgressViewModel(
            pauseAction: id => _downloadManager.Pause(id),
            resumeAction: id => _downloadManager.Start(id),
            stopAction: (id, close) =>
            {
                if (close)
                    _downloadManager.Cancel(id);
                else
                    _downloadManager.Pause(id);
            })
        {
            DownloadId = downloadId,
            FileName = fileName,
            Url = url
        };
        var window = new DownloadProgressWindow
        {
            DataContext = vm
        };
        window.Initialize();

        // Clean up tracking when the window closes
        window.Closed += (_, _) =>
        {
            _progressWindows.TryRemove(downloadId, out _);
        };

        // Track the window
        _progressWindows.TryAdd(downloadId, (vm, window));

        window.Show();
    }

    /// <summary>
    /// Close the progress window for a specific download if it's open.
    /// </summary>
    private void CloseProgressWindow(Guid downloadId)
    {
        if (_progressWindows.TryRemove(downloadId, out var pw))
        {
            try
            {
                // Detach closing handler to prevent re-stopping the download
                pw.Window.Close();
            }
            catch
            {
                // Window may already be closed; ignore
            }
        }
    }

    /// <summary>
    /// Show the download complete dialog with file/folder open actions.
    /// </summary>
    private void ShowDownloadCompleteDialog(string finalPath)
    {
        var fileName = Path.GetFileName(finalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = "download";

        var folder = Path.GetDirectoryName(finalPath) ?? string.Empty;

        var vm = new DownloadCompleteViewModel(
            fileName,
            folder,
            onDontShowAgain: () => _showCompleteDialog = false);

        var dialog = new DownloadCompleteWindow
        {
            DataContext = vm
        };

        dialog.Initialize();
        dialog.Show();
    }

    private void UpdateStatusText()
    {
        ActiveCount = Downloads.Count(d => d.IsActive);
        
        StatusText = ActiveCount > 0
            ? $"{ActiveCount} active download(s)"
            : "Ready";
    }

    /// <summary>
    /// Show the delete confirmation dialog as a modal.
    /// Returns the user's choice including whether to delete files from disk.
    /// </summary>
    private async Task<DeleteConfirmResult> ShowDeleteConfirmDialogAsync(int itemCount)
    {
        var message = itemCount == 1
        ? "Are you sure you want to delete the selected download?"
        : $"Are you sure you want to delete {itemCount} selected download(s)?";

        var vm = new DeleteConfirmViewModel(message);
        var dialog = new DeleteConfirmWindow { DataContext = vm };

        dialog.Initialize();
        await dialog.ShowDialog(OwnerWindow!);

        return vm.Result;
    }

    /// <summary>
    /// Show the speed limiter configuration window.
    /// Reads current config, displays the dialog, and persists changes on apply.
    /// </summary>
    public void ShowSpeedLimiterWindow()
    {
        var config = _configStore.Current;
        var isEnabled = config.SpeedLimitKBps > 0;

        var vm = new SpeedLimiterViewModel(isEnabled, config.SpeedLimitKBps);
        var window = new SpeedLimiterWindow { DataContext = vm };
        window.Initialize();

        window.Closed += (_, _) =>
        {
            if (vm.Result.Applied)
            {
                _configStore.Update(FuncConvert.FromFunc<AppConfig, AppConfig>(cfg => new AppConfig
                {
                    MaxConcurrentDownloads = cfg.MaxConcurrentDownloads,
                    MaxSegmentsPerDownload = cfg.MaxSegmentsPerDownload,
                    DefaultDownloadFolder = cfg.DefaultDownloadFolder,
                    TempFolder = cfg.TempFolder,
                    SpeedLimitKBps = vm.Result.IsEnabled ? vm.Result.SpeedLimitKBps : 0,
                    MinDiskSpaceMB = cfg.MinDiskSpaceMB,
                    MonitorClipboard = cfg.MonitorClipboard,
                    FileExtensions = cfg.FileExtensions,
                    VideoExtensions = cfg.VideoExtensions,
                    BlockedHosts = cfg.BlockedHosts,
                    Proxy = cfg.Proxy,
                    AutoStartQueue = cfg.AutoStartQueue,
                    FileConflictMode = cfg.FileConflictMode,
                    NetworkTimeoutSeconds = cfg.NetworkTimeoutSeconds,
                    MaxRetries = cfg.MaxRetries,
                    RetryDelaySeconds = cfg.RetryDelaySeconds
                }));

                var limitText = vm.Result is { IsEnabled: true, SpeedLimitKBps: > 0 }
                    ? $"Speed Limit - {vm.Result.SpeedLimitKBps} KB/s"
                    : "No Speed Limit";

                foreach (var pw in _progressWindows.Values)
                {
                    pw.Vm.SpeedLimitText = limitText;
                }
            }
        };
    }

    // --- Commands ---

    /// <summary>
    /// Add a new download. Uses the Task-returning AddAsync() directly
    /// no FSharpAsync.StartAsTask wrapper needed.
    /// </summary>
    [RelayCommand]
    private async Task AddDownloadAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            StatusText = "Invalid URL";
            return;
        }

        StatusText = "Adding download...";

        var request = new AddDownloadRequest { Url = uri, StartImmediately = true };

        var result = await _downloadManager.AddAsync(request);

        if (result.IsAdded)
        {
            var added = (AddDownloadResult.Added)result;

            var entryOpts = _downloadManager.TryGet(added.id);

            if (entryOpts is { Value: var entry })
            {
                Downloads.Insert(0, MapToModel(entry));
                // Auto-open the progress window for the new download
                ShowProgressWindow(entry.Id, entry.FileName, entry.Url.ToString());
            }

            StatusText = "Download added";
        }
        else if (result.IsInvalidUrl)
        {
            var invalid = (AddDownloadResult.InvalidUrl)result;
            StatusText = $"Failed: {invalid.message}";
        }

        UpdateStatusText();
    }

    [RelayCommand]
    private void PauseDownload()
    {
        if (SelectedDownload is { } item)
        {
            _downloadManager.Pause(item.Id);
            item.StatusText = "Paused";
            item.Speed = 0;
            item.IsActive = false;
            UpdateStatusText();
        }
    }

    [RelayCommand]
    private void ResumeDownload()
    {
        if (SelectedDownload is { } item)
        {
            _downloadManager.Start(item.Id);
            item.StatusText = "Resuming...";
            item.IsActive = true;
            UpdateStatusText();
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        if (SelectedDownload is { } item)
        {
            _downloadManager.Cancel(item.Id);
            item.StatusText = "Cancelled";
            item.Speed = 0;
            item.IsActive = false;
            UpdateStatusText();
        }
    }

    [RelayCommand]
    private void RemoveDownload()
    {
        if (SelectedDownload is { } item)
        {
            _downloadManager.Remove(item.Id, deleteFiles: false);
            Downloads.Remove(item);
            SelectedDownload = null;
            UpdateStatusText();
        }
    }

    [RelayCommand]
    private async Task RemoveDownloadWithConfirmAsync()
    {
        if (SelectedDownload is not { } item) return;

        var result = await ShowDeleteConfirmDialogAsync(1);

        if (result.Confirmed)
        {
            _downloadManager.Remove(item.Id, deleteFiles: result.DeleteFromDisk);
            Downloads.Remove(item);
            SelectedDownload = null;
            UpdateStatusText();
        }
    }

    [RelayCommand]
    private void RemoveSelectedDownloads()
    {
        var toRemove = Downloads.Where(d => d.IsSelected).ToList();

        foreach (var item in toRemove)
        {
            _downloadManager.Remove(item.Id, deleteFiles: false);
            Downloads.Remove(item);
        }
        UpdateStatusText();
    }

    [RelayCommand]
    private void RefreshList() => LoadDownloads();

    public bool CanPause => SelectedDownload?.IsActive == true;
    public bool CanResume => SelectedDownload is { IsActive: false, StatusText: "Paused" or "Queued" or "Error" };
    public bool CanCancel => SelectedDownload?.IsActive == true;
    public bool CanRemove => SelectedDownload != null;

    partial void OnSelectedDownloadChanged(DownloadItemModel? value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRemove));
    }

    public void Dispose()
    {
        foreach (var kvp in _progressWindows)
        {
            try { kvp.Value.Window.Close(); } catch (Exception) { /* Ignored */ }
        }
        _progressWindows.Clear();

        _queueScheduler.Stop();
        ((IDisposable)_queueScheduler).Dispose();
        ((IDisposable)_downloadManager).Dispose();
        GC.SuppressFinalize(this);
    }
}

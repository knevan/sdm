using System;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDM.Application;
using SDM.Domain;
using SDM.Infrastructure;
using SDM.UI.Helpers;
using SDM.UI.Models;

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
    private readonly string _connectionString;

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
        _connectionString = $"Data Source={dbPath}";

        DownloadStore.initializeDb(_connectionString);

        _downloadManager = new DownloadManager(
            _configStore,
            _connectionString,
            Microsoft.FSharp.Core.FuncConvert.FromAction<DownloadEvent>(OnDownloadEvent)
        );

        _queueScheduler = new QueueScheduler(_downloadManager, _configStore);
        _queueScheduler.Start();

        LoadDownloads();
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
            IsActive = statusText is "Downloading" or "Starting"
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
            var speed = (long)dl.speed;
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
    /// Handle engine events on the UI thread
    /// </summary>
    private void OnDownloadEvent(DownloadEvent evt)
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
        }
        else if (evt.IsProgressUpdated)
        {
            var prog = (DownloadEvent.ProgressUpdated)evt;
            var item = Downloads.FirstOrDefault(d => d.Id == prog.id);
            if (item != null)
            {
                item.Progress = prog.progress;
                item.Speed = (long)prog.speed;
                item.StatusText = $"Downloading - {FormatHelper.FormatSpeed((long)prog.speed)}";
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
        }

        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        ActiveCount = _downloadManager.ActiveCount;
        StatusText = ActiveCount > 0
            ? $"{ActiveCount} active download(s)"
            : "Ready";
    }

    // --- Commands ---

    /// <summary>
    /// Show the new download dialog result. Called by NewDownloadDialog.
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

        var request = new AddDownloadRequest(
            uri,
            Microsoft.FSharp.Core.FSharpOption<string>.None,
            Microsoft.FSharp.Core.FSharpOption<string>.None,
            Microsoft.FSharp.Collections.MapModule.Empty<string, string>(),
            Microsoft.FSharp.Core.FSharpOption<string>.None,
            AuthInfo.NoAuth,
            Microsoft.FSharp.Core.FSharpOption<Tuple<HashAlgorithm, string>>.None,
            startImmediately: true);

        var result = await Microsoft.FSharp.Control.FSharpAsync.StartAsTask(
            _downloadManager.Add(request), null, null);

        if (result.IsAdded)
        {
            var added = (AddDownloadResult.Added)result;
            var entry = _downloadManager.TryGet(added.id);

            if (entry is { } e)
            {
                Downloads.Insert(0, MapToModel(e.Value));
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
    private void RefreshList() => LoadDownloads();

    public bool CanPause => SelectedDownload?.IsActive == true;
    public bool CanResume => SelectedDownload is { IsActive: false, StatusText: "Paused" or "Queued" or "Error" };
    public bool CanCancel => SelectedDownload?.IsActive == true;
    public bool CanRemove => SelectedDownload != null;

    partial void OnSelectedDownloadChanged(DownloadItemModel? value)
    {
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRemove));
    }

    public void Dispose()
    {
        _queueScheduler.Stop();
        ((IDisposable)_queueScheduler).Dispose();
        ((IDisposable)_downloadManager).Dispose();
        GC.SuppressFinalize(this);
    }
}

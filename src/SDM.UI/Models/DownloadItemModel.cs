using CommunityToolkit.Mvvm.ComponentModel;
using SDM.UI.Helpers;

namespace SDM.UI.Models;

/// <summary>
/// Observable model wrapping a download entry for DataGrid binding.
/// Maps domain types to UI display properties.
/// </summary>
public partial class DownloadItemModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
    [NotifyPropertyChangedFor(nameof(ShowStatusText))]
    private Guid _id;
    
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private long _totalSize;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(ProgressInt))]
    private double _progress;
    
    [ObservableProperty] private long _speed;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
    [NotifyPropertyChangedFor(nameof(ShowStatusText))]
    private string _statusText = "Queued";
    
    [ObservableProperty] private DateTime _addedAt;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
    [NotifyPropertyChangedFor(nameof(ShowStatusText))]
    private bool _isActive;
    
    [ObservableProperty] private bool _isSelected;
    
    public string SizeText => TotalSize > 0 ? FormatHelper.FormatSize(TotalSize) : "Unknown";
    public string SpeedText => Speed > 0 ? FormatHelper.FormatSpeed(Speed) : "";
    public string DateText => AddedAt.ToString("M");
    public string FileCategory => FormatHelper.GetFileCategory(FileName);
    public int ProgressInt => (int)Math.Clamp(Progress, 0, 100);
    
    /// <summary>
    /// Returns true if the download is explicitly paused.
    /// </summary>
    public bool IsPaused => StatusText.StartsWith("Paused", StringComparison.OrdinalIgnoreCase);
    
    /// <summary>
    /// Returns true if the download is currently active or starting.
    /// </summary>
    public bool IsDownloading => IsActive || StatusText.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase) || StatusText == "Starting";
    
    /// <summary>
    /// Returns true if the download has finished successfully.
    /// </summary>
    public bool IsCompleted => StatusText == "Completed";
    
    /// <summary>
    /// Determines if the progress bar should be visible instead of plain status text.
    /// </summary>
    public bool ShowProgressBar => IsDownloading || IsPaused || IsCompleted;

    /// <summary>
    /// Explicit property for text visibility to avoid using '!' in XAML binding.
    /// </summary>
    public bool ShowStatusText => !ShowProgressBar;
    
    /// <summary>
    /// Notify computed property changes when source properties change
    /// </summary>
    partial void OnTotalSizeChanged(long value) => OnPropertyChanged(nameof(SizeText));
    partial void OnSpeedChanged(long value) => OnPropertyChanged(nameof(SpeedText));
    partial void OnFileNameChanged(string value) => OnPropertyChanged(nameof(FileCategory));
    partial void OnAddedAtChanged(DateTime value) => OnPropertyChanged(nameof(DateText));
}

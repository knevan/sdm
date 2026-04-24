using CommunityToolkit.Mvvm.ComponentModel;
using System;
using SDM.UI.Helpers;

namespace SDM.UI.Models;

/// <summary>
/// Observable model wrapping a download entry for DataGrid binding.
/// Maps domain types to UI display properties.
/// </summary>
public partial class DownloadItemModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private long _totalSize;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private long _speed;
    [ObservableProperty] private string _statusText = "Queued";
    [ObservableProperty] private DateTime _addedAt;
    [ObservableProperty] private bool _isActive;

    public string SizeText => TotalSize > 0 ? FormatHelper.FormatSize(TotalSize) : "Unknown";
    public string SpeedText => Speed > 0 ? FormatHelper.FormatSpeed(Speed) : "";
    public string DateText => AddedAt.ToString("yyyy-MM-dd HH:mm");
    public string FileCategory => FormatHelper.GetFileCategory(FileName);
    public int ProgressInt => (int)Math.Clamp(Progress, 0, 100);

    /// <summary>
    /// Notify computed property changes when source properties change
    /// </summary>
    partial void OnTotalSizeChanged(long value) => OnPropertyChanged(nameof(SizeText));
    partial void OnSpeedChanged(long value) => OnPropertyChanged(nameof(SpeedText));
    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(ProgressInt));
    partial void OnFileNameChanged(string value) => OnPropertyChanged(nameof(FileCategory));
    partial void OnAddedAtChanged(DateTime value) => OnPropertyChanged(nameof(DateText));
}

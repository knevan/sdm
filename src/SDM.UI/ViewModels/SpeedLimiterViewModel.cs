using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SDM.UI.ViewModels;

/// <summary>
/// Result returned by the speed limiter dialog.
/// </summary>
public readonly record struct SpeedLimitResult(bool Applied, bool IsEnabled, int SpeedLimitKBps);

/// <summary>
/// ViewModel for the speed limiter configuration window.
/// Allows the user to enable/disable bandwidth throttling
/// and set the speed limit in KB/s.
///
/// Architecture improvement over XDM:
/// - XDM used a UserControl (SpeedLimiter.xaml) inside a Window (SpeedLimiterWindow.xaml)
///   with input validation scattered across WPF event handlers (PreviewTextInput, Pasting, LostFocus).
/// - SDM consolidates everything into a single ViewModel with proper validation,
///   removing the unnecessary UserControl indirection and code-behind validation.
/// </summary>
public partial class SpeedLimiterViewModel : ViewModelBase
{
    /// <summary>
    /// Whether speed limiting is enabled.
    /// When disabled, the speed limit input becomes read-only.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpeedInputActive))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isEnabled;

    /// <summary>
    /// Speed limit input as string for TextBox binding.
    /// Validated to ensure only valid non-negative integers are accepted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _speedLimitText = "0";

    /// <summary>
    /// Validation error message, empty when input is valid.
    /// </summary>
    [ObservableProperty] 
    private string _validationError = string.Empty;

    /// <summary>
    /// The dialog result, read by the caller after the dialog closes.
    /// </summary>
    public SpeedLimitResult Result { get; private set; }

    /// <summary>
    /// Event raised to signal the View to close.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Logic for enabling/disabling the input field.
    /// </summary>
    public bool IsSpeedInputActive => IsEnabled;

    /// <summary>
    /// Computed display text summarizing the current speed limit setting.
    /// </summary>
    public string SummaryText => IsEnabled && ParsedSpeedLimit > 0
        ? $"Speed Limit - {ParsedSpeedLimit} KB/s"
        : "No Speed Limit";

    /// <summary>
    /// Whether the OK button should be enabled.
    /// Disabled when enabled is checked but input is invalid.
    /// </summary>
    public bool CanApply => !IsEnabled || (IsEnabled && ParsedSpeedLimit > 0 &&
                                           string.IsNullOrEmpty(ValidationError));

    /// <summary>
    /// Parsed integer value from the speed limit text.
    /// Returns 0 if parsing fails.
    /// </summary>
    public int ParsedSpeedLimit => int.TryParse(SpeedLimitText, out var val) && val >= 0 ? val : 0;

    /// <summary>
    /// Parameterless constructor for Avalonia designer support.
    /// </summary>
    public SpeedLimiterViewModel()
    {
    }

    /// <summary>
    /// Production constructor with current configuration values.
    /// </summary>
    /// <param name="isEnabled">Current speed limit enabled state</param>
    /// <param name="speedLimitKBps">Current speed limit value in KB/s</param>
    public SpeedLimiterViewModel(bool isEnabled, int speedLimitKBps)
    {
        _isEnabled = isEnabled;
        _speedLimitText = speedLimitKBps.ToString();
        ValidateSpeedInput();
    }

    /// <summary>
    /// Apply the speed limit settings and close the dialog.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        var limit = IsEnabled ? ParsedSpeedLimit : 0;
        Result = new SpeedLimitResult(Applied: true, IsEnabled: IsEnabled, SpeedLimitKBps: limit);
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Discard changes and close.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = new SpeedLimitResult(Applied: false, IsEnabled: false, SpeedLimitKBps: 0);
        CloseRequested?.Invoke();
    }

    // Revalidate when the enabled state changes
    partial void OnIsEnabledChanged(bool value)
    {
        ValidateSpeedInput();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    partial void OnSpeedLimitTextChanged(string value)
    {
        ValidateSpeedInput();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Validate the speed limit input text.
    /// Only validates when speed limiting is enabled.
    /// </summary>
    private void ValidateSpeedInput()
    {
        if (!IsEnabled)
        {
            ValidationError = "";
            return;
        }

        if (string.IsNullOrWhiteSpace(SpeedLimitText))
        {
            ValidationError = "Speed limit is required";
            return;
        }

        if (!int.TryParse(SpeedLimitText, out var val))
        {
            ValidationError = "Must be a valid number";
            return;
        }

        if (val < 0)
        {
            ValidationError = "Must be greater than 0";
            return;
        }

        ValidationError = "";
    }
}
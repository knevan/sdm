using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SDM.UI.ViewModels;

/// <summary>
/// Result returned by the delete confirmation dialog.
/// </summary>
public readonly record struct DeleteConfirmResult(bool Confirmed, bool DeleteFromDisk);

/// <summary>
/// ViewModel for the delete confirmation modal dialog.
/// Presents the user with a confirmation prompt and an option
/// to also delete the file(s) from disk.
/// </summary>
public partial class DeleteConfirmViewModel : ViewModelBase
{
    /// <summary>
    /// Descriptive message shown in the dialog body.
    /// Can be customized for single/batch delete scenarios.
    /// </summary>
    [ObservableProperty] private string _message = "Are you sure you want to delete the selected download(s)?";

    /// <summary>
    /// Whether to also delete the downloaded file from disk.
    /// Bound to the checkbox via two-way binding.
    /// </summary>
    [ObservableProperty] private bool _deleteFromDisk;

    /// <summary>
    /// The result of the dialog interaction.
    /// Read by the caller after the dialog closes.
    /// </summary>
    public DeleteConfirmResult Result { get; private set; }

    /// <summary>
    /// Event raised to signal the View to close.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Parameterless constructor for Avalonia designer support.
    /// </summary>
    public DeleteConfirmViewModel()
    {
    }

    /// <summary>
    /// Production constructor with custom description text.
    /// </summary>
    /// <param name="message">Contextual message (e.g., count of items to delete)</param>
    public DeleteConfirmViewModel(string message)
    {
        _message = message;
    }

    /// <summary>
    /// Confirm deletion. Sets result and closes the dialog.
    /// </summary>
    [RelayCommand]
    private void ConfirmDelete()
    {
        Result = new DeleteConfirmResult(Confirmed: true, DeleteFromDisk: DeleteFromDisk);
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Cancel without deleting. Sets negative result and closes.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = new DeleteConfirmResult(Confirmed: false, DeleteFromDisk: false);
        CloseRequested?.Invoke();
    }
}


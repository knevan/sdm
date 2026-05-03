using Avalonia.Controls;
using SDM.UI.ViewModels;

namespace SDM.UI.Views;

public partial class DeleteConfirmWindow : Window
{
    public DeleteConfirmWindow()
    {
        InitializeComponent();
    }

    public void Initialize()
    {
        if (DataContext is DeleteConfirmViewModel vm)
        {
            vm.CloseRequested += () => Close();
        }
    }
}


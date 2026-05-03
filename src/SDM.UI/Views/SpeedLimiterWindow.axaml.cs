using Avalonia.Controls;
using SDM.UI.ViewModels;

namespace SDM.UI.Views;

public partial class SpeedLimiterWindow : Window
{
    public SpeedLimiterWindow()
    {
        InitializeComponent();
    }

    public void Initialize()
    {
        if (DataContext is SpeedLimiterViewModel vm)
        {
            vm.CloseRequested += () => Close();
        }
    }
}


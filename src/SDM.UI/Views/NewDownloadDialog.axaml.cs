using Avalonia.Controls;
using SDM.UI.ViewModels;
namespace SDM.UI.Views;

public partial class NewDownloadDialog : Window
{
    public NewDownloadDialog()
    {
        InitializeComponent();
        DataContext = new NewDownloadViewModel();
    }
}
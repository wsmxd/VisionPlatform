using System.Windows.Controls;
using VisionPlatform.Infrastructure;

namespace VisionPlatform.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        DataContext = ServiceLocator.LogVm;
    }
}

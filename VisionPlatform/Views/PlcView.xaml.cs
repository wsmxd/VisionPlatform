using System.Windows.Controls;
using VisionPlatform.Infrastructure;

namespace VisionPlatform.Views;

public partial class PlcView : UserControl
{
    public PlcView()
    {
        InitializeComponent();
        DataContext = ServiceLocator.PlcVm;
    }
}

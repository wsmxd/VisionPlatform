using System.Windows;
using VisionPlatform.Infrastructure;

namespace VisionPlatform;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = ServiceLocator.MainViewModel;
    }
}

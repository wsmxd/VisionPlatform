using System.Windows;
using System.Windows.Controls;
using OpenCvSharp;
using VisionPlatform.Infrastructure;
using VisionPlatform.Models;
using VisionPlatform.ViewModels;

namespace VisionPlatform.Views;

public partial class InspectView : UserControl
{
    public InspectView()
    {
        InitializeComponent();
        var vm = ServiceLocator.Inspect;
        DataContext = vm;

        vm.FrameReady += OnFrameReady;
        vm.OverlayRequested += OnOverlayRequested;
        vm.OverlaysCleared += () => ImageDisplay.ClearOverlays();
        Unloaded += (_, _) =>
        {
            vm.FrameReady -= OnFrameReady;
            vm.OverlayRequested -= OnOverlayRequested;
        };
    }

    private void OnFrameReady(Mat frame)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnFrameReady(frame));
            return;
        }
        ImageDisplay.SetFrame(frame);
        frame.Dispose();
        UpdateRoi();
    }

    private void UpdateRoi()
    {
        var r = ServiceLocator.Inspect.Recipe;
        ImageDisplay.SetRoi(new OpenCvSharp.Rect((int)r.RoiX, (int)r.RoiY, (int)r.RoiW, (int)r.RoiH));
    }

    private void OnOverlayRequested(Defect defect) => ImageDisplay.AddOverlay(defect);
}

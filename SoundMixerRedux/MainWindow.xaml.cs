using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;

namespace SoundMixerRedux;

/// <summary>
/// The application window. Hosts the custom title bar and a Frame that displays MainPage.
/// On first layout the window is sized to fit its content (DPI-aware) and centered.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Fallback minimums (logical px) if content measurement comes back too small.
    private const double MinLogicalWidth = 760;
    private const double MinLogicalHeight = 430;

    private bool _sized;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        RootFrame.Navigate(typeof(MainPage));

        if (Content is FrameworkElement root)
            root.Loaded += OnRootLoaded;
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_sized || sender is not FrameworkElement root)
            return;
        _sized = true;

        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size desired = root.DesiredSize;

        double scale = root.XamlRoot?.RasterizationScale ?? 1.0;

        double logicalW = Math.Max(desired.Width, MinLogicalWidth);
        double logicalH = Math.Max(desired.Height, MinLogicalHeight);

        int w = (int)Math.Ceiling(logicalW * scale);
        int h = (int)Math.Ceiling(logicalH * scale);

        // Never exceed the monitor work area.
        RectInt32 workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        w = Math.Min(w, workArea.Width);
        h = Math.Min(h, workArea.Height);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Restore();

        AppWindow.ResizeClient(new SizeInt32(w, h));

        int x = workArea.X + (workArea.Width - AppWindow.Size.Width) / 2;
        int y = workArea.Y + (workArea.Height - AppWindow.Size.Height) / 2;
        AppWindow.Move(new PointInt32(x, y));
    }
}

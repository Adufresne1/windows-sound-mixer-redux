using System;
using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SoundMixerRedux.Services;
using SoundMixerRedux.ViewModels;
using Windows.Foundation;
using Windows.Graphics;

namespace SoundMixerRedux;

/// <summary>
/// The application window. Hosts the custom title bar and a Frame that displays MainPage.
/// Persisted bounds (validated against connected displays) and always-on-top are applied in the
/// constructor — before Activate() — so the window opens in place instead of being repositioned after
/// it is shown (repositioning across a monitor boundary post-show caused an intermittent COM crash).
/// If there are no saved bounds, it sizes to content once the content is measurable.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Fallback minimums (logical px) if content measurement comes back too small.
    private const double MinLogicalWidth = 760;
    private const double MinLogicalHeight = 430;

    private bool _sizedOnce;
    private MixerViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        RootFrame.Navigate(typeof(MainPage));

        // Always-on-top is safe to apply immediately; window sizing waits until content is loaded.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = SettingsService.Current.AlwaysOnTop;

        if (Content is FrameworkElement root)
            root.Loaded += OnRootLoaded;

        Closed += OnWindowClosed;
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement root)
            return;

        // Size/position only after the content (and its swapchain) exist — repositioning earlier
        // (in the ctor, before the window is shown) intermittently crashed in native COM.
        if (!_sizedOnce)
        {
            _sizedOnce = true;
            if (!TryRestoreBounds(SettingsService.Current))
                SizeToContentAndCenter(root);
        }

        if (_viewModel == null && RootFrame.Content is MainPage page)
        {
            _viewModel = page.ViewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MixerViewModel.AlwaysOnTop) &&
            _viewModel != null &&
            AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _viewModel.AlwaysOnTop;
        }
    }

    private bool TryRestoreBounds(AppSettings settings)
    {
        if (settings.WindowX is not int x || settings.WindowY is not int y ||
            settings.WindowWidth is not int w || settings.WindowHeight is not int h ||
            w <= 0 || h <= 0)
        {
            return false;
        }

        // Find the display holding the saved window's centre; if it's gone, fall back to centering.
        var display = FindDisplayForCenter(x + w / 2, y + h / 2);
        if (display == null)
            return false;

        // Clamp fully within that one display's work area. Restoring a rect that straddles two
        // monitors made MoveAndResize crash intermittently (native COM re-entrancy at the DPI boundary).
        RectInt32 wa = display.WorkArea;
        int cw = Math.Min(w, wa.Width);
        int ch = Math.Min(h, wa.Height);
        int cx = Math.Clamp(x, wa.X, wa.X + wa.Width - cw);
        int cy = Math.Clamp(y, wa.Y, wa.Y + wa.Height - ch);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Restore();

        // Separate Resize + Move (not MoveAndResize): the combined call crashed intermittently in
        // native COM on this Windows App SDK build; the two-step form matches the stable content path.
        AppWindow.Resize(new SizeInt32(cw, ch));
        AppWindow.Move(new PointInt32(cx, cy));
        return true;
    }

    /// <summary>The display nearest the saved window centre (Nearest keeps us on-screen even if the
    /// original monitor was unplugged or the layout rearranged). Uses GetFromPoint rather than
    /// FindAll(), whose COM enumeration was crashing intermittently at startup.</summary>
    private static DisplayArea? FindDisplayForCenter(int cx, int cy)
        => DisplayArea.GetFromPoint(new PointInt32(cx, cy), DisplayAreaFallback.Nearest);

    private void SizeToContentAndCenter(FrameworkElement root)
    {
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size desired = root.DesiredSize;

        double scale = root.XamlRoot?.RasterizationScale ?? 1.0;

        double logicalW = Math.Max(desired.Width, MinLogicalWidth);
        double logicalH = Math.Max(desired.Height, MinLogicalHeight);

        int w = (int)Math.Ceiling(logicalW * scale);
        int h = (int)Math.Ceiling(logicalH * scale);

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

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        // Only persist bounds from the normal (restored) state — not minimized/maximized.
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State != OverlappedPresenterState.Restored)
        {
            return;
        }

        var settings = SettingsService.Current;
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        settings.WindowX = pos.X;
        settings.WindowY = pos.Y;
        settings.WindowWidth = size.Width;
        settings.WindowHeight = size.Height;
        SettingsService.Save();
    }
}

using System;
using System.Collections.Specialized;
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
/// The window resizes normally (user drag); the content scales to fit the current client size
/// instead of the window resizing to fit the content (see MixerViewModel.BoardScale). Position and
/// size are both persisted. Sizing/position happen in OnRootLoaded (content measurable) rather than
/// the constructor — repositioning before the window is shown intermittently crashed in native COM.
/// </summary>
public sealed partial class MainWindow : Window
{
    // Fallback minimums (logical px) for the window itself, regardless of content scale.
    private const double MinLogicalWidth = 760;
    // Also enforced as a live drag-resize floor (see OnRootLoaded) — comfortably above the board's
    // natural (Scale=1) height (~430), so BoardScale never needs to shrink vertically far enough to
    // reach the built-in Slider template's own breaking point.
    private const double MinLogicalHeight = 480;

    // Content scale floor/ceiling — below the floor the ScrollViewer in MainPage takes over instead
    // of shrinking tracks further; above the ceiling tracks stop growing (no benefit past that size).
    private const double MinScale = 0.65;
    private const double MaxScale = 1.6;

    // RecomputeScale's measurement pass (infinite available space) never shows a scrollbar, but the
    // real, finite layout can — if the correction targets an exact fit, DPI/font rounding can tip the
    // real layout just past that edge, popping a scrollbar the calculation didn't reserve room for and
    // making the board jump instead of shrinking smoothly. A few px of slack keeps scale comfortably
    // under the exact-fit line so that never happens.
    private const double SizingSlack = 4;

    private bool _sizedOnce;
    private FrameworkElement? _root;
    private MixerViewModel? _viewModel;

    // Pin: SetTitleBar(null) turned out not to withdraw the native TitleBar control's own drag
    // region (confirmed by testing — the window still dragged with no title bar assigned), so instead
    // we re-snap to this anchor the instant AppWindow reports a position change while Pinned is on.
    private PointInt32 _pinAnchor;
    private bool _applyingPinSnap;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        RootFrame.Navigate(typeof(MainPage));

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

        if (_viewModel == null && RootFrame.Content is MainPage page)
        {
            _viewModel = page.ViewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Outputs.CollectionChanged += OnChannelsChanged;
            _viewModel.Inputs.CollectionChanged += OnChannelsChanged;
        }

        // Size/position only after the content (and its swapchain) exist — repositioning earlier
        // (in the ctor, before the window is shown) intermittently crashed in native COM.
        if (!_sizedOnce)
        {
            _sizedOnce = true;
            _root = root;

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                double rasterScale = root.XamlRoot?.RasterizationScale ?? 1.0;
                presenter.PreferredMinimumHeight = (int)Math.Ceiling(MinLogicalHeight * rasterScale);
            }

            bool restoredPosition = TryRestorePosition(SettingsService.Current);
            InitializeSize(root, keepPosition: restoredPosition);

            // After the window has landed at its real starting position — capturing the anchor any
            // earlier would pin it to wherever it happened to be before TryRestorePosition/InitializeSize ran.
            if (_viewModel != null)
                ApplyPinned(_viewModel.Pinned);

            AppWindow.Changed += OnAppWindowChanged;
        }
    }

    private void OnChannelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_root == null)
            return;

        // Defer to the next UI pass so the ItemsControl has finished adding/removing its container
        // before we measure — measuring mid-collection-change would read stale desired size.
        DispatcherQueue.TryEnqueue(RecomputeScale);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
            RecomputeScale();

        if (args.DidPositionChange && !_applyingPinSnap && _viewModel?.Pinned == true)
        {
            _applyingPinSnap = true;
            try { AppWindow.Move(_pinAnchor); }
            finally { _applyingPinSnap = false; }
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
        else if (e.PropertyName == nameof(MixerViewModel.Pinned) && _viewModel != null)
        {
            ApplyPinned(_viewModel.Pinned);
        }
    }

    /// <summary>Pin: captures the position to re-snap to whenever it drifts (see OnAppWindowChanged).
    /// Only needs to act when turning on — the anchor is simply wherever the window already is.</summary>
    private void ApplyPinned(bool pinned)
    {
        if (pinned)
            _pinAnchor = AppWindow.Position;
    }

    private bool TryRestorePosition(AppSettings settings)
    {
        if (settings.WindowX is not int x || settings.WindowY is not int y)
            return false;

        // Find the display holding the saved position; if it's gone, fall back to centering.
        var display = FindDisplayForCenter(x, y);
        if (display == null)
            return false;

        RectInt32 workArea = display.WorkArea;
        int cx = Math.Clamp(x, workArea.X, workArea.X + workArea.Width - 1);
        int cy = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - 1);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Restore();

        AppWindow.Move(new PointInt32(cx, cy));
        return true;
    }

    /// <summary>The display nearest the given point (Nearest keeps us on-screen even if the original
    /// monitor was unplugged or the layout rearranged). Uses GetFromPoint rather than FindAll(), whose
    /// COM enumeration was crashing intermittently at startup.</summary>
    private static DisplayArea? FindDisplayForCenter(int cx, int cy)
        => DisplayArea.GetFromPoint(new PointInt32(cx, cy), DisplayAreaFallback.Nearest);

    /// <summary>Desired size of the content at whatever BoardScale is currently applied.</summary>
    private static (double width, double height) MeasureContent(FrameworkElement root)
    {
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size desired = root.DesiredSize;
        return (desired.Width, desired.Height);
    }

    /// <summary>First-run sizing: restores the persisted client size if present, otherwise falls back
    /// to the natural content size at Scale=1. Position handling mirrors the old SizeToContent — kept
    /// (clamped on-screen) when restored, centered otherwise. Finishes by computing the initial scale
    /// for whatever size was applied.</summary>
    private void InitializeSize(FrameworkElement root, bool keepPosition)
    {
        var settings = SettingsService.Current;
        double rasterScale = root.XamlRoot?.RasterizationScale ?? 1.0;

        int w, h;
        if (settings.WindowWidth is int savedW && settings.WindowHeight is int savedH)
        {
            w = savedW;
            h = savedH;
        }
        else
        {
            // BoardScale is still its default (1.0) here — RecomputeScale hasn't run yet.
            var (naturalWidth, naturalHeight) = MeasureContent(root);
            w = (int)Math.Ceiling(naturalWidth * rasterScale);
            h = (int)Math.Ceiling(naturalHeight * rasterScale);
        }

        PointInt32 pos = AppWindow.Position;
        var display = (keepPosition ? FindDisplayForCenter(pos.X, pos.Y) : null)
            ?? DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);

        RectInt32 workArea = display.WorkArea;
        w = Math.Clamp(w, (int)Math.Ceiling(MinLogicalWidth * rasterScale), workArea.Width);
        h = Math.Clamp(h, (int)Math.Ceiling(MinLogicalHeight * rasterScale), workArea.Height);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Restore();

        AppWindow.ResizeClient(new SizeInt32(w, h));

        int x, y;
        if (keepPosition)
        {
            x = Math.Clamp(pos.X, workArea.X, workArea.X + workArea.Width - AppWindow.Size.Width);
            y = Math.Clamp(pos.Y, workArea.Y, workArea.Y + workArea.Height - AppWindow.Size.Height);
        }
        else
        {
            x = workArea.X + (workArea.Width - AppWindow.Size.Width) / 2;
            y = workArea.Y + (workArea.Height - AppWindow.Size.Height) / 2;
        }

        // Separate Resize + Move (not MoveAndResize): the combined call crashed intermittently in
        // native COM on this Windows App SDK build.
        AppWindow.Move(new PointInt32(x, y));

        RecomputeScale();
    }

    /// <summary>Updates BoardScale so the content fills the window's current client size — the window
    /// itself is never resized here. A single estimate from the size at Scale=1 undershoots: things like
    /// the ItemsControl spacing, the divider, page padding and the device-selector ComboBoxes in
    /// MainPage don't scale with BoardScale, only the ChannelStrip cards do, so the content's actual
    /// size isn't a linear function of scale alone. Instead we measure at the current trial scale and
    /// correct by the ratio of available to actual size, repeating until it converges (usually 1-2
    /// passes). Below MinScale the ScrollViewer in MainPage takes over instead of shrinking further.</summary>
    private void RecomputeScale()
    {
        if (_viewModel == null || _root == null)
            return;

        double rasterScale = _root.XamlRoot?.RasterizationScale ?? 1.0;
        SizeInt32 clientSize = AppWindow.ClientSize;
        double availableWidth = clientSize.Width / rasterScale;
        double availableHeight = clientSize.Height / rasterScale;

        double scale = _viewModel.BoardScale;
        for (int i = 0; i < 4; i++)
        {
            if (_viewModel.BoardScale != scale)
                _viewModel.BoardScale = scale;

            var (width, height) = MeasureContent(_root);
            if (width <= 0 || height <= 0)
                return;

            double factor = Math.Min((availableWidth - SizingSlack) / width, (availableHeight - SizingSlack) / height);
            double next = Math.Clamp(scale * factor, MinScale, MaxScale);
            if (Math.Abs(next - scale) < 0.002)
            {
                scale = next;
                break;
            }
            scale = next;
        }

        _viewModel.BoardScale = scale;
    }

    /// <summary>Stick to right (Settings): the X a programmatic resize should land on to keep the
    /// right edge fixed instead of the left. Nothing calls this yet — today's only resizes are the
    /// user's own drag (which already anchors wherever they grab) and the one-time startup sizing
    /// (which has no "old" window to anchor against). Reserved for the auto-growth-past-MaxScale
    /// idea noted in RecomputeScale's own history, if that's ever built.</summary>
    private static int AnchorX(int oldX, int oldWidth, int newWidth, bool stickToRight)
        => stickToRight ? oldX + (oldWidth - newWidth) : oldX;

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        // Only persist position/size from the normal (restored) state — not minimized/maximized.
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State != OverlappedPresenterState.Restored)
        {
            return;
        }

        var settings = SettingsService.Current;
        var pos = AppWindow.Position;
        settings.WindowX = pos.X;
        settings.WindowY = pos.Y;
        settings.WindowWidth = AppWindow.ClientSize.Width;
        settings.WindowHeight = AppWindow.ClientSize.Height;
        SettingsService.Save();
    }
}

using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SphereDesk.App.ViewModels;
using SphereDesk.Core.Models;
using SphereDesk.Windows.Integration;
using SphereDesk.Windows.Shell;

namespace SphereDesk.App;

public sealed class SphereView : FrameworkElement
{
    private MainViewModel? _model;
    private Window? _window;
    private bool _hooked;
    private bool _suspended;
    private bool _over;
    private bool _down;
    private bool _pressed;
    private bool _released;
    private bool _captureLost;
    private bool _releasingCapture;
    private bool _hasClock;
    private bool _frameQueued;
    private Typeface? _typeface;
    private SolidColorBrush? _labelBrush;
    private double _textDpi = -1;
    private readonly Dictionary<string, FormattedText> _labelCache = new();
    private readonly Dictionary<(string Glyph, int Size), FormattedText> _glyphCache = new();
    private int _clickCount = 1;
    private long _lastTimestamp;
    private Point _pointer;
    private DispatcherTimer? _menuWatch;
    private int _menuWatchTicks;
    private bool _menuBusy;

    public bool TransparentSurface { get; set; }

    public SphereView()
    {
        Focusable = false;
        ClipToBounds = true;
        DataContextChanged += (_, _) => AttachModel(DataContext as MainViewModel);
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (_window is not null)
        {
            _window.StateChanged -= OnWindowStateChanged;
            _window.IsVisibleChanged -= OnWindowVisibilityChanged;
            _window.Deactivated -= OnDeactivated;
        }

        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.StateChanged += OnWindowStateChanged;
            _window.IsVisibleChanged += OnWindowVisibilityChanged;
            _window.Deactivated += OnDeactivated;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        _over = true;
        Advance(0, false);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _over = false;
        Advance(0, false);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _pointer = e.GetPosition(this);
        Advance(0, false);
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _pointer = e.GetPosition(this);
        _down = true;
        _pressed = true;
        _clickCount = e.ClickCount;
        _captureLost = false;
        Advance(0, false);
        _pressed = false;
        e.Handled = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _pointer = e.GetPosition(this);
        _down = false;
        _released = true;
        Advance(0, false);
        _released = false;
        e.Handled = true;
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        _pointer = e.GetPosition(this);
        ShowItemMenu(e.GetPosition(this));
        e.Handled = true;
        base.OnMouseRightButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (!_releasingCapture && _down)
        {
            _captureLost = true;
            _down = false;
            Advance(0, false);
            _captureLost = false;
        }

        base.OnLostMouseCapture(e);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_model is null || _suspended || sizeInfo.NewSize.Width < 2 || sizeInfo.NewSize.Height < 2)
        {
            return;
        }

        Advance(0, false);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!TransparentSurface)
        {
            var background = new SolidColorBrush(Color.FromRgb(18, 20, 26));
            background.Freeze();
            drawingContext.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        EnsureDrawResources(DpiOf(this).PixelsPerDip);
        var frame = _model?.Frame;
        if (frame is null || _model is null)
        {
            return;
        }

        var dpi = _textDpi;
        foreach (var item in frame.Items)
        {
            var center = new Point(item.CenterDip.X, item.CenterDip.Y);
            var radius = item.HitBoundsDip.Width / 2;
            var icon = _model.IconFor(item.ItemId);
            var opacity = item.Opacity;
            if (icon is not null)
            {
                drawingContext.PushOpacity(opacity);
                drawingContext.DrawImage(icon, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
                drawingContext.Pop();
            }
            else
            {
                var fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 42, 49, 66));
                fill.Freeze();
                drawingContext.DrawEllipse(fill, null, center, radius, radius);
                var glyph = GlyphText(Glyph(item.ItemId), Math.Max(10, radius), dpi);
                drawingContext.DrawText(glyph, new Point(center.X - (glyph.Width / 2), center.Y - (glyph.Height / 2)));
            }

            if (!item.ShowLabel)
            {
                continue;
            }

            var text = LabelText(_model.NameFor(item.ItemId), dpi);
            drawingContext.DrawText(text, new Point(center.X - (text.Width / 2), item.HitBoundsDip.Bottom + 2));
        }
    }

    private void EnsureDrawResources(double dpi)
    {
        _typeface ??= new Typeface("Segoe UI, Microsoft YaHei UI");
        if (_labelBrush is null)
        {
            _labelBrush = new SolidColorBrush(Color.FromRgb(200, 208, 220));
            _labelBrush.Freeze();
        }

        if (dpi == _textDpi)
        {
            return;
        }

        _textDpi = dpi;
        _labelCache.Clear();
        _glyphCache.Clear();
    }

    private FormattedText LabelText(string name, double dpi)
    {
        if (_labelCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var text = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface!, 12, _labelBrush, dpi)
        {
            MaxTextWidth = 96,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        _labelCache[name] = text;
        return text;
    }

    private FormattedText GlyphText(string glyph, double size, double dpi)
    {
        var key = (glyph, (int)Math.Round(size));
        if (_glyphCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var text = new FormattedText(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface!, key.Item2, Brushes.White, dpi);
        _glyphCache[key] = text;
        return text;
    }

    private void AttachModel(MainViewModel? model)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.Items.CollectionChanged -= OnItemsChanged;
        }

        _model = model;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            _model.Items.CollectionChanged += OnItemsChanged;
            Advance(0, false);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueFrame();

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Frame))
        {
            InvalidateVisual();
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.IconsRevision) or nameof(MainViewModel.ReducedMotion) or nameof(MainViewModel.AutoRotateEnabled))
        {
            QueueFrame();
        }
    }

    private void QueueFrame()
    {
        if (_frameQueued || _model is null || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _frameQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _frameQueued = false;
            if (_model is null)
            {
                return;
            }

            Advance(0, _suspended);
        }));
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateSuspension();

    private void OnWindowVisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e) => UpdateSuspension();

    private void UpdateSuspension()
    {
        if (_window is null)
        {
            if (_suspended)
            {
                Resume();
            }

            return;
        }

        var interactive = _window is { IsVisible: true, WindowState: not WindowState.Minimized };
        if (!interactive)
        {
            Suspend();
        }
        else if (_suspended)
        {
            Resume();
        }
    }

    public void Tick()
    {
        if (_suspended)
        {
            Resume();
            return;
        }

        Advance(0, false);
    }

    public bool ContainsDesktopPoint(double x, double y) => IsInsideSphere(x, y);

    public double HitRadiusDip
    {
        get
        {
            if (ActualWidth < 2 || ActualHeight < 2)
            {
                return 0;
            }

            var halfMin = Math.Min(ActualWidth, ActualHeight) / 2;
            var guideRadius = Math.Max(0, halfMin - 60);
            return guideRadius + 28 + 48;
        }
    }

    public void SetDesktopPointer(double x, double y)
    {
        _pointer = new Point(x, y);
        _over = IsInsideSphere(x, y);
    }

    public void DesktopInput(double x, double y, bool over, bool down, bool pressed, bool released, int clickCount)
    {
        _pointer = new Point(x, y);
        _over = over && IsInsideSphere(x, y);
        _down = down;
        _pressed = pressed;
        _released = released;
        _clickCount = Math.Max(1, clickCount);
        Advance(0, false);
        _pressed = false;
        _released = false;
    }

    private bool IsInsideSphere(double x, double y)
    {
        var radius = HitRadiusDip;
        if (radius <= 0)
        {
            return false;
        }

        var dx = x - (ActualWidth / 2);
        var dy = y - (ActualHeight / 2);
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static DpiScale DpiOf(Visual visual)
    {
        try
        {
            return VisualTreeHelper.GetDpi(visual);
        }
        catch (InvalidOperationException)
        {
            return new DpiScale(1, 1);
        }
    }

    public void DesktopRightClick(double x, double y, nint owner)
    {
        _pointer = new Point(x, y);
        var point = _pointer;
        var hasScreen = DesktopLayerHost.TryGetScreenCursor(out var screenX, out var screenY);
        ShowDesktopShellMenu(point, owner, hasScreen, screenX, screenY);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_down)
        {
            _captureLost = true;
            _down = false;
            Advance(0, false);
            _captureLost = false;
        }
    }

    private void Suspend()
    {
        _suspended = true;
        Unhook();
        Advance(0, true);
    }

    private void Resume()
    {
        _suspended = false;
        _hasClock = false;
        Advance(0, false);
    }

    private void Advance(double forcedElapsed, bool suspended)
    {
        if (_model is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = forcedElapsed;
        if (!suspended && _hasClock)
        {
            elapsed = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        }

        _lastTimestamp = now;
        _hasClock = !suspended;
        var dpi = DpiOf(this);
        _model.NoteDpi(dpi.DpiScaleX);
        var frame = _model.Advance(new SpherePointer
        {
            ElapsedSeconds = elapsed,
            WindowInteractive = !suspended && _window is not { WindowState: WindowState.Minimized },
            Over = _over && !suspended,
            Down = _down,
            Pressed = _pressed,
            Released = _released,
            CaptureLost = _captureLost,
            X = _pointer.X,
            Y = _pointer.Y,
            ClickCount = _clickCount,
            DragThreshold = DragMetrics.ThresholdDip(dpi.DpiScaleX),
            Width = Math.Max(1, ActualWidth),
            Height = Math.Max(1, ActualHeight),
            DpiScale = dpi.DpiScaleX,
        });
        if (frame?.CaptureRequested == true && PresentationSource.FromVisual(this) is not null)
        {
            CaptureMouse();
        }

        if (frame?.CaptureReleaseRequested == true && IsMouseCaptured && PresentationSource.FromVisual(this) is not null)
        {
            _releasingCapture = true;
            ReleaseMouseCapture();
            _releasingCapture = false;
        }

        InvalidateVisual();
        if (frame?.NeedsContinuousRender == true && !_suspended)
        {
            Hook();
        }
        else if (frame?.NeedsContinuousRender != true)
        {
            Unhook();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_suspended)
        {
            Unhook();
            return;
        }

        Advance(0, false);
    }

    private void Hook()
    {
        if (_hooked)
        {
            return;
        }

        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    private void ShowDesktopShellMenu(Point point, nint owner, bool screenKnown, int screenX, int screenY)
    {
        if (_model?.Frame is null)
        {
            return;
        }

        if (!screenKnown && !DesktopLayerHost.TryGetScreenCursor(out screenX, out screenY))
        {
            return;
        }

        if (_menuBusy)
        {
            return;
        }

        var hit = _model.Frame.Items
            .Where(item => HitsItem(item, point))
            .OrderByDescending(item => item.DrawOrder)
            .FirstOrDefault();
        _menuBusy = true;
        EndMenuWatch(pop: true);
        _model.PushMenu();
        if (hit is not null)
        {
            var shown = ShowFileMenu(hit.ItemId, owner, screenX, screenY);
            _model.PopMenu();
            _menuBusy = false;
            if (!shown)
            {
                AppLog.Write("ShellMenuFailed");
            }

            return;
        }

        var desktopShown = ShellContextMenu.ShowForDesktop(owner, screenX, screenY);
        if (!desktopShown)
        {
            _model.PopMenu();
            _menuBusy = false;
            AppLog.Write("ShellMenuFailed");
            return;
        }

        _menuBusy = false;

        _menuWatchTicks = 0;
        _menuWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _menuWatch.Tick += (_, _) =>
        {
            _menuWatchTicks++;
            if (ShellContextMenu.IsSystemMenuOpen())
            {
                ShellContextMenu.KeepSurfaceBehind(owner);
                return;
            }

            if (_menuWatchTicks < 8)
            {
                return;
            }

            EndMenuWatch(pop: true);
        };
        _menuWatch.Start();
    }

    private void EndMenuWatch(bool pop)
    {
        if (_menuWatch is null)
        {
            return;
        }

        _menuWatch.Stop();
        _menuWatch = null;
        if (pop)
        {
            _model?.PopMenu();
        }
    }

    private bool ShowFileMenu(string itemId, nint owner, int screenX, int screenY)
    {
        _model!.SelectItem(itemId);
        return ShellContextMenu.ShowForPath(owner, screenX, screenY, _model.PathFor(itemId));
    }

    private void ShowItemMenu(Point point, bool screenKnown = false, int screenX = 0, int screenY = 0)
    {
        if (_model?.Frame is null)
        {
            return;
        }

        var hit = _model.Frame.Items
            .Where(item => HitsItem(item, point))
            .OrderByDescending(item => item.DrawOrder)
            .FirstOrDefault();
        if (hit is null)
        {
            return;
        }

        _model.SelectItem(hit.ItemId);
        var menu = new System.Windows.Controls.ContextMenu();
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "打开", Command = _model.OpenCommand });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "加入分组", Command = _model.AddToGroupCommand });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "移出当前分组", Command = _model.RemoveFromGroupCommand });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "定位到正面", Command = _model.AlignCommand });
        menu.Opened += (_, _) => _model.PushMenu();
        menu.Closed += (_, _) => _model.PopMenu();
        Window? host = null;
        if (PresentationSource.FromVisual(this) is null)
        {
            if (!screenKnown && !DesktopLayerHost.TryGetScreenCursor(out screenX, out screenY))
            {
                return;
            }

            host = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                Width = 1,
                Height = 1,
                Left = screenX,
                Top = screenY,
                Topmost = true,
            };
            host.Show();
            var dpi = VisualTreeHelper.GetDpi(host);
            var dipX = screenX / dpi.DpiScaleX;
            var dipY = screenY / dpi.DpiScaleY;
            host.Left = dipX;
            host.Top = dipY;
            menu.PlacementTarget = host;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = dipX;
            menu.VerticalOffset = dipY;
            menu.Closed += (_, _) => host.Close();
        }
        else
        {
            menu.PlacementTarget = this;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        }

        try
        {
            menu.IsOpen = true;
        }
        catch (InvalidOperationException)
        {
            host?.Close();
            AppLog.Write("DesktopMenuFailed");
        }
    }

    private static bool HitsItem(ProjectedItem item, Point point)
    {
        if (item.IsBackFacing)
        {
            return false;
        }

        if (item.HitBoundsDip.Contains(point.X, point.Y))
        {
            return true;
        }

        if (!item.ShowLabel)
        {
            return false;
        }

        var left = item.CenterDip.X - 48;
        var top = item.HitBoundsDip.Bottom + 2;
        return point.X >= left && point.Y >= top && point.X < left + 96 && point.Y < top + 18;
    }

    private string Glyph(string itemId) => _model?.KindOf(itemId) switch
    {
        ItemKind.Folder => "夹",
        ItemKind.Shortcut => "链",
        ItemKind.InternetShortcut => "址",
        _ => "档",
    };
}

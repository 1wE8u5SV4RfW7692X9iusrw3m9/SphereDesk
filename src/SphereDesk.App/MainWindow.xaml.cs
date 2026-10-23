using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SphereDesk.Application.Desktop;
using SphereDesk.App.ViewModels;
using SphereDesk.Core.Geometry;
using SphereDesk.Core.Models;
using SphereDesk.Windows.Integration;

namespace SphereDesk.App;

public partial class MainWindow : Window
{
    private readonly DesktopSession _session;
    private readonly MainViewModel _model;
    private readonly ShellHost _shell = new();
    private Window? _desktop;
    private DesktopIconVisibility? _desktopIcons;
    private DispatcherTimer? _desktopWatch;
    private bool _desktopMode;
    private bool _exitRequested;
    private SphereView? _desktopSphere;
    private nint _surface;
    private DispatcherTimer? _presentTimer;
    private bool _surfaceDown;
    private bool _desktopHand;
    private double _arrangedDipWidth;
    private double _arrangedDipHeight;
    private bool _presenting;
    private bool _presentLogged;
    private bool _presentFailed;
    private int _frameSamples;
    private long _frameWindowStart;
    private int _spriteIconRevision = -1;
    private readonly Dictionary<string, CachedSprite> _spriteIcons = new();
    private readonly Dictionary<string, CachedSprite> _spriteLabels = new();
    private readonly List<DesktopLayerHost.DesktopSprite> _sprites = new();
    private bool _syncingGroups;
    private bool _placed;

    public MainWindow(DesktopSession session, bool synthetic)
    {
        InitializeComponent();
        _session = session;
        _model = new MainViewModel(session, Dispatcher, () => new WindowInteropHelper(this).Handle);
        _model.AskText = (title, initial) =>
        {
            _model.PushMenu();
            try
            {
                return PromptWindow.Ask(this, title, "名称", initial);
            }
            finally
            {
                _model.PopMenu();
            }
        };
        _model.Confirm = message =>
        {
            _model.PushMenu();
            try
            {
                return MessageBox.Show(this, message, "SphereDesk", MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK;
            }
            finally
            {
                _model.PopMenu();
            }
        };
        DataContext = _model;
        KindFilter.ItemsSource = new[] { "全部类型", "文件", "文件夹", "快捷方式", "网页快捷方式" };
        KindFilter.SelectedIndex = 0;
        KindFilter.SelectionChanged += (_, _) =>
        {
            _model.KindFilter = KindFilter.SelectedIndex switch
            {
                1 => ItemKind.File,
                2 => ItemKind.Folder,
                3 => ItemKind.Shortcut,
                4 => ItemKind.InternetShortcut,
                _ => null,
            };
        };
        GroupList.SelectionChanged += OnGroupSelectionChanged;
        _model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.Groups))
            {
                SelectCurrentGroup();
            }
        };
        SearchBox.GotKeyboardFocus += (_, _) => _model.SearchFocused = true;
        SearchBox.LostKeyboardFocus += (_, _) => _model.SearchFocused = false;
        SearchBox.PreviewKeyDown += OnSearchKeyDown;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        LocationChanged += OnBoundsChanged;
        SizeChanged += OnBoundsChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SelectCurrentGroup();
        _ = synthetic;
    }

    public void EnableDesktopMode()
    {
        _desktopMode = true;
        Sphere.DataContext = "detached";
        Visibility = Visibility.Hidden;
        new WindowInteropHelper(this).EnsureHandle();
        CreateDesktopSurface();
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var bounds = _session.Settings.WindowBoundsDip;
        if (bounds is not null)
        {
            Left = bounds.X;
            Top = bounds.Y;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        EnsureOnScreen();
        _shell.Create();
        _shell.HotkeyPressed += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_desktopMode)
            {
                TogglePanel();
            }
            else
            {
                BringToFront();
            }
        }));
        _shell.ShowRequested += () => Dispatcher.BeginInvoke(BringToFront);
        _shell.ExitRequested += () => Dispatcher.BeginInvoke(() =>
        {
            _exitRequested = true;
            Close();
        });
        _shell.DisplayChanged += () => Dispatcher.BeginInvoke(() =>
        {
            EnsureOnScreen();
            FitDesktop();
        });
        _shell.AddTray();
        if (!_shell.TryRegisterHotkey(_session.Settings.Hotkey, out _))
        {
            MessageBox.Show(this, "热键已被其他程序占用。请在设置中更换，当前仍可从托盘图标打开窗口。", "SphereDesk", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        _placed = true;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_desktopMode && !_exitRequested)
        {
            e.Cancel = true;
            HidePanel();
            return;
        }

        RememberBounds();
        _desktopWatch?.Stop();
        _presentTimer?.Stop();
        if (_surface != 0)
        {
            DesktopLayerHost.DestroySurface(_surface);
            _surface = 0;
        }

        if (_desktop is not null)
        {
            var desktop = _desktop;
            _desktop = null;
            desktop.Close();
        }

        DesktopLayerHost.SetIconViewVisible(true);
        _desktopIcons?.Dispose();
        _desktopIcons = null;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _shell.Dispose();
    }

    private void OnBoundsChanged(object? sender, EventArgs e)
    {
        if (_placed)
        {
            RememberBounds();
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(EnsureOnScreen));

    private void RememberBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _session.NoteWindow(new WindowBounds(Left, Top, Width, Height), handle == 0 ? _session.Settings.MonitorId : MonitorLayout.GetDeviceName(handle));
    }

    private void EnsureOnScreen()
    {
        var virtualScreen = new DipRect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var window = new DipRect(Left, Top, Width, Height);
        var names = MonitorLayout.GetDeviceNames();
        var monitorMissing = !string.IsNullOrWhiteSpace(_session.Settings.MonitorId) && !names.Contains(_session.Settings.MonitorId);
        if (!monitorMissing && WorkAreaClamp.IsSufficientlyVisible(window, virtualScreen))
        {
            return;
        }

        var work = new DipRect(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        var clamped = WorkAreaClamp.ClampInto(window, work);
        Left = clamped.X;
        Top = clamped.Y;
        Width = clamped.Width;
        Height = clamped.Height;
    }

    private void OnGroupSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_syncingGroups || GroupList.SelectedItem is not GroupRow row)
        {
            return;
        }

        _model.SelectedGroupId = row.Id;
    }

    private void SelectCurrentGroup()
    {
        _syncingGroups = true;
        GroupList.SelectedItem = _model.Groups.FirstOrDefault(group => group.Id == _model.SelectedGroupId) ?? _model.Groups.FirstOrDefault();
        _syncingGroups = false;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _model.ClearSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _model.OpenSelected(align: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            ItemList.Focus();
            e.Handled = true;
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e) => _model.OpenSelected(align: true);

    private void OnItemKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _model.OpenSelected(align: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _model.ClearSearch();
            e.Handled = true;
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        _model.PushMenu();
        try
        {
            var edited = SettingsWindow.Edit(this, _session.Settings);
            if (edited is null)
            {
                return;
            }

            var previousHotkey = _session.Settings.Hotkey;
            _model.ApplyPreferences(edited.AutoRotateEnabled, edited.AngularSpeed, edited.ReducedMotion, edited.LaunchAtStartup, edited.Hotkey);
            if (!_session.IsSynthetic)
            {
                StartupRegistration.Apply(edited.LaunchAtStartup);
            }

            if (_shell.TryRegisterHotkey(edited.Hotkey, out _))
            {
                return;
            }

            _model.ApplyPreferences(edited.AutoRotateEnabled, edited.AngularSpeed, edited.ReducedMotion, edited.LaunchAtStartup, previousHotkey);
            MessageBox.Show(this, "这个热键已被占用，请换成其他组合。其余设置已保存，热键保持原样。", "SphereDesk", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _model.PopMenu();
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        _model.PushMenu();
        try
        {
            HelpWindow.Show(this);
        }
        finally
        {
            _model.PopMenu();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (_desktopMode)
        {
            HidePanel();
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_desktopMode)
        {
            HidePanel();
            return;
        }

        Close();
    }

    private void TogglePanel()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            HidePanel();
            return;
        }

        BringToFront();
    }

    private void HidePanel()
    {
        _model.SearchFocused = false;
        Hide();
    }

    private void RemoveEmbeddedSphere()
    {
        if (Sphere.Parent is not Grid grid)
        {
            return;
        }

        var row = Grid.GetRow(Sphere);
        grid.Children.Remove(Sphere);
        if (row >= 0 && row < grid.RowDefinitions.Count)
        {
            grid.RowDefinitions[row].Height = new GridLength(0);
        }

        if (grid.RowDefinitions.Count > 2)
        {
            grid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
        }
    }

    private void CreateDesktopSurface()
    {
        _desktopSphere = new SphereView { TransparentSurface = true, DataContext = _model };
        _surface = DesktopLayerHost.CreateSurface(OnDesktopMessage);
        if (_surface == 0)
        {
            AbandonDesktop();
            return;
        }

        _desktopIcons = DesktopIconVisibility.HideForSession();
        if (!_desktopIcons.AreHidden)
        {
            AbandonDesktop();
            return;
        }

        DesktopLayerHost.SetIconViewVisible(false);
        RemoveEmbeddedSphere();
        _presentTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _presentTimer.Tick += (_, _) => PresentDesktop();
        _presentTimer.Start();
        _desktopWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _desktopWatch.Tick += (_, _) =>
        {
            DesktopLayerHost.PlaceForDesktop(_surface);
            DesktopLayerHost.SetIconViewVisible(false);
        };
        _desktopWatch.Start();
        PresentDesktop();
    }

    private void OnDesktopMessage(uint message, nint wParam, nint lParam)
    {
        _ = wParam;
        if (_desktopSphere is null)
        {
            return;
        }

        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = x / dpi.DpiScaleX;
        var dy = y / dpi.DpiScaleY;
        if (message == 0x0205)
        {
            _desktopSphere.DesktopRightClick(dx, dy, _surface);
            return;
        }

        if (message is 0x0201 or 0x0203)
        {
            _surfaceDown = true;
            _desktopSphere.DesktopInput(dx, dy, true, true, true, false, message == 0x0203 ? 2 : 1);
            return;
        }

        if (message == 0x0202)
        {
            _surfaceDown = false;
            _desktopSphere.DesktopInput(dx, dy, true, false, false, true, 1);
            return;
        }

        _desktopSphere.DesktopInput(dx, dy, true, _surfaceDown, false, false, 1);
    }

    private void PresentDesktop()
    {
        if (_presenting || _desktopSphere is null || _surface == 0 || !DesktopLayerHost.TryGetClientSize(_surface, out var width, out var height))
        {
            return;
        }

        _presenting = true;
        try
        {
            PresentDesktopCore(width, height);
        }
        finally
        {
            _presenting = false;
        }
    }

    private void PresentDesktopCore(int width, int height)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var dipWidth = width / dpi.DpiScaleX;
        var dipHeight = height / dpi.DpiScaleY;
        if (dipWidth != _arrangedDipWidth || dipHeight != _arrangedDipHeight)
        {
            _desktopSphere!.Measure(new Size(dipWidth, dipHeight));
            _desktopSphere.Arrange(new Rect(0, 0, dipWidth, dipHeight));
            _desktopSphere.UpdateLayout();
            _arrangedDipWidth = dipWidth;
            _arrangedDipHeight = dipHeight;
        }

        if (DesktopLayerHost.TryGetCursorInWindow(_surface, out var cursorX, out var cursorY))
        {
            var dipX = cursorX / dpi.DpiScaleX;
            var dipY = cursorY / dpi.DpiScaleY;
            _desktopSphere!.SetDesktopPointer(dipX, dipY);
            var inside = _desktopSphere.ContainsDesktopPoint(dipX, dipY);
            if (_surfaceDown && !DesktopLayerHost.IsPrimaryButtonDown())
            {
                _surfaceDown = false;
                _desktopSphere.DesktopInput(dipX, dipY, inside, false, false, true, 1);
            }

            var hand = inside || _surfaceDown;
            if (hand || _desktopHand)
            {
                DesktopLayerHost.SetPointerCursor(hand);
            }

            _desktopHand = hand;
        }

        _desktopSphere!.Tick();
        var frame = _model.Frame;
        if (frame is null)
        {
            return;
        }

        if (_model.IconsRevision != _spriteIconRevision)
        {
            _spriteIcons.Clear();
            _spriteIconRevision = _model.IconsRevision;
        }

        var scale = dpi.DpiScaleX;
        _sprites.Clear();
        foreach (var item in frame.Items)
        {
            var hitWidth = Math.Max(1, (int)Math.Round(item.HitBoundsDip.Width * scale));
            var hitHeight = Math.Max(1, (int)Math.Round(item.HitBoundsDip.Height * scale));
            var hitX = (int)Math.Round(item.HitBoundsDip.X * scale);
            var hitY = (int)Math.Round(item.HitBoundsDip.Y * scale);
            var opacity = (byte)Math.Clamp((int)Math.Round(item.Opacity * 255), 0, 255);
            if (TrySpriteIcon(item.ItemId, out var icon))
            {
                _sprites.Add(new DesktopLayerHost.DesktopSprite
                {
                    X = hitX,
                    Y = hitY,
                    DestinationWidth = hitWidth,
                    DestinationHeight = hitHeight,
                    SourceWidth = icon.Width,
                    SourceHeight = icon.Height,
                    Opacity = opacity,
                    Pixels = icon.Pixels,
                });
            }

            if (item.ShowLabel && TrySpriteLabel(_model.NameFor(item.ItemId), scale, out var label))
            {
                _sprites.Add(new DesktopLayerHost.DesktopSprite
                {
                    X = (int)Math.Round((item.CenterDip.X * scale) - (label.Width / 2.0)),
                    Y = (int)Math.Round((item.HitBoundsDip.Bottom + 2) * scale),
                    DestinationWidth = label.Width,
                    DestinationHeight = label.Height,
                    SourceWidth = label.Width,
                    SourceHeight = label.Height,
                    Opacity = 255,
                    Pixels = label.Pixels,
                });
            }
        }

        if (!_presentLogged && _sprites.Count > 0)
        {
            _presentLogged = true;
            AppLog.Write("DesktopPresented", _sprites.Count);
        }

        var ringRadius = (int)Math.Round(Math.Max(0, Math.Min(dipWidth, dipHeight) - 120) / 2 * scale);
        var hitRadius = (int)Math.Round(_desktopSphere!.HitRadiusDip * scale);
        if (DesktopLayerHost.PresentSprites(_surface, width, height, _sprites, width / 2, height / 2, ringRadius, hitRadius, 0, 0, 0))
        {
            NoteFrame();
        }
        else if (!_presentFailed)
        {
            _presentFailed = true;
            AppLog.Write("DesktopPresentFailed", System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
    }

    private bool TrySpriteIcon(string itemId, out CachedSprite sprite)
    {
        if (_spriteIcons.TryGetValue(itemId, out sprite))
        {
            return sprite.Pixels is { Length: > 0 };
        }

        sprite = default;
        if (_model.IconFor(itemId) is not System.Windows.Media.Imaging.BitmapSource source)
        {
            _spriteIcons[itemId] = default;
            return false;
        }

        var bitmap = source.Format == PixelFormats.Pbgra32
            ? source
            : new System.Windows.Media.Imaging.FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        sprite = new CachedSprite(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
        _spriteIcons[itemId] = sprite;
        return true;
    }

    private bool TrySpriteLabel(string name, double scale, out CachedSprite sprite)
    {
        sprite = default;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var key = scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "\n" + name;
        if (_spriteLabels.TryGetValue(key, out sprite))
        {
            return true;
        }

        var labelBrush = new SolidColorBrush(Color.FromRgb(200, 208, 220));
        labelBrush.Freeze();
        var text = new FormattedText(name, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Microsoft YaHei UI"), 12, labelBrush, scale)
        {
            MaxTextWidth = 96,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(text.Width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(text.Height * scale));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawText(text, new Point(0, 0));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[pixelWidth * pixelHeight * 4];
        bitmap.CopyPixels(pixels, pixelWidth * 4, 0);
        sprite = new CachedSprite(pixels, pixelWidth, pixelHeight);
        _spriteLabels[key] = sprite;
        return true;
    }

    private readonly record struct CachedSprite(byte[] Pixels, int Width, int Height);

    private void NoteFrame()
    {
        _frameSamples++;
        if (_frameSamples == 80)
        {
            _frameWindowStart = System.Diagnostics.Stopwatch.GetTimestamp();
            return;
        }

        if (_frameSamples != 140)
        {
            return;
        }

        var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _frameWindowStart) / (double)System.Diagnostics.Stopwatch.Frequency * 1000;
        AppLog.Write("DesktopFrameMs", (int)Math.Round(elapsedMs / 60));
    }

    private void AbandonDesktop()
    {
        _desktopWatch?.Stop();
        _presentTimer?.Stop();
        if (_surface != 0)
        {
            DesktopLayerHost.DestroySurface(_surface);
            _surface = 0;
        }

        if (_desktop is not null)
        {
            var desktop = _desktop;
            _desktop = null;
            desktop.Close();
        }

        DesktopLayerHost.SetIconViewVisible(true);
        _desktopIcons?.Dispose();
        _desktopIcons = null;
        _desktopMode = false;
        Sphere.DataContext = _model;
        ShowInTaskbar = true;
        BringToFront();
    }

    private void FitDesktop()
    {
        if (_surface != 0)
        {
            DesktopLayerHost.PlaceForDesktop(_surface);
        }
    }
}

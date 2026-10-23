using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SphereDesk.Application.Desktop;
using SphereDesk.Core.Engine;
using SphereDesk.Core.Geometry;
using SphereDesk.Core.Models;
using SphereDesk.Core.Search;
using SphereDesk.Windows.Integration;

namespace SphereDesk.App.ViewModels;

public sealed class GroupRow
{
    public string? Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class ItemRow : ObservableObject
{
    private ImageSource? _icon;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string KindLabel { get; init; }
    public required string StatusLabel { get; init; }
    public required string IconKey { get; init; }
    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }
}

public sealed class SpherePointer
{
    public double ElapsedSeconds { get; init; }
    public bool WindowInteractive { get; init; } = true;
    public bool Over { get; init; }
    public bool Down { get; init; }
    public bool Pressed { get; init; }
    public bool Released { get; init; }
    public bool CaptureLost { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public int ClickCount { get; init; } = 1;
    public double DragThreshold { get; init; } = 4;
    public double Width { get; init; } = 800;
    public double Height { get; init; } = 600;
    public double DpiScale { get; init; } = 1;
}

public sealed class MainViewModel : ObservableObject
{
    private readonly DesktopSession _session;
    private readonly Dispatcher _dispatcher;
    private readonly SphereEngine _engine = new();
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.Ordinal);
    private readonly Func<nint> _ownerHandle;
    private CancellationTokenSource _iconRequests = new();
    private List<DesktopItem> _all = [];
    private List<SourceRoot> _roots = [];
    private int _alignToken;
    private string? _alignItemId;
    private bool _syncingSelection;
    private int _menuDepth;
    private bool _searchFocused;
    private string _searchText = string.Empty;
    private string? _selectedGroupId;
    private ItemKind? _kindFilter;
    private ItemRow? _selectedRow;
    private string? _keptItemId;
    private string _statusText = "正在读取桌面…";
    private string _hoverText = string.Empty;
    private string _failureText = string.Empty;
    private string _emptyText = string.Empty;
    private bool _autoRotateEnabled;
    private double _angularSpeed = 0.15;
    private bool _reducedMotion;
    private int _iconsRevision;
    private double _dpiScale = 1;

    public MainViewModel(DesktopSession session, Dispatcher dispatcher, Func<nint> ownerHandle)
    {
        _session = session;
        _dispatcher = dispatcher;
        _ownerHandle = ownerHandle;
        _autoRotateEnabled = session.Settings.AutoRotateEnabled;
        _angularSpeed = session.Settings.AngularSpeed;
        _reducedMotion = session.Settings.ReducedMotion;
        Items = [];
        Groups = [];
        ReloadGroups();
        _session.SnapshotUpdated += (_, snapshot) => _dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
        _session.SettingsChanged += (_, _) => _dispatcher.InvokeAsync(ReloadGroups);
        RefreshCommand = new RelayCommand(() => _ = _session.RefreshAsync(CancellationToken.None));
        OpenCommand = new RelayCommand(() => OpenSelected(align: true));
        AddToGroupCommand = new RelayCommand(AddToGroup);
        RemoveFromGroupCommand = new RelayCommand(RemoveFromGroup);
        CreateGroupCommand = new RelayCommand(CreateGroup);
        RenameGroupCommand = new RelayCommand(RenameGroup);
        DeleteGroupCommand = new RelayCommand(DeleteGroup);
        RebalanceCommand = new RelayCommand(() => StatusText = _session.Rebalance().UserMessage ?? StatusText);
        AlignCommand = new RelayCommand(() =>
        {
            if (SelectedRow is not null)
            {
                RequestAlign(SelectedRow.Id);
            }
        });
    }

    public ObservableCollection<ItemRow> Items { get; }
    public ObservableCollection<GroupRow> Groups { get; }
    public SphereFrame? Frame { get; private set; }
    public bool IsSynthetic => _session.IsSynthetic;
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand AddToGroupCommand { get; }
    public RelayCommand RemoveFromGroupCommand { get; }
    public RelayCommand CreateGroupCommand { get; }
    public RelayCommand RenameGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand RebalanceCommand { get; }
    public RelayCommand AlignCommand { get; }
    public Func<string, string?, string?>? AskText { get; set; }
    public Func<string, bool>? Confirm { get; set; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }

            Rebuild();
            Raise(nameof(SearchActive));
            if (!string.IsNullOrWhiteSpace(value) && Items.Count > 0)
            {
                RequestAlign(Items[0].Id);
                SelectRow(Items[0], align: false);
            }
        }
    }

    public bool SearchFocused
    {
        get => _searchFocused;
        set
        {
            if (SetProperty(ref _searchFocused, value))
            {
                Raise(nameof(SearchActive));
            }
        }
    }

    public bool SearchActive => _searchFocused || !string.IsNullOrWhiteSpace(_searchText);
    public bool MenuActive => _menuDepth > 0;
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string HoverText { get => _hoverText; private set => SetProperty(ref _hoverText, value); }
    public string FailureText { get => _failureText; private set => SetProperty(ref _failureText, value); }
    public string EmptyText { get => _emptyText; private set => SetProperty(ref _emptyText, value); }
    public int IconsRevision { get => _iconsRevision; private set => SetProperty(ref _iconsRevision, value); }
    public bool AutoRotateEnabled { get => _autoRotateEnabled; set => SetProperty(ref _autoRotateEnabled, value); }
    public double AngularSpeed { get => _angularSpeed; set => SetProperty(ref _angularSpeed, value); }
    public bool ReducedMotion { get => _reducedMotion; set => SetProperty(ref _reducedMotion, value); }

    public string? SelectedGroupId
    {
        get => _selectedGroupId;
        set
        {
            if (SetProperty(ref _selectedGroupId, value))
            {
                Rebuild();
            }
        }
    }

    public ItemKind? KindFilter
    {
        get => _kindFilter;
        set
        {
            if (SetProperty(ref _kindFilter, value))
            {
                Rebuild();
            }
        }
    }

    public ItemRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (value is not null)
            {
                _keptItemId = value.Id;
            }

            if (!SetProperty(ref _selectedRow, value) || _syncingSelection || value is null)
            {
                return;
            }

            RequestAlign(value.Id);
        }
    }

    public void PushMenu() => _menuDepth++;
    public void PopMenu() => _menuDepth = Math.Max(0, _menuDepth - 1);

    public SphereFrame? Advance(SpherePointer pointer)
    {
        var slots = VisibleSlots();
        var input = new SphereFrameInput
        {
            ElapsedSeconds = pointer.ElapsedSeconds,
            WindowInteractive = pointer.WindowInteractive,
            ReducedMotion = ReducedMotion,
            AutoRotateEnabled = AutoRotateEnabled,
            AngularSpeedRadiansPerSecond = AngularSpeed,
            PointerOverScene = pointer.Over,
            PrimaryDown = pointer.Down,
            PrimaryPressed = pointer.Pressed,
            PrimaryReleased = pointer.Released,
            CaptureLost = pointer.CaptureLost,
            PointerX = pointer.X,
            PointerY = pointer.Y,
            ClickCount = pointer.ClickCount,
            DragThresholdDip = pointer.DragThreshold,
            ViewportWidth = pointer.Width,
            ViewportHeight = pointer.Height,
            SearchActive = SearchActive,
            MenuActive = MenuActive,
            AlignToken = _alignToken,
            AlignItemId = _alignItemId,
            Slots = slots,
            FrontRest = slots.Count <= FibonacciSphere.FrontDistributionLimit,
        };
        Frame = _engine.Update(input);
        Raise(nameof(Frame));
        var hovered = Frame.HoveredItemId is null ? null : _all.FirstOrDefault(item => item.Id == Frame.HoveredItemId);
        HoverText = hovered is null ? string.Empty : VisibleName(hovered);
        if (Frame.SelectedItemId is not null)
        {
            var row = Items.FirstOrDefault(item => item.Id == Frame.SelectedItemId);
            if (row is not null)
            {
                SelectRow(row, align: false);
            }
        }

        if (Frame.OpenRequested && Frame.OpenItemId is not null)
        {
            _ = OpenAsync(Frame.OpenItemId);
        }

        return Frame;
    }

    public ImageSource? IconFor(string itemId)
    {
        var item = _all.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            return null;
        }

        return _icons.TryGetValue(item.IconKey, out var image) ? image : null;
    }

    public string PathFor(string itemId) =>
        _all.FirstOrDefault(candidate => candidate.Id == itemId)?.OriginalPath ?? string.Empty;

    public string NameFor(string itemId)
    {
        var item = _all.FirstOrDefault(candidate => candidate.Id == itemId);
        return item is null ? string.Empty : VisibleName(item);
    }

    public ItemKind KindOf(string itemId) =>
        _all.FirstOrDefault(candidate => candidate.Id == itemId)?.Kind ?? ItemKind.File;

    private static string VisibleName(DesktopItem item)
    {
        if (item.Kind is not (ItemKind.Shortcut or ItemKind.InternetShortcut))
        {
            return item.DisplayName;
        }

        var name = item.DisplayName;
        if (!name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        var withoutSuffix = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrEmpty(withoutSuffix) ? name : withoutSuffix;
    }

    public void SelectItem(string itemId) =>
        SelectRow(Items.FirstOrDefault(row => row.Id == itemId), align: false);

    public void OpenSelected(bool align)
    {
        if (SelectedRow is null)
        {
            return;
        }

        if (align)
        {
            RequestAlign(SelectedRow.Id);
        }

        _ = OpenAsync(SelectedRow.Id);
    }

    public void ClearSearch()
    {
        SearchText = string.Empty;
        SearchFocused = false;
    }

    public void ApplyPreferences(bool autoRotate, double speed, bool reducedMotion, bool launchAtStartup, HotkeyGesture hotkey)
    {
        AutoRotateEnabled = autoRotate;
        AngularSpeed = speed;
        ReducedMotion = reducedMotion;
        _session.UpdatePreferences(autoRotate, speed, reducedMotion, true, launchAtStartup, hotkey, _session.Settings.FallbackScanIntervalSeconds);
    }

    private void ApplySnapshot(DesktopSnapshot snapshot)
    {
        _all = snapshot.Items.ToList();
        _roots = snapshot.Roots.ToList();
        var failed = _roots.Count(root => root.LastScanStatus == ScanStatus.Failed);
        AppLog.Write("ScanCompleted items=" + _all.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " failedRoots=" + failed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Rebuild();
        _ = LoadIconsAsync(_dpiScale);
    }

    public void NoteDpi(double dpiScale)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0 || Math.Abs(dpiScale - _dpiScale) < 0.05)
        {
            return;
        }

        _dpiScale = dpiScale;
        _icons.Clear();
        _ = LoadIconsAsync(_dpiScale);
    }

    private void Rebuild()
    {
        IEnumerable<DesktopItem> source = _all;
        if (!string.IsNullOrEmpty(SelectedGroupId))
        {
            var group = _session.Settings.Groups.FirstOrDefault(candidate => candidate.Id == SelectedGroupId);
            var ids = group?.ItemIds.ToHashSet(StringComparer.Ordinal) ?? [];
            source = source.Where(item => ids.Contains(item.Id));
        }

        var ranked = SearchRanker.Rank(source, SearchText, KindFilter);
        if (_keptItemId is not null && _all.All(item => item.Id != _keptItemId))
        {
            _keptItemId = null;
        }

        var selectedId = _keptItemId;
        Items.Clear();
        foreach (var item in ranked)
        {
            var row = new ItemRow
            {
                Id = item.Id,
                Name = VisibleName(item),
                KindLabel = KindText(item.Kind),
                StatusLabel = StatusTextFor(item),
                IconKey = item.IconKey,
                Icon = _icons.TryGetValue(item.IconKey, out var icon) ? icon : null,
            };
            Items.Add(row);
        }

        if (selectedId is not null)
        {
            SelectRow(Items.FirstOrDefault(row => row.Id == selectedId), align: false);
        }

        FailureText = string.Join(" ", _roots.Where(root => root.LastScanStatus == ScanStatus.Failed).Select(root => root.KnownFolderKind == KnownFolderKind.PublicDesktop ? "公共桌面暂时不可用。" : "用户桌面暂时不可用。"));
        if (Items.Count > 0)
        {
            EmptyText = string.Empty;
        }
        else if (!string.IsNullOrEmpty(SelectedGroupId))
        {
            EmptyText = "这个分组里还没有项目。文件仍留在原来的位置。";
        }
        else if (_roots.Any(root => root.LastScanStatus == ScanStatus.Failed) && _all.Count == 0)
        {
            EmptyText = "暂时无法读取桌面。这不是空桌面，请稍后刷新。";
        }
        else if (_all.Count == 0 && _roots.Count == 0)
        {
            EmptyText = "正在读取桌面…";
        }
        else
        {
            EmptyText = "桌面上没有可显示的项目。";
        }

        var groupName = string.IsNullOrEmpty(SelectedGroupId) ? "全部" : _session.Settings.Groups.FirstOrDefault(group => group.Id == SelectedGroupId)?.Name ?? "分组";
        StatusText = IsSynthetic
            ? "当前为合成数据，不是真实桌面。显示 " + Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个样例。"
            : groupName + " · " + Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个项目";
    }

    private async Task LoadIconsAsync(double pointerDpi)
    {
        var previous = Interlocked.Exchange(ref _iconRequests, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
        var token = _iconRequests.Token;
        foreach (var item in _all.ToArray())
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (_icons.ContainsKey(item.IconKey))
            {
                continue;
            }

            try
            {
                var result = await _session.Icons.GetAsync(item, (int)Math.Round(128 * Math.Max(1, pointerDpi)), Math.Max(1, pointerDpi), token).ConfigureAwait(false);
                if (token.IsCancellationRequested || result.BgraPixels is null || result.PixelWidth <= 0 || result.PixelHeight <= 0)
                {
                    continue;
                }

                var dpi = 96 * Math.Max(1, pointerDpi);
                var bitmap = BitmapSource.Create(result.PixelWidth, result.PixelHeight, dpi, dpi, PixelFormats.Bgra32, null, result.BgraPixels, result.PixelWidth * 4);
                bitmap.Freeze();
                await _dispatcher.InvokeAsync(() =>
                {
                    _icons[item.IconKey] = bitmap;
                    foreach (var row in Items.Where(row => row.IconKey == item.IconKey))
                    {
                        row.Icon = bitmap;
                    }

                    IconsRevision++;
                });
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                AppLog.Write("IconBindFailed");
            }
        }
    }

    private IReadOnlyList<LayoutSlot> VisibleSlots()
    {
        var visible = Items.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var slots = _session.Settings.LayoutSlots.Where(slot => visible.Contains(slot.ItemId)).ToArray();
        if (slots.Length == 0 || slots.Length > FibonacciSphere.FrontDistributionLimit)
        {
            return slots;
        }

        var ordered = slots.OrderBy(slot => slot.StableOrder).ThenBy(slot => slot.ItemId, StringComparer.Ordinal).ToArray();
        var points = FibonacciSphere.DistributeFront(ordered.Length);
        var facing = new LayoutSlot[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            facing[index] = ordered[index] with { UnitPosition = points[index] };
        }

        return facing;
    }

    private void RequestAlign(string itemId)
    {
        _alignItemId = itemId;
        _alignToken++;
    }

    private void SelectRow(ItemRow? row, bool align)
    {
        _syncingSelection = !align;
        SelectedRow = row;
        _syncingSelection = false;
    }

    private async Task OpenAsync(string itemId)
    {
        var item = _all.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            return;
        }

        PushMenu();
        try
        {
            var result = await _session.Launcher.OpenAsync(item, _ownerHandle(), CancellationToken.None).ConfigureAwait(true);
            if (result.Status != OperationStatus.Success)
            {
                StatusText = result.UserMessage ?? "无法打开这个项目。";
                MessageBox.Show(result.UserMessage ?? "无法打开这个项目。", "SphereDesk", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            PopMenu();
        }
    }

    private void AddToGroup()
    {
        if (string.IsNullOrEmpty(SelectedGroupId))
        {
            StatusText = "请先在左侧选择一个分组，再加入分组。文件不会被移动。";
            return;
        }

        var itemId = SelectedRow?.Id ?? _keptItemId;
        if (string.IsNullOrEmpty(itemId))
        {
            StatusText = "请先选择一个项目。";
            return;
        }

        var result = _session.AddToGroup(SelectedGroupId, itemId);
        Rebuild();
        StatusText = result.UserMessage ?? StatusText;
    }

    private void RemoveFromGroup()
    {
        if (string.IsNullOrEmpty(SelectedGroupId) || SelectedRow is null)
        {
            StatusText = "请在某个分组中选择要移出的项目。";
            return;
        }

        StatusText = _session.RemoveFromGroup(SelectedGroupId, SelectedRow.Id).UserMessage ?? StatusText;
        Rebuild();
    }

    private void CreateGroup()
    {
        var name = AskText?.Invoke("新建分组", null);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = _session.CreateGroup(name);
        StatusText = result.UserMessage ?? StatusText;
        ReloadGroups();
    }

    private void RenameGroup()
    {
        if (string.IsNullOrEmpty(SelectedGroupId))
        {
            StatusText = "“全部”不能改名。请选择一个分组。";
            return;
        }

        var current = _session.Settings.Groups.FirstOrDefault(group => group.Id == SelectedGroupId)?.Name;
        var name = AskText?.Invoke("重命名分组", current);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        StatusText = _session.RenameGroup(SelectedGroupId, name).UserMessage ?? StatusText;
        ReloadGroups();
    }

    private void DeleteGroup()
    {
        if (string.IsNullOrEmpty(SelectedGroupId))
        {
            StatusText = "“全部”不能删除。";
            return;
        }

        var name = _session.Settings.Groups.FirstOrDefault(group => group.Id == SelectedGroupId)?.Name ?? "分组";
        if (Confirm?.Invoke("删除分组“" + name + "”？分组里的文件不会被删除。") != true)
        {
            return;
        }

        StatusText = _session.DeleteGroup(SelectedGroupId).UserMessage ?? StatusText;
        SelectedGroupId = null;
        ReloadGroups();
    }

    private void ReloadGroups()
    {
        var selected = SelectedGroupId;
        Groups.Clear();
        Groups.Add(new GroupRow { Id = null, Name = "全部" });
        foreach (var group in _session.Settings.Groups.OrderBy(group => group.SortOrder))
        {
            Groups.Add(new GroupRow { Id = group.Id, Name = group.Name });
        }

        if (selected is not null && Groups.All(group => group.Id != selected))
        {
            SelectedGroupId = null;
        }

        Raise(nameof(Groups));
    }

    private static string KindText(ItemKind kind) => kind switch
    {
        ItemKind.Folder => "文件夹",
        ItemKind.Shortcut => "快捷方式",
        ItemKind.InternetShortcut => "网页快捷方式",
        _ => "文件",
    };

    private static string StatusTextFor(DesktopItem item) => item.Availability switch
    {
        ItemAvailability.SourceUnavailable => "源不可用",
        ItemAvailability.Missing => "不存在",
        _ => item.Exists ? "可用" : "不存在",
    };
}

using SphereDesk.Application.Abstractions;
using SphereDesk.Core.Layout;
using SphereDesk.Core.Merge;
using SphereDesk.Core.Models;

namespace SphereDesk.Application.Desktop;

public sealed class DesktopSession : IAsyncDisposable
{
    private readonly IConfigStore _store;
    private readonly IDesktopRoots _rootsLocator;
    private readonly IDesktopSource _source;
    private readonly IDesktopChangeFeed _feed;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly object _renameGate = new();
    private readonly List<ExplicitRename> _pendingRenames = [];
    private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _nextAllowed = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly object _stateGate = new();
    private List<SourceRoot> _roots = [];
    private List<DesktopItem> _items = [];
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _saveDebounce;
    private long _generation;
    private bool _dirty;
    private int _disposed;

    public DesktopSession(
        UserSettings settings,
        IConfigStore store,
        IDesktopRoots rootsLocator,
        IDesktopSource source,
        IDesktopChangeFeed feed,
        IIconProvider icons,
        IShellLauncher launcher,
        bool isSynthetic)
    {
        Settings = settings;
        _store = store;
        _rootsLocator = rootsLocator;
        _source = source;
        _feed = feed;
        Icons = icons;
        Launcher = launcher;
        IsSynthetic = isSynthetic;
        _feed.Changed += OnChanged;
        _feed.Overflowed += OnChanged;
        _feed.Renamed += OnRenamed;
    }

    public UserSettings Settings { get; }
    public IIconProvider Icons { get; }
    public IShellLauncher Launcher { get; }
    public bool IsSynthetic { get; }
    public event EventHandler<DesktopSnapshot>? SnapshotUpdated;
    public event EventHandler? SettingsChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        _roots = _rootsLocator.Resolve().ToList();
        _feed.Watch(_roots);
        await ScanAsync(manual: true, linked.Token).ConfigureAwait(false);
        if (_dirty)
        {
            await ScanAsync(manual: true, linked.Token).ConfigureAwait(false);
        }

        _ = FallbackLoop();
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        _failures.Clear();
        _nextAllowed.Clear();
        _roots = _rootsLocator.Resolve().ToList();
        _feed.Watch(_roots);
        return ScanAsync(manual: true, cancellationToken);
    }

    public OperationResult CreateGroup(string name) => Mutate(() => GroupCommands.Create(Settings, name));

    public OperationResult RenameGroup(string groupId, string name) => Mutate(() => GroupCommands.Rename(Settings, groupId, name));

    public OperationResult DeleteGroup(string groupId) => Mutate(() => GroupCommands.Delete(Settings, groupId));

    public OperationResult AddToGroup(string groupId, string itemId)
    {
        lock (_stateGate)
        {
            if (_items.All(item => item.Id != itemId))
            {
                return OperationResult.Fail("找不到这个项目，请先刷新。");
            }
        }

        return Mutate(() => GroupCommands.AddItem(Settings, groupId, itemId));
    }

    public OperationResult RemoveFromGroup(string groupId, string itemId) =>
        Mutate(() => GroupCommands.RemoveItem(Settings, groupId, itemId));

    public OperationResult Rebalance()
    {
        lock (_stateGate)
        {
            var ids = _items
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.OriginalPath, StringComparer.Ordinal)
                .Select(item => item.Id)
                .ToArray();
            Settings.LayoutSlots = LayoutStability.Rebalance(ids).ToList();
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        ScheduleSave();
        return OperationResult.Ok("已重新均匀排列。文件位置没有改变。");
    }

    public void UpdatePreferences(bool autoRotate, double angularSpeed, bool reducedMotion, bool motionPreferenceUserSet, bool launchAtStartup, HotkeyGesture hotkey, int fallbackScanIntervalSeconds)
    {
        lock (_stateGate)
        {
            Settings.AutoRotateEnabled = autoRotate;
            Settings.AngularSpeed = angularSpeed;
            Settings.ReducedMotion = reducedMotion;
            Settings.MotionPreferenceUserSet = motionPreferenceUserSet;
            Settings.LaunchAtStartup = launchAtStartup;
            Settings.Hotkey = hotkey;
            Settings.FallbackScanIntervalSeconds = fallbackScanIntervalSeconds;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        ScheduleSave();
    }

    public void NoteWindow(WindowBounds bounds, string? monitorId)
    {
        lock (_stateGate)
        {
            Settings.WindowBoundsDip = bounds;
            Settings.MonitorId = monitorId;
        }

        ScheduleSave();
    }

    public async Task SaveNowAsync(CancellationToken cancellationToken)
    {
        CancelSaveDebounce();
        UserSettings copy;
        lock (_stateGate)
        {
            copy = Settings.Clone();
        }

        await _store.SaveAsync(copy, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _feed.Changed -= OnChanged;
        _feed.Overflowed -= OnChanged;
        _feed.Renamed -= OnRenamed;
        _feed.Dispose();
        CancelDebounce();
        CancelSaveDebounce();
        try
        {
            await SaveNowAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        Icons.Dispose();
        _lifetime.Dispose();
        _scanLock.Dispose();
        _saveLock.Dispose();
    }

    private async Task FallbackLoop()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            var seconds = Math.Clamp(Settings.FallbackScanIntervalSeconds, SettingsLimits.MinFallbackScanSeconds, SettingsLimits.MaxFallbackScanSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), _lifetime.Token).ConfigureAwait(false);
                await ScanAsync(manual: false, _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ScanAsync(bool manual, CancellationToken cancellationToken)
    {
        if (_disposed != 0)
        {
            return;
        }

        await _scanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var generation = Interlocked.Increment(ref _generation);
            _dirty = false;
            var requested = BuildRequest(manual);
            List<ExplicitRename> renames;
            lock (_renameGate)
            {
                renames = [.. _pendingRenames];
                _pendingRenames.Clear();
            }

            var snapshot = await _source.ScanAsync(requested, generation, cancellationToken).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _generation) || _disposed != 0)
            {
                return;
            }

            UpdateBackoff(snapshot.Roots);
            DesktopSnapshot published;
            lock (_stateGate)
            {
                var merged = SnapshotMerger.Merge(_items, snapshot, renames);
                _items = merged.Items.ToList();
                var live = _items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                Settings.Groups = MembershipMigration.Apply(Settings.Groups, merged.MigratedIds, live);
                Settings.LayoutSlots = LayoutStability.Assign(Settings.LayoutSlots, LiveIdsInOrder(), merged.MigratedIds).ToList();
                published = new DesktopSnapshot(snapshot.Generation, snapshot.Roots, _items, snapshot.CaseSensitiveByRoot, snapshot.ScannedAt);
            }

            SnapshotUpdated?.Invoke(this, published);
            ScheduleSave();
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private IReadOnlyList<SourceRoot> BuildRequest(bool manual)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new List<SourceRoot>(_roots.Count);
        foreach (var root in _roots)
        {
            if (!manual && _nextAllowed.TryGetValue(root.Id, out var next) && now < next)
            {
                request.Add(root with { LastScanStatus = ScanStatus.Skipped, FailureMessage = null });
            }
            else
            {
                request.Add(root with { LastScanStatus = ScanStatus.NotScanned, FailureMessage = null });
            }
        }

        return request;
    }

    private void UpdateBackoff(IReadOnlyList<SourceRoot> roots)
    {
        foreach (var root in roots)
        {
            var known = _roots.FindIndex(candidate => candidate.Id == root.Id);
            if (known >= 0 && !string.Equals(_roots[known].ResolvedPath, root.ResolvedPath, StringComparison.Ordinal))
            {
                _roots[known] = root;
                _feed.Watch(_roots);
            }
            else if (known >= 0)
            {
                _roots[known] = root;
            }

            if (root.LastScanStatus == ScanStatus.Failed)
            {
                var count = _failures.GetValueOrDefault(root.Id) + 1;
                _failures[root.Id] = count;
                var delay = Math.Min(600, 30 * Math.Pow(2, Math.Min(count, 5)));
                _nextAllowed[root.Id] = DateTimeOffset.UtcNow.AddSeconds(delay);
            }
            else if (root.LastScanStatus == ScanStatus.Succeeded)
            {
                _failures.Remove(root.Id);
                _nextAllowed.Remove(root.Id);
            }
        }
    }

    private IReadOnlyList<string> LiveIdsInOrder()
    {
        var live = _items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var slot in Settings.LayoutSlots.OrderBy(slot => slot.StableOrder))
        {
            if (live.Contains(slot.ItemId))
            {
                ordered.Add(slot.ItemId);
            }
        }

        ordered.AddRange(_items
            .Select(item => item.Id)
            .Where(id => !ordered.Contains(id, StringComparer.Ordinal))
            .OrderBy(id => _items.First(item => item.Id == id).DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(id => id, StringComparer.Ordinal));
        return ordered;
    }

    private OperationResult Mutate(Func<OperationResult> action)
    {
        OperationResult result;
        lock (_stateGate)
        {
            result = action();
        }

        if (result.Status == OperationStatus.Success)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            ScheduleSave();
        }

        return result;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        _dirty = true;
        var previous = Interlocked.Exchange(ref _debounce, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();
        var token = _debounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token).ConfigureAwait(false);
                await ScanAsync(manual: false, _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void OnRenamed(object? sender, ExplicitRename rename)
    {
        lock (_renameGate)
        {
            _pendingRenames.Add(rename);
        }
    }

    private void ScheduleSave()
    {
        if (_disposed != 0)
        {
            return;
        }

        var previous = Interlocked.Exchange(ref _saveDebounce, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();
        var token = _saveDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, token).ConfigureAwait(false);
                await _saveLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    UserSettings copy;
                    lock (_stateGate)
                    {
                        copy = Settings.Clone();
                    }

                    await _store.SaveAsync(copy, token).ConfigureAwait(false);
                }
                finally
                {
                    _saveLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }, token);
    }

    private void CancelDebounce()
    {
        var previous = Interlocked.Exchange(ref _debounce, null);
        previous?.Cancel();
        previous?.Dispose();
    }

    private void CancelSaveDebounce()
    {
        var previous = Interlocked.Exchange(ref _saveDebounce, null);
        previous?.Cancel();
        previous?.Dispose();
    }
}

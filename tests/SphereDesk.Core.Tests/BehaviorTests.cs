using SphereDesk.Application.Configuration;
using SphereDesk.Application.Desktop;
using SphereDesk.Core.Configuration;
using SphereDesk.Core.Engine;
using SphereDesk.Core.Geometry;
using SphereDesk.Core.Identity;
using SphereDesk.Core.Layout;
using SphereDesk.Core.Merge;
using SphereDesk.Core.Models;
using SphereDesk.Core.Search;

namespace SphereDesk.Core.Tests;

public class BehaviorTests
{
    [Fact]
    public void DragDoesNotOpenAndLostCaptureEndsDrag()
    {
        var engine = new SphereEngine();
        var slot = new LayoutSlot("a", Vector3.UnitZ, 0);
        engine.Update(Press(slot, 400, 300, 1));
        var dragging = engine.Update(Press(slot, 480, 300, 1) with { PrimaryPressed = false });
        Assert.Equal(InteractionMode.Dragging, dragging.Mode);
        var released = engine.Update(Press(slot, 480, 300, 1) with { PrimaryDown = false, PrimaryPressed = false, PrimaryReleased = true });
        Assert.False(released.OpenRequested);
        Assert.NotEqual(InteractionMode.Dragging, released.Mode);

        var again = new SphereEngine();
        again.Update(Press(slot, 400, 300, 1));
        again.Update(Press(slot, 480, 300, 1) with { PrimaryPressed = false });
        var lost = again.Update(GeometryTests.BaseInput(slots: [slot]) with { CaptureLost = true, PointerOverScene = true });
        Assert.NotEqual(InteractionMode.Dragging, lost.Mode);
    }

    [Fact]
    public void DoubleClickOpensOnceAndBackItemDoesNot()
    {
        var engine = new SphereEngine();
        var slot = new LayoutSlot("a", Vector3.UnitZ, 0);
        engine.Update(Press(slot, 400, 300, 1));
        var firstRelease = engine.Update(Press(slot, 400, 300, 1) with { PrimaryDown = false, PrimaryPressed = false, PrimaryReleased = true });
        Assert.False(firstRelease.OpenRequested);
        engine.Update(Press(slot, 400, 300, 2));
        var secondRelease = engine.Update(Press(slot, 400, 300, 2) with { PrimaryDown = false, PrimaryPressed = false, PrimaryReleased = true });
        Assert.True(secondRelease.OpenRequested);
        Assert.Equal("a", secondRelease.OpenItemId);
        var extra = engine.Update(Press(slot, 400, 300, 1) with { PrimaryDown = false, PrimaryPressed = false, PrimaryReleased = true });
        Assert.False(extra.OpenRequested);

        var back = new SphereEngine();
        var backSlot = new LayoutSlot("back", new Vector3(0, 0, -1), 0);
        back.Update(Press(backSlot, 400, 300, 1));
        back.Update(Press(backSlot, 400, 300, 1) with { PrimaryDown = false, PrimaryPressed = false, PrimaryReleased = true });
        back.Update(Press(backSlot, 400, 300, 2));
        var backRelease = back.Update(Press(backSlot, 400, 300, 2) with { PrimaryDown = false, PrimaryPressed = false, PrimaryReleased = true });
        Assert.False(backRelease.OpenRequested);
    }

    [Fact]
    public void MenuCloseDoesNotResumeWhileHoveringAndReducedMotionStaysStill()
    {
        var slot = new LayoutSlot("a", Vector3.UnitX, 0);
        var engine = new SphereEngine();
        engine.Update(GeometryTests.BaseInput(slots: [slot]) with { MenuActive = true, PointerOverScene = true, ElapsedSeconds = 0.05, AngularSpeedRadiansPerSecond = 2 });
        var closed = engine.Update(GeometryTests.BaseInput(slots: [slot]) with { PointerOverScene = true, ElapsedSeconds = 0.05, AngularSpeedRadiansPerSecond = 2 });
        Assert.Equal(InteractionMode.HoverPaused, closed.Mode);
        GeometryTests.Near(Vector3.UnitX, closed.Pose.Rotate(Vector3.UnitX), 1e-8);

        var reduced = new SphereEngine();
        var still = reduced.Update(GeometryTests.BaseInput(slots: [slot]) with { ReducedMotion = true, PointerOverScene = false, ElapsedSeconds = 10, AngularSpeedRadiansPerSecond = 2 });
        GeometryTests.Near(Vector3.UnitX, still.Pose.Rotate(Vector3.UnitX), 1e-8);

        var drag = new SphereEngine();
        drag.Update(Press(slot, 400, 300, 1) with { ReducedMotion = true });
        drag.Update(Press(slot, 500, 360, 1) with { PrimaryPressed = false, ReducedMotion = true });
        var released = drag.Update(Press(slot, 500, 360, 1) with { PrimaryDown = false, PrimaryPressed = false, PrimaryReleased = true, ReducedMotion = true });
        var pose = released.Pose;
        for (var step = 0; step < 5; step++)
        {
            var next = drag.Update(GeometryTests.BaseInput(slots: [slot]) with { ReducedMotion = true, PointerOverScene = false, ElapsedSeconds = 0.05, AngularSpeedRadiansPerSecond = 2 });
            GeometryTests.Near(pose.Rotate(Vector3.UnitX), next.Pose.Rotate(Vector3.UnitX), 1e-8);
        }
    }

    [Fact]
    public void SearchRanksNameAndKind()
    {
        var items = new[]
        {
            Item("报告.txt", ItemKind.File),
            Item("年度报告.docx", ItemKind.File),
            Item("照片", ItemKind.Folder),
        };
        var ranked = SearchRanker.Rank(items, "报告", null);
        Assert.Equal("报告.txt", ranked[0].DisplayName);
        Assert.Equal(2, ranked.Count);
        Assert.Empty(SearchRanker.Rank(items, "报告", ItemKind.Folder));
        Assert.Equal(3, SearchRanker.Rank(items, "", null).Count);
    }

    [Fact]
    public void MergeKeepsFailedSourceAndMigratesExplicitRenameOnly()
    {
        var previous = new[] { Item("旧文件.txt", ItemKind.File, "desktop|C:\\Desk\\旧文件.txt", @"C:\Desk\旧文件.txt") };
        var failed = Snapshot(ScanStatus.Failed, []);
        var failedMerge = SnapshotMerger.Merge(previous, failed, []);
        Assert.Equal(ItemAvailability.SourceUnavailable, failedMerge.Items.Single().Availability);
        Assert.Empty(failedMerge.RemovedIds);

        var scanned = new[] { Item("新文件.txt", ItemKind.File, "desktop|C:\\Desk\\新文件.txt", @"C:\Desk\新文件.txt") };
        var withoutRename = SnapshotMerger.Merge(previous, Snapshot(ScanStatus.Succeeded, scanned), []);
        Assert.Contains("desktop|C:\\Desk\\旧文件.txt", withoutRename.RemovedIds);
        Assert.Empty(withoutRename.MigratedIds);

        var withRename = SnapshotMerger.Merge(previous, Snapshot(ScanStatus.Succeeded, scanned), [new ExplicitRename("desktop", @"C:\Desk\旧文件.txt", @"C:\Desk\新文件.txt")]);
        Assert.Equal("desktop|C:\\Desk\\新文件.txt", withRename.MigratedIds["desktop|C:\\Desk\\旧文件.txt"]);
        Assert.Empty(withRename.RemovedIds);
    }

    [Fact]
    public void LayoutKeepsExistingPositionsUntilRebalance()
    {
        var existing = new Vector3(0.2, 0.3, 0.8).Normalized();
        var kept = LayoutStability.Assign([new LayoutSlot("a", existing, 4)], ["a", "b"], new Dictionary<string, string>());
        GeometryTests.Near(existing, kept.Single(slot => slot.ItemId == "a").UnitPosition, 1e-9);
        Assert.Equal(2, kept.Count);
        var migrated = LayoutStability.Assign(kept, ["a2", "b"], new Dictionary<string, string> { ["a"] = "a2" });
        Assert.Contains(migrated, slot => slot.ItemId == "a2");
        Assert.DoesNotContain(migrated, slot => slot.ItemId == "a");
        var balanced = LayoutStability.Rebalance(["b", "a2"]);
        Assert.Equal(0, balanced[0].StableOrder);
        Assert.Equal(2, balanced.Count);
        Assert.All(balanced, slot => Assert.True(slot.UnitPosition.Z > 0));

        var fresh = LayoutStability.Assign([], ["a", "b", "c", "d", "e"], new Dictionary<string, string>());
        Assert.Equal(5, fresh.Count);
        Assert.All(fresh, slot => Assert.True(slot.UnitPosition.Z > 0));
        Assert.True(fresh.Max(slot => slot.UnitPosition.Z) - fresh.Min(slot => slot.UnitPosition.Z) > 0.5);

        var many = Enumerable.Range(0, 25).Select(index => "item-" + index).ToArray();
        var full = LayoutStability.Rebalance(many);
        Assert.Contains(full, slot => slot.UnitPosition.Z < 0);
    }

    [Fact]
    public void FrontRestWobblesInPlaceAndReturnsAfterDrag()
    {
        var engine = new SphereEngine();
        var slot = new LayoutSlot("a", Vector3.UnitZ, 0);
        var input = GeometryTests.BaseInput(slots: [slot]) with
        {
            FrontRest = true,
            PointerOverScene = false,
            ElapsedSeconds = 0.05,
            AngularSpeedRadiansPerSecond = 2,
        };
        SphereFrame? frame = null;
        for (var step = 0; step < 80; step++)
        {
            frame = engine.Update(input);
        }

        var facing = frame!.Pose.Rotate(Vector3.UnitZ).Normalized();
        Assert.True(Vector3.Dot(facing, Vector3.UnitZ) > Math.Cos(2.5 * Math.PI / 180));
        Assert.True(Vector3.Dot(facing, Vector3.UnitZ) < 0.9999);

        var held = engine.Update(input with { PointerOverScene = true });
        var heldAgain = engine.Update(input with { PointerOverScene = true });
        GeometryTests.Near(held.Pose.Rotate(Vector3.UnitZ), heldAgain.Pose.Rotate(Vector3.UnitZ), 1e-8);

        var reduced = new SphereEngine();
        var still = reduced.Update(input with { ReducedMotion = true, ElapsedSeconds = 1 });
        GeometryTests.Near(Vector3.UnitZ, still.Pose.Rotate(Vector3.UnitZ), 1e-8);

        var dragged = new SphereEngine();
        dragged.Update(Press(slot, 400, 300, 1) with { FrontRest = true });
        dragged.Update(Press(slot, 560, 300, 1) with { FrontRest = true, PrimaryPressed = false });
        var released = dragged.Update(Press(slot, 560, 300, 1) with { FrontRest = true, PrimaryDown = false, PrimaryPressed = false, PrimaryReleased = true });
        Assert.NotEqual(InteractionMode.Inertia, released.Mode);
        SphereFrame settled = released;
        var settleInput = GeometryTests.BaseInput(slots: [slot]) with { FrontRest = true, PointerOverScene = true, ElapsedSeconds = 0.05 };
        for (var step = 0; step < 20; step++)
        {
            settled = dragged.Update(settleInput);
        }

        GeometryTests.Near(Vector3.UnitZ, settled.Pose.Rotate(Vector3.UnitZ), 1e-4);

        var searching = new SphereEngine();
        var before = searching.Update(input with { SearchActive = true });
        var after = searching.Update(input with { SearchActive = true });
        Assert.True((before.Pose.Rotate(Vector3.UnitZ) - after.Pose.Rotate(Vector3.UnitZ)).Length > 1e-4);
    }

    [Fact]
    public void PathStoragePreservesCase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SphereDeskCase" + Guid.NewGuid().ToString("N"), "AbC文件");
        var stored = PathIdentity.Store(directory);
        Assert.Contains("AbC文件", stored, StringComparison.Ordinal);
        Assert.True(PathIdentity.Equals(stored, stored.Replace("AbC文件", "abc文件"), false));
        Assert.False(PathIdentity.Equals(stored, stored.Replace("AbC文件", "abc文件"), true));
    }

    [Fact]
    public async Task ConfigRoundtripClampsAndQuarantinesCorruptFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SphereDeskCfg" + Guid.NewGuid().ToString("N"));
        var store = new JsonConfigStore(directory);
        var settings = new UserSettings();
        var created = GroupCommands.Create(settings, "工作");
        Assert.Equal(OperationStatus.Success, created.Status);
        settings.Hotkey = new HotkeyGesture(HotkeyGesture.ModControl | HotkeyGesture.ModAlt, 0x44);
        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Contains(loaded.Settings.Groups, group => group.Name == "工作");
        Assert.Equal(0x44, loaded.Settings.Hotkey.VirtualKey);
        Assert.True(loaded.Settings.LaunchAtStartup);

        var fast = SettingsMigration.Read("""{"schemaVersion":1,"angularSpeed":99}""");
        Assert.False(fast.IsCorrupt);
        Assert.True(fast.HasWarnings);
        Assert.Equal(2, fast.Settings.AngularSpeed);
        Assert.True(fast.Settings.LaunchAtStartup);

        var disabled = SettingsMigration.Read("""{"schemaVersion":1,"launchAtStartup":false}""");
        Assert.False(disabled.Settings.LaunchAtStartup);
        Assert.False(disabled.Settings.Clone().LaunchAtStartup);

        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":1,"groups":[""");
        var corrupt = await store.LoadAsync(CancellationToken.None);
        Assert.True(corrupt.QuarantinedCorruptFile);
        Assert.Empty(corrupt.Settings.Groups);
        Assert.True(Directory.EnumerateFiles(directory, "settings.json.corrupt-*.json").Any());

        await File.WriteAllTextAsync(path, """{"schemaVersion":99}""");
        var newer = await store.LoadAsync(CancellationToken.None);
        Assert.True(newer.QuarantinedCorruptFile);
    }

    [Fact]
    public async Task SyntheticGroupDoesNotRequireRealFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SphereDeskSyn" + Guid.NewGuid().ToString("N"));
        var store = new JsonConfigStore(directory);
        var source = new SyntheticDesktopSource();
        await using var session = new DesktopSession(new UserSettings(), store, source, source, new NullChangeFeed(), new PlaceholderIconProvider(), new RejectingLauncher(), true);
        await session.StartAsync(CancellationToken.None);
        var created = session.CreateGroup("样例分组");
        Assert.Equal(OperationStatus.Success, created.Status);
        await session.SaveNowAsync(CancellationToken.None);
        await session.DisposeAsync();
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Contains(loaded.Settings.Groups, group => group.Name == "样例分组");
    }

    private static SphereFrameInput Press(LayoutSlot slot, double x, double y, int clickCount) =>
        GeometryTests.BaseInput(slots: [slot]) with
        {
            PointerOverScene = true,
            PrimaryDown = true,
            PrimaryPressed = true,
            PointerX = x,
            PointerY = y,
            ClickCount = clickCount,
        };

    private static DesktopItem Item(string name, ItemKind kind, string? id = null, string? path = null) =>
        new(id ?? name, "desktop", path ?? name, name, kind, name, true, ItemAvailability.Available, 1);

    private static DesktopSnapshot Snapshot(ScanStatus status, IReadOnlyList<DesktopItem> items) =>
        new(1, [new SourceRoot("desktop", KnownFolderKind.UserDesktop, @"C:\Desk", status, status == ScanStatus.Failed ? "无法读取这个桌面位置。" : null)], items, new Dictionary<string, bool> { ["desktop"] = false }, DateTimeOffset.UtcNow);
}

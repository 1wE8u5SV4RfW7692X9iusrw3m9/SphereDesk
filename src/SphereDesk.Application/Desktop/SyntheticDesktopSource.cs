using SphereDesk.Application.Abstractions;
using SphereDesk.Core.Merge;
using SphereDesk.Core.Models;

namespace SphereDesk.Application.Desktop;

public sealed class SyntheticDesktopSource : IDesktopRoots, IDesktopSource
{
    public const string RootId = "synthetic";

    public IReadOnlyList<SourceRoot> Resolve() =>
    [
        new SourceRoot(RootId, KnownFolderKind.UserDesktop, null, ScanStatus.Succeeded, null),
    ];

    public Task<DesktopSnapshot> ScanAsync(IReadOnlyList<SourceRoot> roots, long generation, CancellationToken cancellationToken)
    {
        var names = new (string Name, ItemKind Kind)[]
        {
            ("季度报告.txt", ItemKind.File),
            ("照片 😀.png", ItemKind.File),
            ("项目 文件夹", ItemKind.Folder),
            ("参考资料.url", ItemKind.InternetShortcut),
            ("启动器.lnk", ItemKind.Shortcut),
            ("笔记.txt", ItemKind.File),
            ("合同 草案.docx", ItemKind.File),
            ("音乐", ItemKind.Folder),
        };

        var items = names.Select((entry, index) =>
        {
            var path = "synthetic://" + index + "/" + entry.Name;
            var id = RootId + "|" + path;
            return new DesktopItem(id, RootId, path, entry.Name, entry.Kind, id + "#1", true, ItemAvailability.Available, 1);
        }).ToArray();

        var snapshot = new DesktopSnapshot(
            generation,
            Resolve(),
            items,
            new Dictionary<string, bool> { [RootId] = true },
            DateTimeOffset.UtcNow);
        return Task.FromResult(snapshot);
    }
}

public sealed class PlaceholderIconProvider : IIconProvider
{
    public Task<IconResult> GetAsync(DesktopItem item, int requestedPixelSize, double dpiScale, CancellationToken cancellationToken) =>
        Task.FromResult(new IconResult(true, 0, 0, null, "合成数据使用占位图。"));

    public void Dispose()
    {
    }
}

public sealed class RejectingLauncher : IShellLauncher
{
    public Task<OperationResult> OpenAsync(DesktopItem item, nint ownerWindow, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail("当前是合成数据，没有真实文件可打开。"));
}

public sealed class NullChangeFeed : IDesktopChangeFeed
{
#pragma warning disable CS0067
    public event EventHandler? Changed;
    public event EventHandler<ExplicitRename>? Renamed;
    public event EventHandler? Overflowed;
#pragma warning restore CS0067

    public void Watch(IReadOnlyList<SourceRoot> roots)
    {
    }

    public void Dispose()
    {
    }
}

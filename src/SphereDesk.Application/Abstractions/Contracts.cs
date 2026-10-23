using SphereDesk.Core.Merge;
using SphereDesk.Core.Models;

namespace SphereDesk.Application.Abstractions;

public interface IDesktopRoots
{
    IReadOnlyList<SourceRoot> Resolve();
}

public interface IDesktopSource
{
    Task<DesktopSnapshot> ScanAsync(IReadOnlyList<SourceRoot> roots, long generation, CancellationToken cancellationToken);
}

public interface IDesktopChangeFeed : IDisposable
{
    event EventHandler? Changed;
    event EventHandler<ExplicitRename>? Renamed;
    event EventHandler? Overflowed;
    void Watch(IReadOnlyList<SourceRoot> roots);
}

public sealed record IconResult(bool IsPlaceholder, int PixelWidth, int PixelHeight, byte[]? BgraPixels, string? Reason);

public interface IIconProvider : IDisposable
{
    Task<IconResult> GetAsync(DesktopItem item, int requestedPixelSize, double dpiScale, CancellationToken cancellationToken);
}

public interface IShellLauncher
{
    Task<OperationResult> OpenAsync(DesktopItem item, nint ownerWindow, CancellationToken cancellationToken);
}

public sealed record ConfigLoadResult(
    UserSettings Settings,
    bool RecoveredFromBackup,
    bool QuarantinedCorruptFile,
    string? UserMessage);

public interface IConfigStore
{
    Task<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken);
}

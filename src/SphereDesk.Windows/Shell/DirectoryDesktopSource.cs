using SphereDesk.Application.Abstractions;
using SphereDesk.Core.Identity;
using SphereDesk.Core.Models;

namespace SphereDesk.Windows.Shell;

public static class DesktopEntryInspector
{
    public static bool ShouldSkip(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Hidden) != 0 && (attributes & FileAttributes.System) != 0)
            {
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    public static DesktopItem CreateItem(string sourceRootId, string path)
    {
        var stored = PathIdentity.Store(path);
        var directory = false;
        long metadata = 0;
        try
        {
            var attributes = File.GetAttributes(stored);
            directory = (attributes & FileAttributes.Directory) != 0;
            metadata = File.GetLastWriteTimeUtc(stored).Ticks;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var extension = Path.GetExtension(stored);
        var kind = directory
            ? ItemKind.Folder
            : extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                ? ItemKind.Shortcut
                : extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                    ? ItemKind.InternetShortcut
                    : ItemKind.File;
        var id = PathIdentity.ItemId(sourceRootId, stored);
        return new DesktopItem(
            id,
            sourceRootId,
            stored,
            Path.GetFileName(stored),
            kind,
            id + "#" + metadata.ToString(System.Globalization.CultureInfo.InvariantCulture),
            true,
            ItemAvailability.Available,
            metadata);
    }
}

public sealed class DirectoryDesktopSource : IDesktopSource
{
    public Task<DesktopSnapshot> ScanAsync(IReadOnlyList<SourceRoot> roots, long generation, CancellationToken cancellationToken)
    {
        var scannedRoots = new List<SourceRoot>(roots.Count);
        var items = new List<DesktopItem>();
        var sensitivity = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (root.LastScanStatus == ScanStatus.Skipped)
            {
                scannedRoots.Add(root);
                sensitivity[root.Id] = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(root.ResolvedPath))
            {
                scannedRoots.Add(root with { LastScanStatus = ScanStatus.Failed, FailureMessage = "无法读取这个桌面位置。" });
                sensitivity[root.Id] = false;
                continue;
            }

            try
            {
                var caseSensitive = DirectoryCaseSensitivity.IsCaseSensitive(root.ResolvedPath);
                sensitivity[root.Id] = caseSensitive;
                foreach (var entry in Directory.EnumerateFileSystemEntries(root.ResolvedPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DesktopEntryInspector.ShouldSkip(entry))
                    {
                        continue;
                    }

                    items.Add(DesktopEntryInspector.CreateItem(root.Id, entry));
                }

                scannedRoots.Add(root with { LastScanStatus = ScanStatus.Succeeded, FailureMessage = null });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                items.RemoveAll(item => item.SourceRootId == root.Id);
                scannedRoots.Add(root with { LastScanStatus = ScanStatus.Failed, FailureMessage = "无法读取这个桌面位置。" });
                sensitivity[root.Id] = false;
            }
        }

        return Task.FromResult(new DesktopSnapshot(generation, scannedRoots, items, sensitivity, DateTimeOffset.UtcNow));
    }
}

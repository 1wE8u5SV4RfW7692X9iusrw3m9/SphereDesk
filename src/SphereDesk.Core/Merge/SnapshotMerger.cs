using SphereDesk.Core.Identity;
using SphereDesk.Core.Models;

namespace SphereDesk.Core.Merge;

public sealed record ExplicitRename(string SourceRootId, string OldPath, string NewPath);

public sealed record MergeResult(
    IReadOnlyList<DesktopItem> Items,
    IReadOnlyDictionary<string, string> MigratedIds,
    IReadOnlyList<string> RemovedIds);

public static class SnapshotMerger
{
    public static MergeResult Merge(
        IReadOnlyList<DesktopItem> previous,
        DesktopSnapshot scan,
        IReadOnlyList<ExplicitRename> renames)
    {
        var migrated = new Dictionary<string, string>(StringComparer.Ordinal);
        var removed = new List<string>();
        var items = new List<DesktopItem>();
        var seenRoots = scan.Roots.Select(root => root.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var root in scan.Roots)
        {
            var caseSensitive = scan.CaseSensitiveByRoot.TryGetValue(root.Id, out var sensitive) && sensitive;
            var comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            var previousForRoot = previous.Where(item => item.SourceRootId == root.Id).ToList();
            if (root.LastScanStatus is ScanStatus.Failed)
            {
                items.AddRange(previousForRoot.Select(item => item with { Availability = ItemAvailability.SourceUnavailable }));
                continue;
            }

            if (root.LastScanStatus is ScanStatus.Skipped or ScanStatus.NotScanned)
            {
                items.AddRange(previousForRoot);
                continue;
            }

            var previousByPath = new Dictionary<string, DesktopItem>(comparer);
            foreach (var item in previousForRoot)
            {
                previousByPath[PathIdentity.Store(item.OriginalPath)] = item;
            }

            var scanned = scan.Items.Where(item => item.SourceRootId == root.Id).ToList();
            foreach (var rename in renames.Where(rename => rename.SourceRootId == root.Id))
            {
                var oldKey = PathIdentity.Store(rename.OldPath);
                var newKey = PathIdentity.Store(rename.NewPath);
                if (!previousByPath.TryGetValue(oldKey, out var oldItem))
                {
                    continue;
                }

                var newItem = scanned.FirstOrDefault(item => comparer.Equals(PathIdentity.Store(item.OriginalPath), newKey));
                if (newItem is null || oldItem.Id == newItem.Id)
                {
                    continue;
                }

                migrated[oldItem.Id] = newItem.Id;
                previousByPath.Remove(oldKey);
            }

            foreach (var item in scanned)
            {
                items.Add(item);
                previousByPath.Remove(PathIdentity.Store(item.OriginalPath));
            }

            foreach (var leftover in previousByPath.Values)
            {
                if (!migrated.ContainsKey(leftover.Id))
                {
                    removed.Add(leftover.Id);
                }
            }
        }

        foreach (var item in previous.Where(item => !seenRoots.Contains(item.SourceRootId)))
        {
            items.Add(item);
        }

        return new MergeResult(items, migrated, removed);
    }
}

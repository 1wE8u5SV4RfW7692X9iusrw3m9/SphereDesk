using SphereDesk.Core.Geometry;
using SphereDesk.Core.Models;

namespace SphereDesk.Core.Layout;

public static class LayoutStability
{
    public static IReadOnlyList<LayoutSlot> Assign(
        IReadOnlyList<LayoutSlot> existing,
        IReadOnlyList<string> liveItemIds,
        IReadOnlyDictionary<string, string> migratedIds)
    {
        var live = liveItemIds.ToHashSet(StringComparer.Ordinal);
        var kept = new List<LayoutSlot>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in existing.OrderBy(slot => slot.StableOrder))
        {
            var itemId = migratedIds.TryGetValue(slot.ItemId, out var migrated) ? migrated : slot.ItemId;
            if (!live.Contains(itemId) || !used.Add(itemId))
            {
                continue;
            }

            kept.Add(slot with { ItemId = itemId, UnitPosition = slot.UnitPosition.Normalized() });
        }

        var nextOrder = kept.Count == 0 ? 0 : kept.Max(slot => slot.StableOrder) + 1;
        var newcomers = new List<string>();
        foreach (var itemId in liveItemIds)
        {
            if (!used.Add(itemId))
            {
                continue;
            }

            newcomers.Add(itemId);
        }

        if (newcomers.Count == 0)
        {
            return kept;
        }

        var points = kept.Count == 0
            ? FibonacciSphere.DistributeForCount(newcomers.Count)
            : FibonacciSphere.Distribute(newcomers.Count);
        for (var index = 0; index < newcomers.Count; index++)
        {
            kept.Add(new LayoutSlot(newcomers[index], points[index], nextOrder));
            nextOrder++;
        }

        return kept;
    }

    public static IReadOnlyList<LayoutSlot> Rebalance(IReadOnlyList<string> itemIds)
    {
        var distinct = itemIds.Distinct(StringComparer.Ordinal).ToArray();
        var points = FibonacciSphere.DistributeForCount(distinct.Length);
        return distinct.Select((itemId, index) => new LayoutSlot(itemId, points[index], index)).ToArray();
    }
}

public static class MembershipMigration
{
    public static List<Group> Apply(
        IReadOnlyList<Group> groups,
        IReadOnlyDictionary<string, string> migratedIds,
        IReadOnlySet<string> liveIds)
    {
        var result = new List<Group>();
        foreach (var group in groups.OrderBy(group => group.SortOrder))
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var itemId in group.ItemIds)
            {
                var mapped = migratedIds.TryGetValue(itemId, out var migrated) ? migrated : itemId;
                if (liveIds.Contains(mapped) && seen.Add(mapped))
                {
                    ids.Add(mapped);
                }
            }

            result.Add(new Group
            {
                Id = group.Id,
                Name = group.Name,
                SortOrder = group.SortOrder,
                ItemIds = ids,
            });
        }

        return result;
    }
}

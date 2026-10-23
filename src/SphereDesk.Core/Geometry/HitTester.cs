using SphereDesk.Core.Models;

namespace SphereDesk.Core.Geometry;

public static class HitTester
{
    public static string? Find(IReadOnlyList<ProjectedItem> items, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return null;
        }

        string? found = null;
        var bestOrder = int.MinValue;
        foreach (var item in items)
        {
            if (item.IsBackFacing || item.DrawOrder < bestOrder)
            {
                continue;
            }

            if (item.HitBoundsDip.Contains(x, y))
            {
                bestOrder = item.DrawOrder;
                found = item.ItemId;
            }
        }

        return found;
    }
}

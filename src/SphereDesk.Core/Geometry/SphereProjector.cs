using SphereDesk.Core.Models;

namespace SphereDesk.Core.Geometry;

public static class SphereProjector
{
    public const double CameraDistance = 3.0;
    public const double MinimumDepth = 0.75;
    private const double MaximumNormalizedRadius = 0.35355339059327376;

    public static IReadOnlyList<ProjectedItem> Project(
        Quaternion pose,
        IReadOnlyList<LayoutSlot> slots,
        double viewportWidth,
        double viewportHeight,
        double iconRadiusDip,
        double labelMarginDip,
        string? hoveredItemId,
        string? selectedItemId)
    {
        var width = double.IsFinite(viewportWidth) ? Math.Max(1, viewportWidth) : 1;
        var height = double.IsFinite(viewportHeight) ? Math.Max(1, viewportHeight) : 1;
        var iconRadius = double.IsFinite(iconRadiusDip) ? Math.Max(1, iconRadiusDip) : 1;
        var labelMargin = double.IsFinite(labelMarginDip) ? Math.Max(0, labelMarginDip) : 0;
        var halfMin = Math.Min(width, height) * 0.5;
        var padding = iconRadius + labelMargin;
        var available = halfMin <= padding + 8 ? Math.Max(1, halfMin * 0.5) : halfMin - padding;
        var focal = available / MaximumNormalizedRadius;
        var rotation = pose.IsFinite ? pose.Normalized() : Quaternion.Identity;

        var raw = new List<(LayoutSlot Slot, Vector3 World, double Depth)>(slots.Count);
        foreach (var slot in slots.OrderBy(slot => slot.StableOrder).ThenBy(slot => slot.ItemId, StringComparer.Ordinal))
        {
            var world = rotation.Rotate(slot.UnitPosition.Normalized());
            var depth = CameraDistance - world.Z;
            if (!double.IsFinite(depth) || depth < MinimumDepth)
            {
                depth = MinimumDepth;
            }

            raw.Add((slot, world, depth));
        }

        raw.Sort(static (left, right) =>
        {
            var depthCompare = left.World.Z.CompareTo(right.World.Z);
            return depthCompare != 0 ? depthCompare : left.Slot.StableOrder.CompareTo(right.Slot.StableOrder);
        });

        var frontDepth = Math.Max(MinimumDepth, CameraDistance - 1);
        var frontScale = focal / frontDepth;
        var draft = new ProjectedItem[raw.Count];
        for (var index = 0; index < raw.Count; index++)
        {
            var (slot, world, depth) = raw[index];
            var scale = focal / depth;
            var centerX = (width * 0.5) + (world.X * scale);
            var centerY = (height * 0.5) + (world.Y * scale);
            if (!double.IsFinite(centerX))
            {
                centerX = width * 0.5;
            }

            if (!double.IsFinite(centerY))
            {
                centerY = height * 0.5;
            }

            var relative = frontScale <= 0 ? 1 : scale / frontScale;
            var hitRadius = Math.Clamp(iconRadius * relative, 6, iconRadius);
            var backFacing = world.Z < 0;
            var opacity = backFacing ? 0.22 : 0.4 + (0.6 * ((world.Z + 1) * 0.5));
            draft[index] = new ProjectedItem(
                slot.ItemId,
                new DipPoint(centerX, centerY),
                depth,
                relative,
                opacity,
                new DipRect(centerX - hitRadius, centerY - hitRadius, hitRadius * 2, hitRadius * 2),
                index,
                backFacing,
                false);
        }

        return ApplyLabels(draft, hoveredItemId, selectedItemId);
    }

    public static IReadOnlyList<ProjectedItem> ApplyLabels(
        IReadOnlyList<ProjectedItem> items,
        string? hoveredItemId,
        string? selectedItemId)
    {
        const double labelWidth = 96;
        const double labelHeight = 18;
        var occupied = new List<DipRect>();
        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items
            .OrderByDescending(item => LabelPriority(item, hoveredItemId, selectedItemId))
            .ThenByDescending(item => item.DrawOrder))
        {
            var rect = new DipRect(item.CenterDip.X - (labelWidth / 2), item.HitBoundsDip.Bottom + 2, labelWidth, labelHeight);
            var forced = item.ItemId == hoveredItemId || item.ItemId == selectedItemId;
            if (forced || !occupied.Any(existing => existing.Intersects(rect)))
            {
                visible.Add(item.ItemId);
                occupied.Add(rect);
            }
        }

        return items.Select(item => item with { ShowLabel = visible.Contains(item.ItemId) }).ToArray();
    }

    private static int LabelPriority(ProjectedItem item, string? hoveredItemId, string? selectedItemId)
    {
        if (item.ItemId == hoveredItemId)
        {
            return 3;
        }

        if (item.ItemId == selectedItemId)
        {
            return 2;
        }

        return item.IsBackFacing ? 0 : 1;
    }
}

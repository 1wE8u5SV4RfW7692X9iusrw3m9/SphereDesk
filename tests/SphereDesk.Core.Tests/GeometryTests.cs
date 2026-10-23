using SphereDesk.Core.Engine;
using SphereDesk.Core.Geometry;
using SphereDesk.Core.Models;

namespace SphereDesk.Core.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(30)]
    [InlineData(100)]
    [InlineData(500)]
    public void DistributionKeepsUnitLength(int count)
    {
        var points = FibonacciSphere.Distribute(count);
        Assert.Equal(count, points.Length);
        if (count == 1)
        {
            Near(Vector3.UnitZ, points[0]);
        }

        foreach (var point in points)
        {
            Assert.True(point.IsFinite);
            Near(1, point.Length, 1e-9);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(24)]
    public void FrontDistributionStaysOnFacingHemisphere(int count)
    {
        var points = FibonacciSphere.DistributeFront(count);
        Assert.Equal(count, points.Length);
        if (count == 1)
        {
            Near(Vector3.UnitZ, points[0]);
        }

        foreach (var point in points)
        {
            Assert.True(point.Z > 0);
            Near(1, point.Length, 1e-9);
        }

        if (count > 1)
        {
            Assert.True(points.Max(point => point.Z) > 0.9);
            Assert.True(points.Min(point => point.Z) < 0.2);
            var nearest = double.PositiveInfinity;
            for (var left = 0; left < points.Length; left++)
            {
                for (var right = left + 1; right < points.Length; right++)
                {
                    nearest = Math.Min(nearest, (points[left] - points[right]).Length);
                }
            }

            Assert.True(nearest > 0.2);
        }
    }

    [Fact]
    public void QuarterTurnsReturnToStartAfterFullCycle()
    {
        var step = Quaternion.FromAxisAngle(Vector3.UnitY, Math.PI / 2);
        var pose = Quaternion.Identity;
        var original = new Vector3(0.2, -0.4, 0.6).Normalized();
        var quarter = step.Rotate(Vector3.UnitZ);
        Near(Vector3.UnitX, quarter, 1e-9);
        for (var turn = 0; turn < 4; turn++)
        {
            pose = (step * pose).Normalized();
        }

        Near(original, pose.Rotate(original), 1e-8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(30)]
    [InlineData(100)]
    [InlineData(500)]
    public void ProjectionStaysFiniteAndOrdered(int count)
    {
        var points = FibonacciSphere.Distribute(count);
        var slots = points.Select((point, index) => new LayoutSlot("item-" + index, point, index)).ToArray();
        var projected = SphereProjector.Project(Quaternion.Identity, slots, 800, 600, 28, 18, null, null);
        Assert.Equal(count, projected.Count);
        double previousZ = double.NegativeInfinity;
        var seen = new HashSet<int>();
        foreach (var item in projected)
        {
            Assert.True(item.CenterDip.IsFinite);
            Assert.True(item.HitBoundsDip.IsFinite);
            Assert.True(double.IsFinite(item.Depth) && item.Depth >= SphereProjector.MinimumDepth);
            Assert.True(double.IsFinite(item.Scale) && item.Scale > 0);
            Assert.True(double.IsFinite(item.Opacity));
            Assert.True(seen.Add(item.DrawOrder));
            var slot = slots.First(candidate => candidate.ItemId == item.ItemId);
            var z = Quaternion.Identity.Rotate(slot.UnitPosition).Z;
            Assert.True(z >= previousZ - 1e-9);
            previousZ = z;
        }

        if (count > 0)
        {
            var nearest = projected.MaxBy(item => item.DrawOrder)!;
            var farthest = projected.MinBy(item => item.DrawOrder)!;
            Assert.True(nearest.Depth <= farthest.Depth + 1e-9);
        }
    }

    [Fact]
    public void FrontItemWinsOverlappingHitAndBackItemDoesNotOpen()
    {
        var slots = new[]
        {
            new LayoutSlot("back", new Vector3(0, 0, -1), 0),
            new LayoutSlot("front", Vector3.UnitZ, 1),
        };
        var projected = SphereProjector.Project(Quaternion.Identity, slots, 800, 600, 28, 18, null, null);
        Assert.True(projected.Single(item => item.ItemId == "back").IsBackFacing);
        Assert.False(projected.Single(item => item.ItemId == "front").IsBackFacing);
        Assert.Equal("front", HitTester.Find(projected, 400, 300));
        var onlyBack = SphereProjector.Project(Quaternion.Identity, [slots[0]], 800, 600, 28, 18, null, null);
        Assert.Null(HitTester.Find(onlyBack, 400, 300));
    }

    [Fact]
    public void TinyViewportAndLabelsStayFinite()
    {
        var slots = FibonacciSphere.Distribute(40).Select((point, index) => new LayoutSlot(index.ToString(), point, index)).ToArray();
        var projected = SphereProjector.Project(Quaternion.Identity, slots, 1, 1, 28, 18, "0", null);
        Assert.All(projected, item => Assert.True(item.CenterDip.IsFinite && double.IsFinite(item.Depth)));
        Assert.Contains(projected, item => item.ItemId == "0" && item.ShowLabel);
        var twins = SphereProjector.Project(Quaternion.Identity, [new LayoutSlot("a", Vector3.UnitZ, 0), new LayoutSlot("b", Vector3.UnitZ, 1)], 800, 600, 28, 18, null, null);
        Assert.Equal(1, twins.Count(item => item.ShowLabel));
    }

    [Fact]
    public void AntipodalAlignmentFacesViewer()
    {
        var engine = new SphereEngine();
        var slot = new LayoutSlot("back", new Vector3(0, 0, -1), 0);
        var frame = engine.Update(BaseInput(slots: [slot]) with
        {
            ReducedMotion = true,
            AlignToken = 1,
            AlignItemId = "back",
        });
        var facing = frame.Pose.Rotate(slot.UnitPosition);
        Near(Vector3.UnitZ, facing.Normalized(), 1e-6);
        Assert.False(frame.Items.Single().IsBackFacing);
    }

    [Fact]
    public void EngineFullTurnAndClampedTimeStepStayFinite()
    {
        var engine = new SphereEngine();
        var slot = new LayoutSlot("a", Vector3.UnitX, 0);
        var input = BaseInput(slots: [slot]) with
        {
            ElapsedSeconds = SphereEngine.MaxStepSeconds,
            AngularSpeedRadiansPerSecond = Math.PI / 2,
            PointerOverScene = false,
        };
        SphereFrame? frame = null;
        for (var step = 0; step < 80; step++)
        {
            frame = engine.Update(input);
        }

        Near(Vector3.UnitX, frame!.Pose.Rotate(Vector3.UnitX), 1e-4);
        var jumped = new SphereEngine().Update(input with { ElapsedSeconds = 100 });
        var rotated = jumped.Pose.Rotate(Vector3.UnitX);
        var expected = Math.Cos(SphereEngine.MaxStepSeconds * Math.PI / 2);
        Near(expected, rotated.X, 1e-6);
        Near(1, rotated.Length, 1e-6);

        var extreme = new SphereEngine().Update(input with
        {
            ElapsedSeconds = 1e12,
            AngularSpeedRadiansPerSecond = 1e12,
        });
        Assert.True(extreme.Pose.IsFinite);
        Assert.True(extreme.Items.Single().CenterDip.IsFinite);
    }

    [Fact]
    public void WindowClampMovesOffscreenWindowIntoWorkArea()
    {
        var work = new DipRect(0, 0, 800, 600);
        var clamped = WorkAreaClamp.ClampInto(new DipRect(-1000, -1000, 300, 200), work);
        Assert.True(clamped.X >= work.X && clamped.Y >= work.Y);
        Assert.True(clamped.Right <= work.Right + 1e-6 && clamped.Bottom <= work.Bottom + 1e-6);
        Assert.False(WorkAreaClamp.IsSufficientlyVisible(new DipRect(-400, -400, 100, 100), work));
    }

    internal static SphereFrameInput BaseInput(LayoutSlot[]? slots = null) => new()
    {
        WindowInteractive = true,
        AutoRotateEnabled = true,
        ViewportWidth = 800,
        ViewportHeight = 600,
        DragThresholdDip = 4,
        Slots = slots ?? [],
        IconRadiusDip = 28,
        LabelMarginDip = 18,
    };

    internal static void Near(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, "expected " + expected + " but was " + actual);

    internal static void Near(Vector3 expected, Vector3 actual, double tolerance = 1e-6)
    {
        Near(expected.X, actual.X, tolerance);
        Near(expected.Y, actual.Y, tolerance);
        Near(expected.Z, actual.Z, tolerance);
    }
}

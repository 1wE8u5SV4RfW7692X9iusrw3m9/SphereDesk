namespace SphereDesk.Core.Geometry;

public static class FibonacciSphere
{
    public const int FrontDistributionLimit = 24;
    public static readonly double GoldenAngle = Math.PI * (3 - Math.Sqrt(5));

    public static Vector3[] DistributeForCount(int count) =>
        count <= FrontDistributionLimit ? DistributeFront(count) : Distribute(count);

    public static Vector3[] DistributeFront(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return [];
        }

        if (count == 1)
        {
            return [Vector3.UnitZ];
        }

        var points = new Vector3[count];
        for (var index = 0; index < count; index++)
        {
            var z = 1.0 - ((index + 0.5) / count);
            var radial = Math.Sqrt(Math.Max(0, 1 - (z * z)));
            var theta = GoldenAngle * index;
            points[index] = new Vector3(Math.Cos(theta) * radial, Math.Sin(theta) * radial, z);
        }

        return points;
    }

    public static Vector3[] Distribute(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return [];
        }

        if (count == 1)
        {
            return [Vector3.UnitZ];
        }

        var points = new Vector3[count];
        for (var index = 0; index < count; index++)
        {
            var y = ((2.0 * (index + 0.5)) / count) - 1.0;
            var radial = Math.Sqrt(Math.Max(0, 1 - (y * y)));
            var theta = GoldenAngle * index;
            points[index] = new Vector3(Math.Cos(theta) * radial, y, Math.Sin(theta) * radial);
        }

        return points;
    }
}

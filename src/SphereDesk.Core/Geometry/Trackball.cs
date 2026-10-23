namespace SphereDesk.Core.Geometry;

public static class Trackball
{
    public static Vector3 Map(double x, double y, double width, double height)
    {
        var safeWidth = double.IsFinite(width) ? Math.Max(1, width) : 1;
        var safeHeight = double.IsFinite(height) ? Math.Max(1, height) : 1;
        var radius = Math.Max(1, Math.Min(safeWidth, safeHeight) * 0.5);
        var nx = ((double.IsFinite(x) ? x : safeWidth * 0.5) - (safeWidth * 0.5)) / radius;
        var ny = ((double.IsFinite(y) ? y : safeHeight * 0.5) - (safeHeight * 0.5)) / radius;
        var distance = (nx * nx) + (ny * ny);
        double nz;
        if (distance <= 1)
        {
            nz = Math.Sqrt(1 - distance);
        }
        else
        {
            var scale = 1 / Math.Sqrt(distance);
            nx *= scale;
            ny *= scale;
            nz = 0;
        }

        return new Vector3(nx, ny, nz);
    }
}

namespace SphereDesk.Core.Geometry;

/// <summary>
/// 屏幕坐标：X 向右，Y 向下，Z 朝向观察者。
/// </summary>
public readonly record struct Vector3(double X, double Y, double Z)
{
    public static Vector3 Zero => new(0, 0, 0);
    public static Vector3 UnitX => new(1, 0, 0);
    public static Vector3 UnitY => new(0, 1, 0);
    public static Vector3 UnitZ => new(0, 0, 1);

    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);
    public double Length => Math.Sqrt(LengthSquared);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    public Vector3 Normalized()
    {
        var length = Length;
        if (length < 1e-12 || !double.IsFinite(length))
        {
            return UnitZ;
        }

        return new Vector3(X / length, Y / length, Z / length);
    }

    public static double Dot(Vector3 left, Vector3 right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    public static Vector3 Cross(Vector3 left, Vector3 right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    public static Vector3 operator +(Vector3 left, Vector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Vector3 operator -(Vector3 left, Vector3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Vector3 operator -(Vector3 value) => new(-value.X, -value.Y, -value.Z);

    public static Vector3 operator *(Vector3 value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    public static Vector3 operator *(double scale, Vector3 value) => value * scale;
}

public readonly record struct Quaternion(double W, double X, double Y, double Z)
{
    public static Quaternion Identity { get; } = new(1, 0, 0, 0);

    public double LengthSquared => (W * W) + (X * X) + (Y * Y) + (Z * Z);
    public double Length => Math.Sqrt(LengthSquared);
    public bool IsFinite => double.IsFinite(W) && double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    public Quaternion Normalized()
    {
        var length = Length;
        if (length < 1e-12 || !double.IsFinite(length))
        {
            return Identity;
        }

        return new Quaternion(W / length, X / length, Y / length, Z / length);
    }

    public Quaternion Conjugate() => new(W, -X, -Y, -Z);

    public static Quaternion operator *(Quaternion left, Quaternion right) => new(
        (left.W * right.W) - (left.X * right.X) - (left.Y * right.Y) - (left.Z * right.Z),
        (left.W * right.X) + (left.X * right.W) + (left.Y * right.Z) - (left.Z * right.Y),
        (left.W * right.Y) - (left.X * right.Z) + (left.Y * right.W) + (left.Z * right.X),
        (left.W * right.Z) + (left.X * right.Y) - (left.Y * right.X) + (left.Z * right.W));

    public Vector3 Rotate(Vector3 value)
    {
        var q = Normalized();
        var u = new Vector3(q.X, q.Y, q.Z);
        var rotated = value + (2 * Vector3.Cross(u, Vector3.Cross(u, value) + (q.W * value)));
        return rotated;
    }

    public static Quaternion FromAxisAngle(Vector3 axis, double radians)
    {
        if (!double.IsFinite(radians))
        {
            return Identity;
        }

        var normal = axis.Normalized();
        var half = radians * 0.5;
        var sine = Math.Sin(half);
        return new Quaternion(Math.Cos(half), normal.X * sine, normal.Y * sine, normal.Z * sine).Normalized();
    }

    public static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        var start = from.Normalized();
        var end = to.Normalized();
        var dot = Math.Clamp(Vector3.Dot(start, end), -1, 1);
        if (dot > 1 - 1e-8)
        {
            return Identity;
        }

        if (dot < -1 + 1e-8)
        {
            var axis = Vector3.Cross(Vector3.UnitX, start);
            if (axis.LengthSquared < 1e-8)
            {
                axis = Vector3.Cross(Vector3.UnitY, start);
            }

            return FromAxisAngle(axis, Math.PI);
        }

        var cross = Vector3.Cross(start, end);
        return new Quaternion(1 + dot, cross.X, cross.Y, cross.Z).Normalized();
    }

    public static Quaternion Slerp(Quaternion start, Quaternion end, double amount)
    {
        amount = double.IsFinite(amount) ? Math.Clamp(amount, 0, 1) : 0;
        start = start.Normalized();
        end = end.Normalized();
        var dot = (start.W * end.W) + (start.X * end.X) + (start.Y * end.Y) + (start.Z * end.Z);
        if (dot < 0)
        {
            end = new Quaternion(-end.W, -end.X, -end.Y, -end.Z);
            dot = -dot;
        }

        if (dot > 0.9995)
        {
            return new Quaternion(
                start.W + (amount * (end.W - start.W)),
                start.X + (amount * (end.X - start.X)),
                start.Y + (amount * (end.Y - start.Y)),
                start.Z + (amount * (end.Z - start.Z))).Normalized();
        }

        var theta = Math.Acos(Math.Clamp(dot, -1, 1));
        var sin = Math.Sin(theta);
        var weightStart = Math.Sin((1 - amount) * theta) / sin;
        var weightEnd = Math.Sin(amount * theta) / sin;
        return new Quaternion(
            (start.W * weightStart) + (end.W * weightEnd),
            (start.X * weightStart) + (end.X * weightEnd),
            (start.Y * weightStart) + (end.Y * weightEnd),
            (start.Z * weightStart) + (end.Z * weightEnd)).Normalized();
    }

    public bool TryGetAxisAngle(out Vector3 axis, out double radians)
    {
        var q = Normalized();
        radians = 2 * Math.Acos(Math.Clamp(q.W, -1, 1));
        var scale = Math.Sqrt(Math.Max(0, 1 - (q.W * q.W)));
        if (scale < 1e-8 || radians < 1e-8)
        {
            axis = Vector3.UnitY;
            radians = 0;
            return false;
        }

        axis = new Vector3(q.X / scale, q.Y / scale, q.Z / scale);
        return true;
    }
}

public readonly record struct DipPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public readonly record struct DipRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height);

    public bool Contains(double x, double y) =>
        x >= X && y >= Y && x < Right && y < Bottom;

    public bool Intersects(DipRect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    public static DipRect FromSize(double x, double y, double width, double height) => new(x, y, width, height);
}

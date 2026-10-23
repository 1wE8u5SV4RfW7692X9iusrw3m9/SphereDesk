namespace SphereDesk.Core.Geometry;

public static class WorkAreaClamp
{
    public static DipRect ClampInto(DipRect window, DipRect workArea)
    {
        var width = Math.Min(Math.Max(1, window.Width), Math.Max(1, workArea.Width));
        var height = Math.Min(Math.Max(1, window.Height), Math.Max(1, workArea.Height));
        var x = window.X;
        var y = window.Y;
        if (x + width > workArea.Right)
        {
            x = workArea.Right - width;
        }

        if (y + height > workArea.Bottom)
        {
            y = workArea.Bottom - height;
        }

        if (x < workArea.X)
        {
            x = workArea.X;
        }

        if (y < workArea.Y)
        {
            y = workArea.Y;
        }

        return new DipRect(x, y, width, height);
    }

    public static bool IsSufficientlyVisible(DipRect window, DipRect virtualScreen, double minimumDip = 48)
    {
        var visibleWidth = Math.Min(window.Right, virtualScreen.Right) - Math.Max(window.X, virtualScreen.X);
        var visibleHeight = Math.Min(window.Bottom, virtualScreen.Bottom) - Math.Max(window.Y, virtualScreen.Y);
        return visibleWidth >= minimumDip && visibleHeight >= minimumDip;
    }
}

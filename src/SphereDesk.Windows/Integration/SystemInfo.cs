using SphereDesk.Windows.Interop;

namespace SphereDesk.Windows.Integration;

public readonly record struct MonitorDescription(string DeviceName);

public static class MonitorLayout
{
    public static IReadOnlyList<string> GetDeviceNames()
    {
        var names = new List<string>();
        NativeMethods.MonitorEnumProc callback = (nint monitor, nint _, ref NativeMethods.NativeRect _, nint _) =>
        {
            var info = new NativeMethods.MonitorInfoEx
            {
                Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
            };
            if (NativeMethods.GetMonitorInfo(monitor, ref info) && !string.IsNullOrWhiteSpace(info.Device))
            {
                names.Add(info.Device);
            }

            return true;
        };
        NativeMethods.EnumDisplayMonitors(0, 0, callback, 0);
        return names;
    }

    public static string? GetDeviceName(nint windowHandle)
    {
        var monitor = NativeMethods.MonitorFromWindow(windowHandle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == 0)
        {
            return null;
        }

        var info = new NativeMethods.MonitorInfoEx
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
        };
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? info.Device : null;
    }
}

public static class SystemMotion
{
    public static bool AnimationsEnabled()
    {
        var enabled = 1;
        var ok = NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETCLIENTAREAANIMATION, 0, ref enabled, 0);
        return !ok || enabled != 0;
    }
}

public static class DragMetrics
{
    public static double ThresholdDip(double dpiScale)
    {
        var px = Math.Max(NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDRAG), NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDRAG));
        if (px <= 0)
        {
            px = 4;
        }

        var scale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        return px / scale;
    }

    public static int DoubleClickMilliseconds()
    {
        var value = NativeMethods.GetDoubleClickTime();
        return value == 0 ? 500 : (int)value;
    }
}

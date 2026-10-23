using System.Runtime.InteropServices;

namespace SphereDesk.Windows.Integration;

public sealed class DesktopIconVisibility : IDisposable
{
    private const uint HideIconsMask = 0x00004000;
    private const int HideIconsBit = 12;
    private readonly bool _hiddenByUs;
    private bool _disposed;

    private DesktopIconVisibility(bool hiddenByUs, bool areHidden)
    {
        _hiddenByUs = hiddenByUs;
        AreHidden = areHidden;
    }

    public bool AreHidden { get; }

    public static DesktopIconVisibility HideForSession()
    {
        RecoverInterruptedSession();
        if (IsHidden())
        {
            return new DesktopIconVisibility(false, true);
        }

        if (!SetHidden(true) || !IsHidden())
        {
            AppLog.Write("IconsHideFailed");
            return new DesktopIconVisibility(false, false);
        }

        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SphereDesk");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "desktop-icons.marker"), "hidden");
        }
        catch (IOException)
        {
            AppLog.Write("IconsHideFailed");
        }
        catch (UnauthorizedAccessException)
        {
            AppLog.Write("IconsHideFailed");
        }

        return new DesktopIconVisibility(true, true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hiddenByUs && IsHidden())
        {
            SetHidden(false);
        }

        DeleteMarker();
    }

    private static void RecoverInterruptedSession()
    {
        if (!File.Exists(MarkerPath()))
        {
            return;
        }

        if (IsHidden())
        {
            SetHidden(false);
        }

        DeleteMarker();
    }

    private static bool IsHidden()
    {
        var state = new ShellState();
        SHGetSetSettings(ref state, HideIconsMask, false);
        return (state.Bits & (1u << HideIconsBit)) != 0;
    }

    private static bool SetHidden(bool hidden)
    {
        var state = new ShellState { Bits = hidden ? 1u << HideIconsBit : 0 };
        SHGetSetSettings(ref state, HideIconsMask, true);
        return true;
    }

    private static string MarkerPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SphereDesk", "desktop-icons.marker");

    private static void DeleteMarker()
    {
        try
        {
            File.Delete(MarkerPath());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHGetSetSettings(ref ShellState state, uint mask, bool set);

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellState
    {
        public uint Bits;
        public uint Win95Unused;
        public uint Win95Unused2;
        public int SortParameter;
        public int SortDirection;
        public uint Version;
        public uint NotUsed;
        public uint Bits2;
    }
}

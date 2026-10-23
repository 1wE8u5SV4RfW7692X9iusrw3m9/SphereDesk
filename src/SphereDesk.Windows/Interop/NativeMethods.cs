using System.Runtime.InteropServices;

namespace SphereDesk.Windows.Interop;

internal static class NativeMethods
{
    public const int HWND_MESSAGE = -3;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint NIN_SELECT = 0x0400;
    public const uint WM_APP_TRAY = 0x8001;
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint MF_STRING = 0x00000000;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;
    public const int SM_CXDRAG = 68;
    public const int SM_CYDRAG = 69;
    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int COINIT_APARTMENTTHREADED = 2;
    public const int BI_RGB = 0;
    public const uint DIB_RGB_COLORS = 0;
    public const uint SEE_MASK_NOASYNC = 0x00000100;
    public const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    public const int SW_SHOWNORMAL = 1;
    public const int SIIGBF_BIGGERSIZEOK = 0x1;
    public const int SIIGBF_ICONONLY = 0x4;
    public static readonly Guid FolderIdDesktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    public static readonly Guid FolderIdPublicDesktop = new("C4AA340D-F20F-4863-AFEF-F87EF2E6BA25");
    public static readonly Guid ShellItemImageFactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [DllImport("shell32.dll")]
    public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, nint token, out nint path);

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, out nint item);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(int style, string className, string windowName, int windowStyle, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? module);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[]? bits, ref BitmapInfo info, uint usage);

    [DllImport("gdi32.dll")]
    public static extern int GetObject(nint handle, int size, out Bitmap bitmap);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out NativeMessage message, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateFile(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint handle);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationFile(nint file, out IoStatusBlock status, nint info, uint length, int infoClass);

    public delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);
    public delegate bool MonitorEnumProc(nint monitor, nint hdc, ref NativeRect rect, nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WindowClass
    {
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyIconData
    {
        public int Size;
        public nint WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public nint WindowHandle;
        public string? Verb;
        public string? File;
        public string? Parameters;
        public string? Directory;
        public int Show;
        public nint Instance;
        public nint IdList;
        public string? ClassName;
        public nint ClassKey;
        public uint HotKey;
        public nint Icon;
        public nint Process;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeMessage
    {
        public nint WindowHandle;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Size
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPerPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ColorsUsed;
        public int ImportantColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint RedMask;
        public uint GreenMask;
        public uint BlueMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IoStatusBlock
    {
        public nint Status;
        public nint Information;
    }
}

[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(NativeMethods.Size size, int flags, out nint bitmap);
}

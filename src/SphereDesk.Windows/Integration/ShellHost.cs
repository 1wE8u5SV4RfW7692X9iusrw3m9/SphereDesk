using System.Runtime.InteropServices;
using SphereDesk.Core.Models;
using SphereDesk.Windows.Interop;

namespace SphereDesk.Windows.Integration;

public sealed class ShellHost : IDisposable
{
    private const int HotkeyId = 1;
    private const uint TrayId = 1;
    private readonly NativeMethods.WindowProcedure _procedure;
    private nint _handle;
    private bool _hotkeyRegistered;
    private bool _trayAdded;

    public ShellHost()
    {
        _procedure = WindowProc;
    }

    public event Action? HotkeyPressed;
    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action? DisplayChanged;

    public nint Handle => _handle;

    public void Create()
    {
        if (_handle != 0)
        {
            return;
        }

        const string className = "SphereDesk.ShellHost";
        var instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new NativeMethods.WindowClass
        {
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = instance,
            ClassName = className,
        };
        NativeMethods.RegisterClass(ref windowClass);
        _handle = NativeMethods.CreateWindowEx(0, className, "SphereDesk", 0, 0, 0, 0, 0, NativeMethods.HWND_MESSAGE, 0, instance, 0);
        if (_handle == 0)
        {
            AppLog.Write("ShellHostFailed", Marshal.GetLastWin32Error());
        }
    }

    public bool TryRegisterHotkey(HotkeyGesture gesture, out int error)
    {
        error = 0;
        if (_handle == 0)
        {
            error = -1;
            return false;
        }

        UnregisterHotkey();
        if (!gesture.IsValid)
        {
            error = -1;
            return false;
        }

        var ok = NativeMethods.RegisterHotKey(_handle, HotkeyId, (uint)gesture.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)gesture.VirtualKey);
        _hotkeyRegistered = ok;
        if (!ok)
        {
            error = Marshal.GetLastWin32Error();
            AppLog.Write("HotkeyRejected", error);
        }

        return ok;
    }

    public void UnregisterHotkey()
    {
        if (_handle != 0 && _hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(_handle, HotkeyId);
            _hotkeyRegistered = false;
        }
    }

    public void AddTray()
    {
        if (_handle == 0 || _trayAdded)
        {
            return;
        }

        var data = CreateTrayData();
        _trayAdded = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (!_trayAdded)
        {
            AppLog.Write("TrayFailed", Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        UnregisterHotkey();
        if (_trayAdded && _handle != 0)
        {
            var data = CreateTrayData();
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _trayAdded = false;
        }

        if (_handle != 0)
        {
            NativeMethods.DestroyWindow(_handle);
            _handle = 0;
        }
    }

    private NativeMethods.NotifyIconData CreateTrayData()
    {
        var data = new NativeMethods.NotifyIconData
        {
            WindowHandle = _handle,
            Id = TrayId,
            Flags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            CallbackMessage = NativeMethods.WM_APP_TRAY,
            Icon = NativeMethods.LoadIcon(0, 32512),
            Tip = "SphereDesk",
        };
        data.Size = Marshal.SizeOf<NativeMethods.NotifyIconData>();
        return data;
    }

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == NativeMethods.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke();
            return 0;
        }

        if (message == NativeMethods.WM_DISPLAYCHANGE)
        {
            DisplayChanged?.Invoke();
            return 0;
        }

        if (message == NativeMethods.WM_APP_TRAY)
        {
            var low = (uint)(lParam.ToInt64() & 0xFFFF);
            if (low is NativeMethods.WM_LBUTTONDBLCLK or NativeMethods.NIN_SELECT)
            {
                ShowRequested?.Invoke();
            }
            else if (low == NativeMethods.WM_RBUTTONUP)
            {
                ShowMenu(hwnd);
            }

            return 0;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu(nint hwnd)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 1, "显示 SphereDesk");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 2, "退出");
            NativeMethods.GetCursorPos(out var point);
            NativeMethods.SetForegroundWindow(hwnd);
            var command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                point.X,
                point.Y,
                0,
                hwnd,
                0);
            if (command == 1)
            {
                ShowRequested?.Invoke();
            }
            else if (command == 2)
            {
                ExitRequested?.Invoke();
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }
}

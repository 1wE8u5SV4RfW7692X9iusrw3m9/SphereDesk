using System.Runtime.InteropServices;
using SphereDesk.Windows.Interop;

namespace SphereDesk.Windows.Shell;

public static class ShellContextMenu
{
    private const uint ContextMenuMessage = 0x007B;
    private const int DesktopWindowClass = 8;
    private const int NeedDispatch = 1;
    private const uint DeselectFlags = 0x4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserId = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");

    public static bool ShowForPath(nint owner, int x, int y, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var parsed = SHParseDisplayName(path, 0, out var absolute, 0, out _);
        if (parsed < 0 || absolute == 0)
        {
            if (absolute != 0)
            {
                CoTaskMemFree(absolute);
            }

            return false;
        }

        IShellFolder? parent = null;
        nint child = 0;
        nint menuPointer = 0;
        try
        {
            var folderId = ShellFolderId;
            var bound = SHBindToParent(absolute, ref folderId, out parent, out child);
            if (bound < 0 || parent is null || child == 0)
            {
                return false;
            }

            var menuId = ContextMenuId;
            var created = parent.GetUIObjectOf(owner, 1, ref child, ref menuId, 0, out menuPointer);
            if (created < 0 || menuPointer == 0)
            {
                return false;
            }

            return ShowClassicMenu(menuPointer, owner, x, y, 0x14);
        }
        finally
        {
            if (menuPointer != 0)
            {
                Marshal.Release(menuPointer);
            }

            if (parent is not null)
            {
                Marshal.FinalReleaseComObject(parent);
            }

            CoTaskMemFree(absolute);
        }
    }

    public static bool ShowForDesktop(nint owner, int x, int y)
    {
        if (!TryClearDesktopSelection())
        {
            return false;
        }

        return PostDesktopMenu(owner, x, y);
    }

    public static bool IsSystemMenuOpen()
    {
        var open = false;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var name = ClassName(hwnd);
            if (name is "#32768" or "Microsoft.UI.Content.PopupWindowSiteBridge")
            {
                open = true;
                return false;
            }

            return true;
        }, 0);
        return open;
    }

    public static void KeepSurfaceBehind(nint surface)
    {
        if (surface != 0)
        {
            SetWindowPos(surface, 1, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        }
    }

    private static bool ShowClassicMenu(nint menuPointer, nint owner, int x, int y, uint flags)
    {
        var menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPointer);
        nint popup = 0;
        try
        {
            popup = NativeMethods.CreatePopupMenu();
            if (popup == 0)
            {
                return false;
            }

            var filled = menu.QueryContextMenu(popup, 0, 1, 0x7FFF, flags);
            if (filled < 0)
            {
                return false;
            }

            KeepSurfaceBehind(owner);
            if (owner != 0)
            {
                NativeMethods.SetForegroundWindow(owner);
                KeepSurfaceBehind(owner);
            }

            var command = NativeMethods.TrackPopupMenu(popup, NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_NONOTIFY | NativeMethods.TPM_RETURNCMD, x, y, 0, owner, 0);
            if (command == 0)
            {
                return true;
            }

            var invoke = new InvokeInfo
            {
                Size = Marshal.SizeOf<InvokeInfo>(),
                Window = owner,
                Verb = command - 1,
                Show = NativeMethods.SW_SHOWNORMAL,
            };
            return menu.InvokeCommand(ref invoke) >= 0;
        }
        finally
        {
            if (popup != 0)
            {
                NativeMethods.DestroyMenu(popup);
            }

            Marshal.FinalReleaseComObject(menu);
        }
    }

    private static bool PostDesktopMenu(nint owner, int x, int y)
    {
        var view = FindDesktopView();
        if (view == 0)
        {
            return false;
        }

        KeepSurfaceBehind(owner);
        var packed = unchecked((nint)((ushort)(short)x | ((uint)unchecked((ushort)(short)y) << 16)));
        return PostMessage(view, ContextMenuMessage, view, packed);
    }

    private static bool TryClearDesktopSelection()
    {
        if (!TryGetFolderView(out var folderView, out var shellView))
        {
            return false;
        }

        try
        {
            return folderView.SelectByIndex(-1, DeselectFlags) >= 0;
        }
        finally
        {
            ReleaseView(folderView, shellView);
        }
    }

    private static void ReleaseView(IFolderView folderView, IShellView shellView)
    {
        Marshal.FinalReleaseComObject(folderView);
        Marshal.FinalReleaseComObject(shellView);
    }

    private static bool TryGetFolderView(out IFolderView folderView, out IShellView shellView)
    {
        folderView = null!;
        shellView = null!;
        var windows = (IShellWindows)new ShellWindowsType();
        nint dispatch = 0;
        nint browserPointer = 0;
        nint viewPointer = 0;
        try
        {
            var location = new Variant();
            var root = new Variant();
            var found = windows.FindWindowSW(ref location, ref root, DesktopWindowClass, out _, NeedDispatch, out dispatch);
            if (found < 0 || dispatch == 0)
            {
                return false;
            }

            var service = (IServiceProvider)Marshal.GetObjectForIUnknown(dispatch);
            var serviceId = TopLevelBrowser;
            var browserId = ShellBrowserId;
            var queried = service.QueryService(ref serviceId, ref browserId, out browserPointer);
            Marshal.FinalReleaseComObject(service);
            if (queried < 0 || browserPointer == 0)
            {
                return false;
            }

            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(browserPointer);
            var viewed = browser.QueryActiveShellView(out viewPointer);
            Marshal.FinalReleaseComObject(browser);
            if (viewed < 0 || viewPointer == 0)
            {
                return false;
            }

            shellView = (IShellView)Marshal.GetObjectForIUnknown(viewPointer);
            var folderId = FolderViewId;
            var queriedFolder = Marshal.QueryInterface(viewPointer, in folderId, out var folderPointer);
            if (queriedFolder < 0 || folderPointer == 0)
            {
                Marshal.FinalReleaseComObject(shellView);
                shellView = null!;
                return false;
            }

            folderView = (IFolderView)Marshal.GetObjectForIUnknown(folderPointer);
            Marshal.Release(folderPointer);
            return true;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (dispatch != 0)
            {
                Marshal.Release(dispatch);
            }

            if (browserPointer != 0)
            {
                Marshal.Release(browserPointer);
            }

            if (viewPointer != 0)
            {
                Marshal.Release(viewPointer);
            }

            Marshal.FinalReleaseComObject(windows);
        }
    }

    private static nint FindDesktopView()
    {
        var progman = FindWindow("Progman", "Program Manager");
        var view = progman == 0 ? 0 : FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        if (view != 0)
        {
            return view;
        }

        EnumWindows((hwnd, _) =>
        {
            if (ClassName(hwnd) == "WorkerW")
            {
                var child = FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null);
                if (child != 0)
                {
                    view = child;
                    return false;
                }
            }

            return true;
        }, 0);
        return view;
    }

    private static string ClassName(nint window)
    {
        var buffer = new char[256];
        var length = GetClassName(window, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint absolute, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(nint absolute, ref Guid interfaceId, out IShellFolder parent, out nint child);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(nint pointer);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProcedure procedure, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, char[] buffer, int capacity);

    private delegate bool EnumProcedure(nint hwnd, nint parameter);

    [ComImport]
    [Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
    private class ShellWindowsType
    {
    }

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellWindows
    {
        void GetTypeInfoCount();
        void GetTypeInfo();
        void GetIDsOfNames();
        void Invoke();
        void Count();
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [PreserveSig]
        int FindWindowSW(ref Variant location, ref Variant root, int windowClass, out int hwnd, int options, out nint dispatch);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid interfaceId, out nint unknown);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenus();
        void SetMenu();
        void RemoveMenus();
        void SetStatusText();
        void EnableModeless();
        void TranslateAccelerator();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();

        [PreserveSig]
        int QueryActiveShellView(out nint shellView);
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
        void CreateViewWindow();
        void DestroyViewWindow();
        void GetCurrentInfo();
        void AddPropertySheetPages();
        void SaveViewState();

        [PreserveSig]
        int SelectItem(nint relative, uint flags);
    }

    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder();
        void Item();
        void ItemCount();
        void Items();
        void GetSelectionMarkedItem();
        void GetFocusedItem();
        void GetItemPosition();
        void GetSpacing();
        void GetDefaultSpacing();
        void GetAutoArrange();

        [PreserveSig]
        int SelectByIndex(int index, uint flags);
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(nint hwnd, nint bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out nint relative, ref uint attributes);

        [PreserveSig]
        int EnumObjects(nint hwnd, uint flags, out nint enumerator);

        [PreserveSig]
        int BindToObject(nint relative, nint bindContext, ref Guid interfaceId, out nint unknown);

        [PreserveSig]
        int BindToStorage(nint relative, nint bindContext, ref Guid interfaceId, out nint unknown);

        [PreserveSig]
        int CompareIDs(nint parameter, nint left, nint right);

        [PreserveSig]
        int CreateViewObject(nint owner, ref Guid interfaceId, out nint unknown);

        [PreserveSig]
        int GetAttributesOf(uint count, nint items, ref uint attributes);

        [PreserveSig]
        int GetUIObjectOf(nint owner, uint count, ref nint items, ref Guid interfaceId, nint reserved, out nint unknown);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(nint menu, uint index, uint firstId, uint lastId, uint flags);

        [PreserveSig]
        int InvokeCommand(ref InvokeInfo info);

        [PreserveSig]
        int GetCommandString(nuint command, uint type, nint reserved, nint name, uint nameSize);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeInfo
    {
        public int Size;
        public int Mask;
        public nint Window;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public int HotKey;
        public nint Icon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Variant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Data1;
        public nint Data2;
    }
}

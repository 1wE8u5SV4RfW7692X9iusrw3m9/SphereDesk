using System.Runtime.InteropServices;
using SphereDesk.Application.Abstractions;
using SphereDesk.Core.Models;
using SphereDesk.Windows.Integration;
using SphereDesk.Windows.Interop;

namespace SphereDesk.Windows.Shell;

public sealed class ShellLauncher : IShellLauncher
{
    public Task<OperationResult> OpenAsync(DesktopItem item, nint ownerWindow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!item.Exists || item.Availability == ItemAvailability.Missing)
        {
            return Task.FromResult(OperationResult.Fail("这个项目已经不存在，请刷新。"));
        }

        if (item.Availability == ItemAvailability.SourceUnavailable)
        {
            return Task.FromResult(OperationResult.Fail("这个桌面位置暂时不可用，请稍后刷新。"));
        }

        var info = new NativeMethods.ShellExecuteInfo
        {
            Size = Marshal.SizeOf<NativeMethods.ShellExecuteInfo>(),
            Mask = NativeMethods.SEE_MASK_NOASYNC | NativeMethods.SEE_MASK_FLAG_NO_UI,
            WindowHandle = ownerWindow,
            Verb = null,
            File = item.OriginalPath,
            Show = NativeMethods.SW_SHOWNORMAL,
        };

        var opened = NativeMethods.ShellExecuteEx(ref info);
        if (opened)
        {
            return Task.FromResult(OperationResult.Ok());
        }

        var error = Marshal.GetLastWin32Error();
        AppLog.Write("OpenFailed", error);
        return Task.FromResult(OperationResult.Fail(Describe(error), error));
    }

    private static string Describe(int error) => error switch
    {
        2 or 3 => "找不到这个项目，请刷新。",
        5 => "没有权限打开这个项目。",
        1155 => "系统没有找到可以打开它的程序。",
        1223 => "已取消打开。",
        _ => "系统无法打开这个项目。",
    };
}

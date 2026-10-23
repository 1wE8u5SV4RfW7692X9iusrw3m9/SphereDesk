using System.Runtime.InteropServices;
using SphereDesk.Application.Abstractions;
using SphereDesk.Core.Models;
using SphereDesk.Windows.Interop;

namespace SphereDesk.Windows.Shell;

public sealed class KnownFolderLocator : IDesktopRoots
{
    public IReadOnlyList<SourceRoot> Resolve() =>
    [
        Resolve(SourceRootIds.UserDesktop, KnownFolderKind.UserDesktop, NativeMethods.FolderIdDesktop),
        Resolve(SourceRootIds.PublicDesktop, KnownFolderKind.PublicDesktop, NativeMethods.FolderIdPublicDesktop),
    ];

    private static SourceRoot Resolve(string id, KnownFolderKind kind, Guid folderId)
    {
        var hr = NativeMethods.SHGetKnownFolderPath(folderId, 0, 0, out var pointer);
        if (hr < 0 || pointer == 0)
        {
            return new SourceRoot(id, kind, null, ScanStatus.Failed, "无法读取这个桌面位置。");
        }

        try
        {
            var path = Marshal.PtrToStringUni(pointer);
            if (string.IsNullOrWhiteSpace(path))
            {
                return new SourceRoot(id, kind, null, ScanStatus.Failed, "无法读取这个桌面位置。");
            }

            return new SourceRoot(id, kind, path, ScanStatus.NotScanned, null);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }
}

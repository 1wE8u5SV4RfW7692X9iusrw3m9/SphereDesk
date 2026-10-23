using System.Runtime.InteropServices;
using SphereDesk.Windows.Interop;

namespace SphereDesk.Windows.Shell;

public static class DirectoryCaseSensitivity
{
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareAll = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FileCaseSensitiveInformation = 71;
    private const uint CaseSensitiveDirectory = 0x1;

    public static bool IsCaseSensitive(string directory)
    {
        var handle = NativeMethods.CreateFile(
            directory,
            FileReadAttributes,
            FileShareAll,
            0,
            OpenExisting,
            FileFlagBackupSemantics,
            0);
        if (handle == 0 || handle == -1)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, 0);
            var status = NativeMethods.NtQueryInformationFile(handle, out _, buffer, 4, FileCaseSensitiveInformation);
            if (status < 0)
            {
                return false;
            }

            return (Marshal.ReadInt32(buffer) & CaseSensitiveDirectory) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            NativeMethods.CloseHandle(handle);
        }
    }
}

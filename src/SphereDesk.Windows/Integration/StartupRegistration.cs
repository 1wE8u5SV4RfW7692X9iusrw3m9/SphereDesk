using Microsoft.Win32;

namespace SphereDesk.Windows.Integration;

public static class StartupRegistration
{
    private const string ValueName = "SphereDesk";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                AppLog.Write("StartupRegistrationFailed", -1);
                return;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                AppLog.Write("StartupRegistrationFailed", -1);
                return;
            }

            key.SetValue(ValueName, "\"" + path + "\"");
        }
        catch (Exception)
        {
            AppLog.Write("StartupRegistrationFailed");
        }
    }
}

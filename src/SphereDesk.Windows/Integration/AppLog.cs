namespace SphereDesk.Windows.Integration;

public static class AppLog
{
    private static readonly object Gate = new();

    public static void Write(string code, int? win32 = null)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SphereDesk", "logs");
            Directory.CreateDirectory(directory);
            var line = win32 is null
                ? $"{DateTimeOffset.Now:O} {code}{Environment.NewLine}"
                : $"{DateTimeOffset.Now:O} {code} {win32.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(directory, "app.log"), line);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

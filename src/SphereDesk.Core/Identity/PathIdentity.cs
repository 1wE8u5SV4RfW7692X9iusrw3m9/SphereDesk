namespace SphereDesk.Core.Identity;

public static class PathIdentity
{
    public static string Store(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > 3)
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return full;
    }

    public static bool Equals(string left, string right, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(Store(left), Store(right), comparison);
    }

    public static string ItemId(string sourceRootId, string storedPath) => sourceRootId + "|" + storedPath;
}

using SphereDesk.Core.Models;

namespace SphereDesk.Core.Search;

public static class SearchRanker
{
    public static IReadOnlyList<DesktopItem> Rank(IEnumerable<DesktopItem> items, string? query, ItemKind? kind)
    {
        var text = query?.Trim() ?? string.Empty;
        var filtered = items.Where(item => kind is null || item.Kind == kind);
        if (text.Length == 0)
        {
            return filtered
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.OriginalPath, StringComparer.Ordinal)
                .ToArray();
        }

        return filtered
            .Select(item => (Item: item, Score: Score(item.DisplayName, text)))
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Item.OriginalPath, StringComparer.Ordinal)
            .Select(entry => entry.Item)
            .ToArray();
    }

    public static int Score(string name, string query)
    {
        if (name.Equals(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 1000;
        }

        if (name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 800;
        }

        if (name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return 500;
        }

        return 0;
    }
}

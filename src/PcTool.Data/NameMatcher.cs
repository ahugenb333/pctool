using System.Text.RegularExpressions;

namespace PcTool.Data;

/// <summary>
/// Matches hardware names ("13th Gen Intel(R) Core(TM) i7-13700KF", "ROG Astral GeForce RTX 5090 32GB") to tier entries
/// by whole-word alias containment. The longest matching alias wins, so "rtx 4060 ti" beats "rtx 4060" and
/// "rtx 4060 laptop" beats both on a laptop name.
/// </summary>
public static partial class NameMatcher
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = name.ToLowerInvariant()
            .Replace("(r)", " ").Replace("(tm)", " ").Replace("®", " ").Replace("™", " ")
            .Replace('_', ' ');
        return Spaces().Replace(s, " ").Trim();
    }

    public static string Slug(string name) => NonSlug().Replace(Normalize(name), "-").Trim('-');

    public static T? BestMatch<T>(string? name, IEnumerable<(T Item, string Alias)> aliases) where T : class
    {
        var normalized = Normalize(name);
        if (normalized.Length == 0) return null;

        T? best = null;
        var bestLength = 0;
        foreach (var (item, alias) in aliases)
        {
            if (alias.Length <= bestLength) continue;
            if (ContainsWord(normalized, alias))
            {
                best = item;
                bestLength = alias.Length;
            }
        }
        return best;
    }

    private static bool ContainsWord(string haystack, string needle)
    {
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var end = index + needle.Length;
            var after = end == haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (before && after) return true;
            index++;
        }
        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();
}

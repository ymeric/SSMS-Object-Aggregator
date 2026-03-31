using System.Text.RegularExpressions;

namespace SSMS.ObjectAggregator.Services;

public static partial class FilterPatternMatcher
{
    public static bool IsMatch(string? pattern, string candidate)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        string regexPattern = ConvertToRegex(pattern!.Trim());
        return Regex.IsMatch(candidate, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ConvertToRegex(string pattern)
    {
        // No wildcards → treat as a substring (contains) search so that typing a partial
        // name finds objects whose name includes the text anywhere (case-insensitive).
        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return Regex.Escape(pattern);
        }

        string escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return $"^{escaped}$";
    }
}
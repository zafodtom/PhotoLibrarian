using System.Globalization;
using System.Text;

namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Matches tag names and full hierarchy paths without requiring users to type Czech accents.
/// Stored tag values are never modified.
/// </summary>
public static class TagSearchMatcher
{
    public static bool Contains(string? tag, string? query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (string.IsNullOrEmpty(tag)) return false;
        return Fold(tag).Contains(Fold(query), StringComparison.OrdinalIgnoreCase);
    }

    private static string Fold(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

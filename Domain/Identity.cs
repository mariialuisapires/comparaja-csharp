using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ComparadorPrecos.Domain;

public static partial class ProductIdentity
{
    [GeneratedRegex(@"[^a-z0-9\s]")]
    private static partial Regex NonAlnum();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();

    public static string CanonicalProductName(string name)
    {
        var s = RemoveDiacritics((name ?? "").ToLowerInvariant());
        s = NonAlnum().Replace(s, " ");
        return MultiSpace().Replace(s.Trim(), " ");
    }

    public static string? LinkStatusForScore(double score) => score switch
    {
        >= Thresholds.AutoLink     => "auto",
        >= Thresholds.ManualReview => "pending",
        _                          => null
    };

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }
}

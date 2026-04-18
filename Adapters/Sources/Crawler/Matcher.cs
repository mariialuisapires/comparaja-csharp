using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FuzzySharp;

namespace ComparadorPrecos.Adapters.Sources.Crawler;

public static partial class Matcher
{
    [GeneratedRegex(@"[^\w\s]", RegexOptions.Compiled)]
    private static partial Regex PunctRegex();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(gb|tb|mp|hz|polegadas?|\""|inch|anos?|years?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FeatureRegex();

    // Words that, when present in the candidate but absent from the query, indicate an accessory/part
    private static readonly string[] AccessoryKeywords =
    [
        "tela", "lcd", "display", "capa", "pelicula", "cabo", "carregador", "bateria",
        "conector", "peca", "reparo", "substituicao", "compativel", "suporte", "case",
        "estojo", "protetor", "vidro", "fonte", "adaptador", "kit ", "lente", "camera",
        "alto-falante", "alto falante", "speaker", "microfone", "flex", "placa", "tampa",
        "traseira", "chassi", "aro", "bumper", "capinha", "skin"
    ];

    public static string Normalize(string text)
    {
        var s = RemoveDiacritics(text.ToLowerInvariant());
        s = PunctRegex().Replace(s, " ");
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static double ScoreMatch(string query, string candidate)
    {
        var nq = Normalize(query);
        var nc = Normalize(candidate);

        var tokenSet = Fuzz.TokenSetRatio(nq, nc);
        var partial  = Fuzz.PartialRatio(nq, nc);
        var base_    = tokenSet * 0.6 + partial * 0.4;

        var qFeats = ExtractFeatures(nq);
        var cFeats = ExtractFeatures(nc);

        double bonus = 0;
        foreach (var (k, v) in qFeats)
        {
            if (cFeats.TryGetValue(k, out var cv))
            {
                if (Math.Abs(v - cv) < 0.01) bonus += 5;
            }
            else
            {
                bonus -= 8;
            }
        }

        // Penalize when candidate contains accessory/part keywords absent from the query
        foreach (var kw in AccessoryKeywords)
        {
            if (!nq.Contains(kw) && nc.Contains(kw))
            {
                bonus -= 30;
                break;
            }
        }

        return Math.Clamp(base_ + bonus, 0, 100);
    }

    public static void RankResults(string query, List<(string Title, object Data)> items)
        => items.Sort((a, b) =>
            ScoreMatch(query, b.Title).CompareTo(ScoreMatch(query, a.Title)));

    private static Dictionary<string, double> ExtractFeatures(string text)
    {
        var result = new Dictionary<string, double>();
        foreach (Match m in FeatureRegex().Matches(text))
        {
            var num  = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var unit = m.Groups[2].Value.ToLowerInvariant();
            result[unit] = num;
        }
        return result;
    }

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

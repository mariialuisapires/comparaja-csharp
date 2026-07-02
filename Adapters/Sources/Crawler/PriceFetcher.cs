using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using AngleSharp.Dom;
using ComparadorPrecos.Domain;

namespace ComparadorPrecos.Adapters.Sources.Crawler;

public sealed partial class PriceFetcher : IDisposable
{
    private readonly HttpClient _http;
    private readonly HtmlParser _parser = new();

    // Captura "R$ 1.234,99" — o grupo 1 é o número
    [GeneratedRegex(@"R\$\s*([\d]{1,3}(?:[.]\d{3})*,\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex BrlPriceRegex();

    // Último segmento numérico ≥ 4 dígitos na URL
    [GeneratedRegex(@"/(\d{4,})(?:[/?#].*)?$")]
    private static partial Regex ProductIdRegex();

    public PriceFetcher()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect      = true,
            AutomaticDecompression =
                System.Net.DecompressionMethods.GZip |
                System.Net.DecompressionMethods.Deflate |
                System.Net.DecompressionMethods.Brotli,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        _http.DefaultRequestHeaders.Add("Accept-Language",
            "pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7");
        _http.DefaultRequestHeaders.Add("Referer", "https://www.google.com.br");
    }

    public async Task<ListingSnapshot?> FetchAsync(string url, string storeName,
        CancellationToken ct = default)
    {
        try
        {
            var html = await _http.GetStringAsync(url, ct);
            var doc  = await _parser.ParseDocumentAsync(html, ct);

            // Estratégias em ordem: JSON-LD → Schema.org → OpenGraph → Regex
            var price = TryJsonLd(doc)
                     ?? TrySchemaOrg(doc)
                     ?? TryOpenGraph(doc)
                     ?? TryRegex(doc.Body?.TextContent ?? "");

            var title = Attr(doc.QuerySelector("meta[property='og:title']"), "content")
                     ?? Text(doc.QuerySelector("h1"))
                     ?? Text(doc.QuerySelector("title"))
                     ?? storeName;

            if (title.Length > 200) title = title[..200];

            return new ListingSnapshot(
                Site:       storeName.ToLowerInvariant(),
                SiteId:     ExtractSiteId(url),
                Title:      title,
                Url:        url,
                Price:      price,
                FetchedAt:  DateTime.UtcNow,
                MatchScore: 100.0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [PriceFetcher] erro em {url}: {ex.Message}");
            return null;
        }
    }

    // ── Estratégia 0 — JSON-LD (Schema.org structured data) ─────────────────

    private static decimal? TryJsonLd(IDocument doc)
    {
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var price = ExtractPriceFromJsonLd(json.RootElement);
                if (price > 1) return price;
            }
            catch { }
        }
        return null;
    }

    private static decimal? ExtractPriceFromJsonLd(JsonElement el)
    {
        if (el.TryGetProperty("price", out var pEl))
        {
            var p = pEl.ValueKind == JsonValueKind.Number ? pEl.GetDecimal()
                  : ParseDecimal(pEl.GetString());
            if (p > 1) return p;
        }
        if (el.TryGetProperty("offers", out var offers))
        {
            var p = offers.ValueKind == JsonValueKind.Array
                ? offers.EnumerateArray().Select(ExtractPriceFromJsonLd).FirstOrDefault(x => x > 1)
                : ExtractPriceFromJsonLd(offers);
            if (p > 1) return p;
        }
        if (el.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in graph.EnumerateArray())
            {
                var p = ExtractPriceFromJsonLd(item);
                if (p > 1) return p;
            }
        }
        return null;
    }

    // ── Estratégia 1 — Schema.org (itemprop="price") ─────────────────────────

    private static decimal? TrySchemaOrg(IDocument doc)
    {
        foreach (var el in doc.QuerySelectorAll("[itemprop='price']"))
        {
            var content = el.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(content))
            {
                var p = ParseDecimal(content);
                if (p > 1) return p;
            }
            var fromText = ParseBrl(el.TextContent);
            if (fromText > 1) return fromText;
        }
        return null;
    }

    // ── Estratégia 2 — OpenGraph / meta tags ─────────────────────────────────

    private static decimal? TryOpenGraph(IDocument doc)
    {
        string[] selectors =
        [
            "meta[property='product:price:amount']",
            "meta[name='price']",
            "meta[property='og:price:amount']",
        ];
        foreach (var sel in selectors)
        {
            var p = ParseDecimal(doc.QuerySelector(sel)?.GetAttribute("content"));
            if (p > 1) return p;
        }
        return null;
    }

    // ── Estratégia 3 — Regex no texto visível (threshold maior: >10) ─────────

    private static decimal? TryRegex(string text)
    {
        foreach (Match m in BrlPriceRegex().Matches(text))
        {
            var p = ParseBrl(m.Groups[1].Value);
            if (p > 10) return p;
        }
        return null;
    }

    // ── Helpers de parse ─────────────────────────────────────────────────────

    // Formato neutro/inglês: "1234.99", "29.90"
    private static decimal? ParseDecimal(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var clean = input.Trim().Replace(",", ".");
        return decimal.TryParse(clean, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // Formato brasileiro: "1.234,99", "29,90"
    private static decimal? ParseBrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var clean = input.Trim().Replace(".", "").Replace(",", ".");
        return decimal.TryParse(clean, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // Tenta extrair o ID numérico do produto da URL; fallback é hash da URL
    private static string ExtractSiteId(string url)
    {
        try
        {
            var m = ProductIdRegex().Match(new Uri(url).AbsolutePath);
            if (m.Success) return m.Groups[1].Value;
        }
        catch { }
        return Math.Abs(url.GetHashCode()).ToString("x8");
    }

    private static string? Attr(IElement? el, string attr) => el?.GetAttribute(attr)?.Trim();
    private static string? Text(IElement? el) => el?.TextContent?.Trim();

    public void Dispose() => _http.Dispose();
}

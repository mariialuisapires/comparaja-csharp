using System.Text.RegularExpressions;
using ComparadorPrecos.Domain;

namespace ComparadorPrecos.Adapters.Sources.Crawler.Sites;

public sealed partial class MagaluScraper : BaseScraper
{
    [GeneratedRegex(@"/p/(\w+)/")]
    private static partial Regex SkuRegex();

    public MagaluScraper(RateLimitedFetcher fetcher) : base(fetcher) { }

    public override string SiteName => "magalu";
    public override string Domain   => "www.magazineluiza.com.br";

    public override async Task<List<ListingSnapshot>> ScrapeAsync(
        string searchTerm, int maxResults, CancellationToken ct = default)
    {
        var q   = Uri.EscapeDataString(searchTerm);
        var url = $"https://www.magazineluiza.com.br/busca/{q}/";
        var html = await Fetcher.FetchHtmlAsync(url, Domain,
            waitSelector: "[data-testid='product-card-container']", ct: ct);

        var doc   = await Parser.ParseDocumentAsync(html, ct);
        // The container IS the <a> element — href lives directly on it
        var items = doc.QuerySelectorAll("[data-testid='product-card-container']");

        if (!items.Any())
            return new List<ListingSnapshot>();

        var results = new List<ListingSnapshot>();

        foreach (var item in items.Take(maxResults * 2))
        {
            var href    = Attr(item, "href");
            var titleEl = item.QuerySelector("[data-testid='product-title']");
            var priceEl = item.QuerySelector("[data-testid='price-value']");
            var origEl  = item.QuerySelector("[data-testid='price-original']");
            var imgEl   = item.QuerySelector("[data-testid='image']");

            var title = Text(titleEl);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href)) continue;

            var skuMatch = SkuRegex().Match(href);
            if (!skuMatch.Success) continue;
            var sku = skuMatch.Groups[1].Value;

            var fullUrl = href.StartsWith("http") ? href : "https://www.magazineluiza.com.br" + href;

            // Price text contains "ou R$&nbsp;5.065,15" — strip non-numeric prefix
            var priceText = Text(priceEl);
            if (priceText is not null)
                priceText = System.Text.RegularExpressions.Regex.Replace(priceText, @"^[^\d]+", "");

            results.Add(new ListingSnapshot(
                Site:          SiteName,
                SiteId:        sku,
                Title:         title,
                Url:           fullUrl.Split('?')[0],
                Price:         ParseBrl(priceText),
                OriginalPrice: ParseBrl(Text(origEl)),
                ImageUrl:      Attr(imgEl, "src"),
                FetchedAt:     DateTime.UtcNow));
        }

        return results;
    }

    public override async Task<ListingSnapshot?> FetchPriceFromUrlAsync(
        string url, CancellationToken ct = default)
    {
        var skuMatch = SkuRegex().Match(url);
        if (!skuMatch.Success) return null;
        var sku      = skuMatch.Groups[1].Value;
        var cleanUrl = url.Split('?')[0];

        // Product detail pages: don't wait for a specific selector — avoids timeout
        // on bot-detection pages that never render JS content
        var html = await Fetcher.FetchHtmlAsync(cleanUrl, Domain,
            waitSelector: null, ct: ct);
        var doc = await Parser.ParseDocumentAsync(html, ct);

        // Title: product detail uses heading-product-title; fall back to h1
        var title = Text(doc.QuerySelector("[data-testid='heading-product-title']"))
                 ?? Text(doc.QuerySelector("h1.product-title"))
                 ?? Text(doc.QuerySelector("h1"));

        // Detect bot-blocked / error pages
        if (string.IsNullOrWhiteSpace(title)
            || title.Contains("possível acessar", StringComparison.OrdinalIgnoreCase)
            || title.Contains("acesso negado", StringComparison.OrdinalIgnoreCase)
            || title.Contains("robô", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"    [magalu] página bloqueada ou inacessível: {title?.Trim()}");
            return null;
        }

        // Price: product detail page uses different testids than search results
        var priceText = Text(doc.QuerySelector("[data-testid='price-value']"))
                     ?? Text(doc.QuerySelector("[class*='Price__Value']"))
                     ?? Text(doc.QuerySelector("[class*='price-value']"));
        if (priceText is not null)
            priceText = System.Text.RegularExpressions.Regex.Replace(priceText, @"^[^\d]+", "");

        var origText = Text(doc.QuerySelector("[data-testid='price-original']"))
                    ?? Text(doc.QuerySelector("[class*='price-original']"));

        // Image: prefer the main selected image, fall back to first product image
        var imgSrc = Attr(doc.QuerySelector("[data-testid='image-selected-thumbnail']"), "src")
                  ?? Attr(doc.QuerySelector("[data-testid='image']"), "src")
                  ?? Attr(doc.QuerySelector("img[src*='mlcdn.com.br']"), "src");

        return new ListingSnapshot(
            Site:          SiteName,
            SiteId:        sku,
            Title:         title,
            Url:           cleanUrl,
            Price:         ParseBrl(priceText),
            OriginalPrice: ParseBrl(origText),
            ImageUrl:      imgSrc,
            MatchScore:    100.0,
            FetchedAt:     DateTime.UtcNow);
    }
}

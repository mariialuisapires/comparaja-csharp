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
}

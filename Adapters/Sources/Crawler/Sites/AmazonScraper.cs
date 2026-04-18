using ComparadorPrecos.Domain;

namespace ComparadorPrecos.Adapters.Sources.Crawler.Sites;

public sealed class AmazonScraper : BaseScraper
{
    public AmazonScraper(RateLimitedFetcher fetcher) : base(fetcher) { }

    public override string SiteName => "amazon";
    public override string Domain   => "www.amazon.com.br";

    public override async Task<List<ListingSnapshot>> ScrapeAsync(
        string searchTerm, int maxResults, CancellationToken ct = default)
    {
        var q   = Uri.EscapeDataString(searchTerm);
        var url = $"https://www.amazon.com.br/s?k={q}";
        var html = await Fetcher.FetchHtmlAsync(url, Domain,
            waitSelector: "[data-component-type='s-search-result']", ct: ct);

        var doc   = await Parser.ParseDocumentAsync(html, ct);
        var items = doc.QuerySelectorAll("[data-component-type='s-search-result']");
        var results = new List<ListingSnapshot>();

        foreach (var item in items.Take(maxResults * 2))
        {
            var asin = item.GetAttribute("data-asin");
            if (string.IsNullOrWhiteSpace(asin)) continue;

            var titleEl   = item.QuerySelector("h2 span");
            var linkEl    = item.QuerySelector("h2 a");
            var priceWhole = item.QuerySelector(".a-price-whole");
            var priceFrac  = item.QuerySelector(".a-price-fraction");
            var origEl    = item.QuerySelector(".a-price.a-text-price span.a-offscreen");
            var imgEl     = item.QuerySelector("img.s-image");
            var ratingEl  = item.QuerySelector("span.a-icon-alt");
            var reviewEl  = item.QuerySelector("span[aria-label]");

            var title = Text(titleEl);
            var href  = Attr(linkEl, "href");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var priceStr = Text(priceWhole);
            if (!string.IsNullOrEmpty(priceStr) && !string.IsNullOrEmpty(Text(priceFrac)))
                priceStr += "." + Text(priceFrac);

            double? rating = null;
            if (Text(ratingEl) is { } rStr)
            {
                var num = System.Text.RegularExpressions.Regex.Match(rStr, @"[\d,]+");
                if (num.Success && double.TryParse(num.Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var rv))
                    rating = rv;
            }

            results.Add(new ListingSnapshot(
                Site:          SiteName,
                SiteId:        asin,
                Title:         title,
                Url:           href is null ? $"https://www.amazon.com.br/dp/{asin}"
                                            : "https://www.amazon.com.br" + href.Split('?')[0],
                Price:         ParseBrl(priceStr),
                OriginalPrice: ParseBrl(Text(origEl)),
                Rating:        rating,
                ImageUrl:      Attr(imgEl, "src"),
                FetchedAt:     DateTime.UtcNow));
        }

        return results;
    }
}

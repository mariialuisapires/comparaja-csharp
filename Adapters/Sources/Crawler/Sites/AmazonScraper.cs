using System.Text.RegularExpressions;
using ComparadorPrecos.Domain;

namespace ComparadorPrecos.Adapters.Sources.Crawler.Sites;

public sealed partial class AmazonScraper : BaseScraper
{
    [GeneratedRegex(@"/dp/([A-Z0-9]{10})", RegexOptions.IgnoreCase)]
    private static partial Regex AsinRegex();

    [GeneratedRegex(@"[\d,]+")]
    private static partial Regex RatingNumRegex();

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

            var titleEl    = item.QuerySelector("h2 span");
            var linkEl     = item.QuerySelector("h2 a");
            var priceWhole = item.QuerySelector(".a-price-whole");
            var priceFrac  = item.QuerySelector(".a-price-fraction");
            var origEl     = item.QuerySelector(".a-price.a-text-price span.a-offscreen");
            var imgEl      = item.QuerySelector("img.s-image");
            var ratingEl   = item.QuerySelector("span.a-icon-alt");

            var title = Text(titleEl);
            var href  = Attr(linkEl, "href");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var priceStr = Text(priceWhole);
            if (!string.IsNullOrEmpty(priceStr) && !string.IsNullOrEmpty(Text(priceFrac)))
                priceStr += "." + Text(priceFrac);

            double? rating = null;
            if (Text(ratingEl) is { } rStr)
            {
                var num = RatingNumRegex().Match(rStr);
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

    public override async Task<ListingSnapshot?> FetchPriceFromUrlAsync(
        string url, CancellationToken ct = default)
    {
        // Tenta extrair ASIN direto da URL; se não achar (ex: link curto a.co),
        // navega a URL com Playwright e extrai do HTML da página de destino.
        var m = AsinRegex().Match(url);
        string? asin = m.Success ? m.Groups[1].Value : null;

        var fetchUrl  = asin is not null ? $"https://www.amazon.com.br/dp/{asin}" : url;
        var html = await Fetcher.FetchHtmlAsync(fetchUrl, Domain,
            waitSelector: "#productTitle", ct: ct);
        var doc = await Parser.ParseDocumentAsync(html, ct);

        if (asin is null)
        {
            // Tenta extrair ASIN da URL canônica ou do campo hidden da página
            var canonical = Attr(doc.QuerySelector("link[rel='canonical']"), "href");
            var mc = canonical is not null ? AsinRegex().Match(canonical) : default;
            if (mc.Success)
                asin = mc.Groups[1].Value;
            else
                asin = Attr(doc.QuerySelector("input[name='ASIN']"), "value")
                    ?? Attr(doc.QuerySelector("[data-asin]"), "data-asin");

            if (string.IsNullOrWhiteSpace(asin)) return null;
        }

        var title = doc.QuerySelector("#productTitle")?.TextContent?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;

        var priceWhole = Text(doc.QuerySelector(".a-price-whole"));
        var priceFrac  = Text(doc.QuerySelector(".a-price-fraction"));
        var priceStr   = priceWhole;
        if (!string.IsNullOrEmpty(priceStr) && !string.IsNullOrEmpty(priceFrac))
            priceStr += "." + priceFrac;
        if (string.IsNullOrEmpty(priceStr))
            priceStr = Text(doc.QuerySelector("#priceblock_ourprice,#priceblock_dealprice,#priceblock_saleprice"));

        var origText = Text(doc.QuerySelector(".a-price.a-text-price .a-offscreen"));
        var imgSrc   = Attr(doc.QuerySelector("#landingImage,#imgBlkFront"), "src");

        return new ListingSnapshot(
            Site:          SiteName,
            SiteId:        asin,
            Title:         title,
            Url:           $"https://www.amazon.com.br/dp/{asin}",
            Price:         ParseBrl(priceStr),
            OriginalPrice: ParseBrl(origText),
            ImageUrl:      imgSrc,
            MatchScore:    100.0,
            FetchedAt:     DateTime.UtcNow);
    }
}

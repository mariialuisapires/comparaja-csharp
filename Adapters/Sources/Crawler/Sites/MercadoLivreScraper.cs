using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using ComparadorPrecos.Domain;

namespace ComparadorPrecos.Adapters.Sources.Crawler.Sites;

public sealed partial class MercadoLivreScraper : BaseScraper
{
    // Handles MLB4699260287, MLBU3873817824, MLB-4699260287
    [GeneratedRegex(@"MLB[U]?-?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MlbRegex();

    public MercadoLivreScraper(RateLimitedFetcher fetcher) : base(fetcher) { }

    public override string SiteName => "mercadolivre";
    public override string Domain   => "lista.mercadolivre.com.br";

    public override async Task<List<ListingSnapshot>> ScrapeAsync(
        string searchTerm, int maxResults, CancellationToken ct = default)
    {
        var q   = Uri.EscapeDataString(searchTerm);
        var url = $"https://lista.mercadolivre.com.br/{q}";
        var html = await Fetcher.FetchHtmlAsync(url, Domain,
            waitSelector: ".ui-search-results", ct: ct);

        var doc   = await Parser.ParseDocumentAsync(html, ct);
        var items = doc.QuerySelectorAll(".ui-search-result__wrapper");
        var results = new List<ListingSnapshot>();

        foreach (var item in items.Take(maxResults * 2))
        {
            var linkEl   = item.QuerySelector("a.poly-component__title");
            var titleEl  = linkEl ?? item.QuerySelector(".poly-component__title");
            var priceEl  = item.QuerySelector(".poly-price__current .andes-money-amount__fraction");
            var origEl   = item.QuerySelector(".poly-price__old-value .andes-money-amount__fraction");
            var imgEl    = item.QuerySelector("img.poly-component__picture");
            var sellerEl = item.QuerySelector(".poly-component__seller-info");

            var title = Text(titleEl);
            var href  = Attr(linkEl, "href");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href)) continue;

            var m = MlbRegex().Match(href);
            if (!m.Success) continue;
            var siteId = "MLB" + m.Groups[1].Value;

            results.Add(new ListingSnapshot(
                Site:          SiteName,
                SiteId:        siteId,
                Title:         title,
                Url:           href.Split('?')[0],
                Price:         ParseBrl(Text(priceEl)),
                OriginalPrice: ParseBrl(Text(origEl)),
                Seller:        Text(sellerEl),
                ImageUrl:      Attr(imgEl, "src") ?? Attr(imgEl, "data-src"),
                FetchedAt:     DateTime.UtcNow));
        }

        return results;
    }

    public override async Task<ListingSnapshot?> FetchPriceFromUrlAsync(
        string url, CancellationToken ct = default)
    {
        var m = MlbRegex().Match(url);
        if (!m.Success) return null;
        var siteId   = "MLB" + m.Groups[1].Value;
        var cleanUrl = url.Split('#')[0].Split('?')[0];
        var domain   = ExtractDomain(cleanUrl);

        var html = await Fetcher.FetchHtmlAsync(cleanUrl, domain,
            waitSelector: ".ui-pdp-title", ct: ct);
        var doc = await Parser.ParseDocumentAsync(html, ct);

        // Try multiple title selectors — pdp pages and catalog pages differ
        var title = Text(doc.QuerySelector(".ui-pdp-title"))
                 ?? Text(doc.QuerySelector("h1.ui-pdp-title"))
                 ?? Text(doc.QuerySelector("h1"));
        if (string.IsNullOrWhiteSpace(title)) return null;

        // Price: pdp listing page vs catalog aggregate page selectors
        var priceEl = Text(doc.QuerySelector(".ui-pdp-price__second-line .andes-money-amount__fraction"))
                   ?? Text(doc.QuerySelector(".ui-pdp-price .andes-money-amount__fraction"))
                   ?? Text(doc.QuerySelector(".andes-money-amount__fraction"));
        var centEl  = Text(doc.QuerySelector(".ui-pdp-price__second-line .andes-money-amount__cents"))
                   ?? Text(doc.QuerySelector(".andes-money-amount__cents"));
        var priceStr = priceEl is null ? null
            : string.IsNullOrEmpty(centEl) ? priceEl
            : priceEl + "," + centEl;

        var origEl  = Text(doc.QuerySelector(".ui-pdp-price__original-value .andes-money-amount__fraction"));
        var imgSrc  = Attr(doc.QuerySelector(".ui-pdp-gallery__figure img"), "src")
                   ?? Attr(doc.QuerySelector(".ui-pdp-image"), "src");
        var seller  = Text(doc.QuerySelector(".ui-pdp-seller__link-trigger"));

        return new ListingSnapshot(
            Site:          SiteName,
            SiteId:        siteId,
            Title:         title,
            Url:           cleanUrl,
            Price:         ParseBrl(priceStr),
            OriginalPrice: ParseBrl(origEl),
            Seller:        seller,
            ImageUrl:      imgSrc,
            MatchScore:    100.0,
            FetchedAt:     DateTime.UtcNow);
    }
}

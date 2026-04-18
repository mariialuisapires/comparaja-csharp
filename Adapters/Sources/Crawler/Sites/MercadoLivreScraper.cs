using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using ComparadorPrecos.Domain;

namespace ComparadorPrecos.Adapters.Sources.Crawler.Sites;

public sealed partial class MercadoLivreScraper : BaseScraper
{
    [GeneratedRegex(@"MLB-?(\d+)", RegexOptions.IgnoreCase)]
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
}

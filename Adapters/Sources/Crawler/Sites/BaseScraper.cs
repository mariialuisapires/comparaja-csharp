using AngleSharp.Html.Parser;
using AngleSharp.Dom;
using ComparadorPrecos.Domain;

namespace ComparadorPrecos.Adapters.Sources.Crawler.Sites;

public abstract class BaseScraper
{
    protected readonly RateLimitedFetcher Fetcher;
    protected readonly HtmlParser Parser = new();

    protected BaseScraper(RateLimitedFetcher fetcher) => Fetcher = fetcher;

    public abstract string SiteName { get; }
    public abstract string Domain   { get; }

    public abstract Task<List<ListingSnapshot>> ScrapeAsync(
        string searchTerm, int maxResults, CancellationToken ct = default);

    public virtual Task<ListingSnapshot?> FetchPriceFromUrlAsync(
        string url, CancellationToken ct = default)
        => Task.FromResult<ListingSnapshot?>(null);

    protected static string ExtractDomain(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }

    protected static decimal? ParseBrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = System.Text.RegularExpressions.Regex.Replace(text, @"[^\d,]", "");
        clean = clean.Replace(",", ".");
        if (clean.Count(c => c == '.') > 1)
        {
            var last = clean.LastIndexOf('.');
            clean = clean[..last].Replace(".", "") + clean[last..];
        }
        return decimal.TryParse(clean, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    protected static string? Attr(IElement? el, string attr) =>
        el?.GetAttribute(attr)?.Trim();

    protected static string? Text(IElement? el) =>
        el?.TextContent?.Trim();
}

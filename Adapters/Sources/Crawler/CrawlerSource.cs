using ComparadorPrecos.Adapters.Sources.Crawler.Sites;
using ComparadorPrecos.Domain;
using ComparadorPrecos.Ports;

namespace ComparadorPrecos.Adapters.Sources.Crawler;

public sealed class CrawlerSource : IPriceSource
{
    private readonly List<BaseScraper> _scrapers;

    public CrawlerSource(RateLimitedFetcher fetcher, IEnumerable<string> sites)
    {
        _scrapers = new List<BaseScraper>();
        foreach (var s in sites)
        {
            _scrapers.Add(s switch
            {
                "mercadolivre" => new MercadoLivreScraper(fetcher),
                "amazon"       => new AmazonScraper(fetcher),
                "magalu"       => new MagaluScraper(fetcher),
                _ => throw new ArgumentException($"Site desconhecido: {s}")
            });
        }
    }

    public string Name => "crawler";

    public async Task<List<ListingSnapshot>> SearchAsync(
        ProductQuery query, int maxResults = 5, CancellationToken ct = default)
    {
        var term = query.Name;
        var results = new List<ListingSnapshot>();

        foreach (var scraper in _scrapers)
        {
            try
            {
                var snaps = await scraper.ScrapeAsync(term, maxResults * 2, ct);

                var scored = snaps
                    .Select(s => s with { MatchScore = Matcher.ScoreMatch(term, s.Title) })
                    .OrderByDescending(s => s.MatchScore)
                    .Take(maxResults)
                    .ToList();

                results.AddRange(scored);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [{scraper.SiteName}] erro: {ex.Message}");
            }
        }

        return results;
    }
}

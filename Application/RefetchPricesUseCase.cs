using ComparadorPrecos.Adapters.Sources.Crawler;
using ComparadorPrecos.Adapters.Sources.Crawler.Sites;
using ComparadorPrecos.Domain;
using ComparadorPrecos.Ports;

namespace ComparadorPrecos.Application;

// Equivalente ao CrawlerService do spec Java:
// percorre todos os produtos existentes, re-busca preço em cada link confirmado
// e salva snapshot. Usa scrapers Playwright para ML/Amazon/Magalu;
// PriceFetcher (HTTP) como fallback para lojas sem scraper (Kabum, Americanas).
public sealed class RefetchPricesUseCase
{
    private readonly IProductRepository         _repo;
    private readonly PriceFetcher               _fetcher;
    private readonly IReadOnlyList<BaseScraper> _scrapers;

    public RefetchPricesUseCase(IProductRepository repo, PriceFetcher fetcher,
        IReadOnlyList<BaseScraper> scrapers)
    {
        _repo     = repo;
        _fetcher  = fetcher;
        _scrapers = scrapers;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var products = _repo.ListProductsSummary();
        Console.WriteLine($"[refetch] {products.Count} produto(s)");

        foreach (var summary in products)
        {
            ct.ThrowIfCancellationRequested();
            Console.WriteLine($"Verificando: {summary.DisplayName}");

            var listings  = _repo.GetListingsWithCurrentPrice(summary.Id);
            decimal? minPrice = null;
            string?  minSite  = null;

            foreach (var listing in listings)
            {
                ct.ThrowIfCancellationRequested();
                Console.WriteLine($"  Buscando em {listing.Site}...");

                var scraper = _scrapers.FirstOrDefault(s => s.SiteName == listing.Site);
                ListingSnapshot? snap;
                if (scraper is not null)
                {
                    snap = await scraper.FetchPriceFromUrlAsync(listing.Url, ct);
                    if (snap is null)
                        snap = await _fetcher.FetchAsync(listing.Url, listing.Site, ct);
                }
                else
                    snap = await _fetcher.FetchAsync(listing.Url, listing.Site, ct);

                if (snap?.Price is null)
                {
                    Console.WriteLine($"  Não encontrado.");
                    continue;
                }

                Console.WriteLine($"  {listing.Site}: R$ {snap.Price:N2}");

                // Reconstrói Listing apenas com os campos usados por AddPriceSnapshot (Id)
                var domainListing = new Listing(
                    listing.Id, Guid.Empty, listing.Site, listing.SiteId,
                    listing.Title, listing.Url, listing.Seller, listing.ImageUrl,
                    listing.MatchScore, listing.LinkStatus,
                    DateTime.UtcNow, DateTime.UtcNow);

                _repo.AddPriceSnapshot(domainListing, snap);

                if (minPrice is null || snap.Price < minPrice)
                {
                    minPrice = snap.Price;
                    minSite  = listing.Site;
                }
            }

            if (minPrice is not null)
                Console.WriteLine($"  Menor preço: R$ {minPrice:N2} na {minSite}");
        }

        Console.WriteLine("[refetch] concluído.");
    }
}

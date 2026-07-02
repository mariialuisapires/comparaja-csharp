using System.Text.Json;
using ComparadorPrecos.Adapters.Sources.Crawler;
using ComparadorPrecos.Adapters.Sources.Crawler.Sites;
using ComparadorPrecos.Domain;
using ComparadorPrecos.Ports;

namespace ComparadorPrecos.Application;

public sealed class TrackLinksUseCase
{
    private readonly IProductRepository         _repo;
    private readonly IReadOnlyList<BaseScraper> _scrapers;
    private readonly PriceFetcher?              _fallback;

    public TrackLinksUseCase(IProductRepository repo, IReadOnlyList<BaseScraper> scrapers,
        PriceFetcher? fallback = null)
    {
        _repo     = repo;
        _scrapers = scrapers;
        _fallback = fallback;
    }

    public async Task ExecuteAsync(string jsonPath, CancellationToken ct = default)
    {
        var entries = LoadJson(jsonPath);
        Console.WriteLine($"[track-links] {entries.Count} produto(s)");

        for (int i = 0; i < entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = entries[i];
            Console.WriteLine($"  [{i + 1}/{entries.Count}] {entry.Nome}");

            var query   = new ProductQuery(entry.Nome, Category: entry.Categoria);
            var product = _repo.UpsertProduct(query);

            foreach (var link in entry.Links)
            {
                ct.ThrowIfCancellationRequested();
                Console.WriteLine($"    [{link.Loja}] {link.Url}");
                try
                {
                    var scraper = _scrapers.FirstOrDefault(s => s.SiteName == link.Loja);
                    ListingSnapshot? snap;

                    if (scraper is not null)
                    {
                        snap = await scraper.FetchPriceFromUrlAsync(link.Url, ct);
                        // Se o scraper Playwright foi bloqueado, tenta HTTP simples como fallback
                        if (snap is null && _fallback is not null)
                        {
                            Console.Error.WriteLine($"    [{link.Loja}] tentando HTTP como fallback...");
                            snap = await _fallback.FetchAsync(link.Url, link.Loja, ct);
                        }
                    }
                    else if (_fallback is not null)
                    {
                        snap = await _fallback.FetchAsync(link.Url, link.Loja, ct);
                    }
                    else
                    {
                        Console.Error.WriteLine($"    Loja desconhecida: {link.Loja}");
                        continue;
                    }

                    if (snap is null)
                    {
                        if (scraper is not null)
                        {
                            // Salva listing sem preço para gestão manual no admin
                            var seg  = new Uri(link.Url).AbsolutePath.TrimEnd('/').Split('/');
                            var sid  = seg.LastOrDefault(s => s.Length >= 4 && s.All(char.IsDigit))
                                    ?? Math.Abs(link.Url.GetHashCode()).ToString("x8");
                            var slug = seg.FirstOrDefault(s => s.Length > 10 && s.Any(char.IsLetter) && !s.Contains('.'));
                            var ttl  = slug is not null
                                ? System.Text.RegularExpressions.Regex.Replace(slug, @"[-_]+", " ").Trim()
                                : $"{link.Loja} {sid}";
                            snap = new ListingSnapshot(Site: link.Loja, SiteId: sid, Title: ttl,
                                Url: link.Url, Price: null, FetchedAt: DateTime.UtcNow, MatchScore: 100.0);
                            Console.Error.WriteLine($"    [{link.Loja}] salvo sem preço — defina manualmente no admin");
                        }
                        else
                        {
                            Console.Error.WriteLine($"    [{link.Loja}] não foi possível extrair preço");
                            continue;
                        }
                    }

                    var listing  = _repo.UpsertListing(product, snap);
                    if (snap.Price.HasValue)
                        _repo.AddPriceSnapshot(listing, snap);
                    var priceStr = snap.Price.HasValue ? $"R$ {snap.Price:N2}" : "(sem preço — defina no admin)";
                    Console.WriteLine($"    [{link.Loja}] {priceStr} — {snap.Title[..Math.Min(60, snap.Title.Length)]}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"    [{link.Loja}] ERRO: {ex.Message}");
                }
            }
        }
        Console.WriteLine("[track-links] concluído.");
    }

    private static List<ProductEntry> LoadJson(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"JSON não encontrado: {path}");
        var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
        return JsonSerializer.Deserialize<List<ProductEntry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("JSON inválido ou vazio.");
    }

    private sealed record ProductEntry(
        string        Nome,
        string?       Categoria,
        List<LinkEntry> Links);

    private sealed record LinkEntry(string Loja, string Url);
}

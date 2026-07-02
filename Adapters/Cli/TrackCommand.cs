using ComparadorPrecos.Adapters.Sources.Crawler;
using ComparadorPrecos.Adapters.Sources.Crawler.Sites;
using ComparadorPrecos.Adapters.Storage.Sqlite;
using ComparadorPrecos.Application;

namespace ComparadorPrecos.Adapters.Cli;

public static class TrackCommand
{
    public static async Task RunAsync(string[] args)
    {
        var input    = GetOpt(args, "--input",     "-i") ?? "products.json";
        var db       = GetOpt(args, "--db")              ?? "data/comparador.db";
        var topStr   = GetOpt(args, "--top")             ?? "5";
        var sitesStr = GetOpt(args, "--sites")           ?? "mercadolivre,amazon,magalu";
        var minDelay = double.Parse(GetOpt(args, "--min-delay") ?? "3.0",
            System.Globalization.CultureInfo.InvariantCulture);
        var maxDelay = double.Parse(GetOpt(args, "--max-delay") ?? "8.0",
            System.Globalization.CultureInfo.InvariantCulture);
        var headful  = args.Contains("--headful");
        var refetch  = args.Contains("--refetch");
        var top      = int.Parse(topStr);
        var sites    = sitesStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim()).ToArray();

        using var repo = new SqliteProductRepository(db);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Modos que precisam do browser (Playwright)
        await using var browserFetcher = await RateLimitedFetcher.CreateAsync(headful, minDelay, maxDelay);

        // Modo --refetch: re-busca preços de todos os listings existentes
        // Usa Playwright para ML/Amazon/Magalu; HTTP simples para Kabum/Americanas
        if (refetch)
        {
            using var fetcher = new PriceFetcher();
            var scrapers = BuildScrapers(browserFetcher, sites);
            var useCase  = new RefetchPricesUseCase(repo, fetcher, scrapers);
            await useCase.ExecuteAsync(cts.Token);
            return;
        }

        if (input.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var scrapers     = BuildScrapers(browserFetcher, sites);
            using var priceFetcher = new PriceFetcher();   // fallback para lojas sem scraper
            var useCase      = new TrackLinksUseCase(repo, scrapers, priceFetcher);
            await useCase.ExecuteAsync(input, cts.Token);
        }
        else
        {
            var source  = new CrawlerSource(browserFetcher, sites);
            var useCase = new TrackPricesUseCase(repo, [source]);
            await useCase.ExecuteAsync(input, top, cts.Token);
        }
    }

    private static List<BaseScraper> BuildScrapers(RateLimitedFetcher fetcher, string[] sites) =>
        sites.Select<string, BaseScraper>(s => s switch
        {
            "mercadolivre" => new MercadoLivreScraper(fetcher),
            "amazon"       => new AmazonScraper(fetcher),
            "magalu"       => new MagaluScraper(fetcher),
            _              => throw new ArgumentException($"Site desconhecido: {s}")
        }).ToList();

    private static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (names.Contains(args[i])) return args[i + 1];
        return null;
    }
}

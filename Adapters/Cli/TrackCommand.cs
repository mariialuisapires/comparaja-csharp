using ComparadorPrecos.Adapters.Sources.Crawler;
using ComparadorPrecos.Adapters.Storage.Sqlite;
using ComparadorPrecos.Application;

namespace ComparadorPrecos.Adapters.Cli;

public static class TrackCommand
{
    public static async Task RunAsync(string[] args)
    {
        var csv      = GetOpt(args, "--input",     "-i") ?? "products.csv";
        var db       = GetOpt(args, "--db")              ?? "data/comparador.db";
        var topStr   = GetOpt(args, "--top")             ?? "5";
        var sitesStr = GetOpt(args, "--sites")           ?? "mercadolivre,amazon,magalu";
        var minDelay = double.Parse(GetOpt(args, "--min-delay") ?? "3.0",
            System.Globalization.CultureInfo.InvariantCulture);
        var maxDelay = double.Parse(GetOpt(args, "--max-delay") ?? "8.0",
            System.Globalization.CultureInfo.InvariantCulture);
        var headful  = args.Contains("--headful");
        var top      = int.Parse(topStr);
        var sites    = sitesStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim()).ToArray();

        using var repo = new SqliteProductRepository(db);
        await using var fetcher = await RateLimitedFetcher.CreateAsync(headful, minDelay, maxDelay);
        var source  = new CrawlerSource(fetcher, sites);
        var useCase = new TrackPricesUseCase(repo, [source]);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await useCase.ExecuteAsync(csv, top, cts.Token);
    }

    private static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (names.Contains(args[i])) return args[i + 1];
        return null;
    }
}

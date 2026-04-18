using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using ComparadorPrecos.Domain;
using ComparadorPrecos.Ports;

namespace ComparadorPrecos.Application;

public sealed class TrackPricesUseCase
{
    private readonly IProductRepository       _repo;
    private readonly IReadOnlyList<IPriceSource> _sources;

    public TrackPricesUseCase(IProductRepository repo, IReadOnlyList<IPriceSource> sources)
    {
        _repo    = repo;
        _sources = sources;
    }

    public async Task ExecuteAsync(string csvPath, int topPerSource = 5, CancellationToken ct = default)
    {
        var queries = LoadCsv(csvPath);
        Console.WriteLine($"[track] {queries.Count} produtos, {_sources.Count} fontes");

        for (int i = 0; i < queries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var q = queries[i];
            Console.WriteLine($"  [{i + 1}/{queries.Count}] {q.Name}");
            var product = _repo.UpsertProduct(q);

            foreach (var source in _sources)
            {
                Console.WriteLine($"    [{source.Name}] buscando...");
                try
                {
                    var snaps = await source.SearchAsync(q, topPerSource, ct);
                    Console.WriteLine($"    [{source.Name}] {snaps.Count} resultados");
                    foreach (var snap in snaps)
                    {
                        if (ProductIdentity.LinkStatusForScore(snap.MatchScore) is null) continue;
                        var listing = _repo.UpsertListing(product, snap);
                        _repo.AddPriceSnapshot(listing, snap);
                        var priceStr = snap.Price.HasValue ? $"R$ {snap.Price:N2}" : "—";
                        Console.WriteLine($"      {snap.Site}/{snap.SiteId} [{listing.LinkStatus}] {priceStr} match={snap.MatchScore:F0} — {snap.Title[..Math.Min(60, snap.Title.Length)]}");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"    [{source.Name}] ERRO: {ex.Message}");
                }
            }
        }
        Console.WriteLine("[track] concluído.");
    }

    private static List<ProductQuery> LoadCsv(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"CSV não encontrado: {path}");
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord    = true,
            MissingFieldFound  = null,
            HeaderValidated    = null,
            PrepareHeaderForMatch = args => args.Header.ToLowerInvariant(),
        });
        return csv.GetRecords<CsvRow>()
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new ProductQuery(
                r.Name.Trim(),
                string.IsNullOrWhiteSpace(r.ReferenceModel) ? null : r.ReferenceModel.Trim(),
                string.IsNullOrWhiteSpace(r.Notes)          ? null : r.Notes.Trim(),
                string.IsNullOrWhiteSpace(r.Category)       ? null : r.Category.Trim()))
            .ToList();
    }

    private sealed class CsvRow
    {
        [Name("name")]             public string Name           { get; set; } = "";
        [Name("reference_model")] public string ReferenceModel { get; set; } = "";
        [Name("notes")]            public string Notes          { get; set; } = "";
        [Name("category")]         public string Category       { get; set; } = "";
    }
}

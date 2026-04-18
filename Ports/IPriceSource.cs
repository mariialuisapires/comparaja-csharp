using ComparadorPrecos.Domain;

namespace ComparadorPrecos.Ports;

public interface IPriceSource
{
    string Name { get; }
    Task<List<ListingSnapshot>> SearchAsync(
        ProductQuery query,
        int maxResults      = 5,
        CancellationToken ct = default);
}

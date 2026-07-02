using ComparadorPrecos.Domain;

namespace ComparadorPrecos.Ports;

// ── Query result records ─────────────────────────────────────────────────────

public sealed record ProductPublicItem(
    Guid     Id,
    string   DisplayName,
    decimal? BestPrice,
    string?  BestSite,
    int      StoreCount,
    string?  ImageUrl,
    string?  Category = null);

public sealed record ProductSummaryItem(
    Guid     Id,
    string   DisplayName,
    int      ListingCount,
    decimal? BestPrice,
    string?  BestSite,
    string?  LastUpdated,
    string?  Category = null);

public sealed record ListingComparisonItem(
    Guid     Id,
    string   Site,
    string   SiteId,
    string   Title,
    string   Url,
    string?  Seller,
    string?  ImageUrl,
    decimal? CurrentPrice,
    decimal? OriginalPrice);

public sealed record ListingAdminItem(
    Guid     Id,
    string   Site,
    string   SiteId,
    string   Title,
    string   Url,
    string?  Seller,
    string?  ImageUrl,
    double   MatchScore,
    string   LinkStatus,
    decimal? CurrentPrice,
    decimal? OriginalPrice,
    string?  LastFetchedAt);

public sealed record PricePoint(string X, decimal Y);

// ── Port ─────────────────────────────────────────────────────────────────────

public interface IProductRepository
{
    // Commands
    Product  UpsertProduct(ProductQuery query);
    Listing  UpsertListing(Product product, ListingSnapshot snap);
    void     AddPriceSnapshot(Listing listing, ListingSnapshot snap);
    void     SetListingStatus(Guid listingId, string status);
    void     SetManualPrice(Guid listingId, decimal price);

    // Admin queries
    IReadOnlyList<ProductSummaryItem>    ListProductsSummary(string? search = null);
    Product?                             GetProduct(Guid productId);
    IReadOnlyList<ListingAdminItem>      GetListingsWithCurrentPrice(Guid productId);
    Dictionary<string, List<PricePoint>> GetPriceHistory(Guid productId);

    // Public queries
    IReadOnlyList<ProductPublicItem>     ListProductsPublicView(string? search = null);
    IReadOnlyList<ProductPublicItem>     ListProductsByCategory(
        string category, string? brand = null,
        decimal? minPrice = null, decimal? maxPrice = null,
        string sort = "price_asc");
    IReadOnlyList<ListingComparisonItem> GetListingsForComparison(Guid productId);
}

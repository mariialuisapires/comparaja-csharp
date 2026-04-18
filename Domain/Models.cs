namespace ComparadorPrecos.Domain;

public static class Thresholds
{
    public const double AutoLink     = 85.0;
    public const double ManualReview = 55.0;
}

public sealed record ProductQuery(
    string  Name,
    string? ReferenceModel = null,
    string? Notes          = null,
    string? Category       = null
);

public sealed record ListingSnapshot(
    string   Site,
    string   SiteId,
    string   Title,
    string   Url,
    decimal? Price,
    decimal? OriginalPrice = null,
    string   Currency      = "BRL",
    string?  Seller        = null,
    string?  ImageUrl      = null,
    double?  Rating        = null,
    int?     ReviewsCount  = null,
    string?  Availability  = null,
    double   MatchScore    = 0.0,
    DateTime? FetchedAt   = null
);

public sealed record Product(
    Guid     Id,
    string   Name,
    string   DisplayName,
    string?  ReferenceModel,
    string?  Notes,
    string?  Category,
    DateTime CreatedAt
);

public sealed record Listing(
    Guid     Id,
    Guid     ProductId,
    string   Site,
    string   SiteId,
    string   Title,
    string   Url,
    string?  Seller,
    string?  ImageUrl,
    double   MatchScore,
    string   LinkStatus,
    DateTime FirstSeenAt,
    DateTime LastSeenAt
);

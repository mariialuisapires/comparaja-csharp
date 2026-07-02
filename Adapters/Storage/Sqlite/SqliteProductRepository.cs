using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using ComparadorPrecos.Domain;
using ComparadorPrecos.Ports;

namespace ComparadorPrecos.Adapters.Storage.Sqlite;

public sealed class SqliteProductRepository : IProductRepository, IDisposable
{
    private readonly SqliteConnection _db;

    public SqliteProductRepository(string dbPath)
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        _db.Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        ApplySchema();
    }

    // ── Schema ───────────────────────────────────────────────────────────────

    private void ApplySchema()
    {
        var sql = ReadEmbeddedSql("schema.sql");
        _db.Execute(sql);

        // Idempotent column migrations
        var cols = _db.Query<string>("SELECT name FROM pragma_table_info('products')").ToHashSet();
        if (!cols.Contains("category"))
            _db.Execute("ALTER TABLE products ADD COLUMN category TEXT NOT NULL DEFAULT ''");

        // Remove listings cujo id não tem formato GUID (dados de versões anteriores do código)
        _db.Execute("""
            DELETE FROM price_snapshots
            WHERE listing_id NOT LIKE '________-____-____-____-____________';
            DELETE FROM listings
            WHERE id NOT LIKE '________-____-____-____-____________';
            """);

        // Corrige slugs de categorias renomeadas
        _db.Execute("UPDATE products SET category = 'console'        WHERE category IN ('videogame','game','games')");
        _db.Execute("UPDATE products SET category = 'placa-de-video' WHERE category IN ('hardware','gpu','graphics')");

        // Atribui categoria a produtos sem categoria pelo nome
        _db.Execute("UPDATE products SET category = 'console'        WHERE category = '' AND lower(name) LIKE '%playstation%'");
        _db.Execute("UPDATE products SET category = 'console'        WHERE category = '' AND lower(name) LIKE '%xbox%'");
        _db.Execute("UPDATE products SET category = 'console'        WHERE category = '' AND lower(name) LIKE '%nintendo%'");
        _db.Execute("UPDATE products SET category = 'smartphone'     WHERE category = '' AND lower(name) LIKE '%iphone%'");
        _db.Execute("UPDATE products SET category = 'smartphone'     WHERE category = '' AND lower(name) LIKE '%samsung galaxy%'");
        _db.Execute("UPDATE products SET category = 'placa-de-video' WHERE category = '' AND lower(name) LIKE '%rtx%'");
        _db.Execute("UPDATE products SET category = 'placa-de-video' WHERE category = '' AND lower(name) LIKE '%radeon%'");

        // Remove produtos que ainda ficaram sem categoria ou fora das 3 categorias ativas
        _db.Execute("""
            DELETE FROM price_snapshots WHERE listing_id IN (
                SELECT l.id FROM listings l
                JOIN products p ON p.id = l.product_id
                WHERE p.category NOT IN ('smartphone','console','placa-de-video')
            );
            DELETE FROM listings WHERE product_id IN (
                SELECT id FROM products
                WHERE category NOT IN ('smartphone','console','placa-de-video')
            );
            DELETE FROM products WHERE category NOT IN ('smartphone','console','placa-de-video');
            """);
    }

    private static string ReadEmbeddedSql(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // GUIDs são armazenados em UPPERCASE pelo driver Microsoft.Data.Sqlite
    private static string G(Guid id) => id.ToString().ToUpperInvariant();

    // ── Commands ─────────────────────────────────────────────────────────────

    public Product UpsertProduct(ProductQuery query)
    {
        var canonical = ProductIdentity.CanonicalProductName(query.Name);
        var existing = _db.QueryFirstOrDefault<ProductRow>(
            "SELECT * FROM products WHERE name = @name", new { name = canonical });

        if (existing is not null)
            return existing.ToDomain();

        var product = new Product(
            Guid.NewGuid(), canonical, query.Name,
            query.ReferenceModel, query.Notes, query.Category, DateTime.UtcNow);

        _db.Execute("""
            INSERT INTO products(id, name, display_name, reference_model, notes, category, created_at)
            VALUES (@Id, @Name, @DisplayName, @ReferenceModel, @Notes, @Category, @CreatedAt)
            """,
            new { Id = G(product.Id), product.Name, DisplayName = product.DisplayName,
                  product.ReferenceModel, product.Notes, Category = product.Category ?? "",
                  CreatedAt = product.CreatedAt.ToString("O") });

        return product;
    }

    public Listing UpsertListing(Product product, ListingSnapshot snap)
    {
        var existing = _db.QueryFirstOrDefault<ListingRow>(
            """
            SELECT * FROM listings
            WHERE (site = @site AND site_id = @siteId)
               OR (product_id = @productId AND site = @site AND url = @url)
            LIMIT 1
            """,
            new { site = snap.Site, siteId = snap.SiteId, productId = G(product.Id), url = snap.Url });

        if (existing is not null)
        {
            var adminSet = existing.LinkStatus is "confirmed" or "rejected";
            // Use the latest score so re-runs can correct previously over-scored accessories
            var newScore = adminSet ? existing.MatchScore : snap.MatchScore;
            var newStatus = adminSet
                ? existing.LinkStatus
                : ProductIdentity.LinkStatusForScore(newScore) ?? existing.LinkStatus;

            _db.Execute("""
                UPDATE listings
                SET title=@title, url=@url, seller=@seller, image_url=@imageUrl,
                    match_score=@score, link_status=@status, last_seen_at=@now
                WHERE id=@id
                """,
                new { title = snap.Title, url = snap.Url, seller = snap.Seller,
                      imageUrl = snap.ImageUrl, score = newScore, status = newStatus,
                      now = DateTime.UtcNow.ToString("O"), id = existing.Id /* already uppercase from DB */ });

            return existing.ToDomain() with
            {
                Title = snap.Title, Url = snap.Url, Seller = snap.Seller,
                ImageUrl = snap.ImageUrl, MatchScore = newScore,
                LinkStatus = newStatus, LastSeenAt = DateTime.UtcNow
            };
        }

        var listing = new Listing(
            Guid.NewGuid(), product.Id, snap.Site, snap.SiteId,
            snap.Title, snap.Url, snap.Seller, snap.ImageUrl, snap.MatchScore,
            ProductIdentity.LinkStatusForScore(snap.MatchScore) ?? "pending",
            DateTime.UtcNow, DateTime.UtcNow);

        _db.Execute("""
            INSERT INTO listings(id, product_id, site, site_id, title, url, seller,
                image_url, match_score, link_status, first_seen_at, last_seen_at)
            VALUES (@Id, @ProductId, @Site, @SiteId, @Title, @Url, @Seller,
                @ImageUrl, @MatchScore, @LinkStatus, @FirstSeenAt, @LastSeenAt)
            """,
            new { Id = G(listing.Id), ProductId = G(listing.ProductId), listing.Site, listing.SiteId,
                  listing.Title, listing.Url, listing.Seller, listing.ImageUrl,
                  listing.MatchScore, listing.LinkStatus,
                  FirstSeenAt = listing.FirstSeenAt.ToString("O"),
                  LastSeenAt  = listing.LastSeenAt.ToString("O") });

        return listing;
    }

    public void AddPriceSnapshot(Listing listing, ListingSnapshot snap)
    {
        _db.Execute("""
            INSERT INTO price_snapshots(listing_id, price, original_price, currency, availability, fetched_at)
            VALUES (@listingId, @price, @originalPrice, @currency, @availability, @fetchedAt)
            """,
            new { listingId = G(listing.Id),
                  price = snap.Price.HasValue ? (object)snap.Price.Value : DBNull.Value,
                  originalPrice = snap.OriginalPrice.HasValue ? (object)snap.OriginalPrice.Value : DBNull.Value,
                  currency = snap.Currency,
                  availability = snap.Availability ?? (object)DBNull.Value,
                  fetchedAt = (snap.FetchedAt ?? DateTime.UtcNow).ToString("O") });
    }

    public void SetListingStatus(Guid listingId, string status)
    {
        var allowed = new[] { "auto", "pending", "confirmed", "rejected" };
        if (!allowed.Contains(status))
            throw new ArgumentException($"Status inválido: {status}");
        _db.Execute("UPDATE listings SET link_status=@status WHERE id=@id",
            new { status, id = G(listingId) });
    }

    public void SetManualPrice(Guid listingId, decimal price)
    {
        _db.Execute("""
            INSERT INTO price_snapshots(listing_id, price, currency, fetched_at)
            VALUES (@listingId, @price, 'BRL', @fetchedAt)
            """,
            new { listingId = G(listingId), price, fetchedAt = DateTime.UtcNow.ToString("O") });
        _db.Execute("UPDATE listings SET link_status='confirmed' WHERE id=@id",
            new { id = G(listingId) });
    }

    // ── Admin queries ─────────────────────────────────────────────────────────

    public IReadOnlyList<ProductSummaryItem> ListProductsSummary(string? search = null)
    {
        var where = string.IsNullOrWhiteSpace(search)
            ? ""
            : "WHERE lower(p.display_name) LIKE @like";

        var sql = $"""
            WITH latest_snap AS (
                SELECT listing_id, price, fetched_at,
                       ROW_NUMBER() OVER (PARTITION BY listing_id ORDER BY fetched_at DESC) AS snap_rn
                FROM price_snapshots
            ),
            latest_price AS (
                SELECT listing_id, price, fetched_at FROM latest_snap WHERE snap_rn = 1
            ),
            ranked AS (
                SELECT l.product_id,
                       lp.price,
                       l.site,
                       lp.fetched_at,
                       ROW_NUMBER() OVER (PARTITION BY l.product_id ORDER BY lp.price ASC) AS rn
                FROM listings l
                JOIN latest_price lp ON lp.listing_id = l.id
                WHERE l.link_status IN ('auto','confirmed') AND lp.price IS NOT NULL
            )
            SELECT p.id, p.display_name, p.category,
                   COUNT(DISTINCT l.id)         AS listing_count,
                   r.price                       AS best_price,
                   r.site                        AS best_site,
                   MAX(lp2.fetched_at)           AS last_updated
            FROM products p
            LEFT JOIN listings l       ON l.product_id = p.id AND l.link_status IN ('auto','confirmed')
            LEFT JOIN latest_price lp2 ON lp2.listing_id = l.id
            LEFT JOIN ranked r         ON r.product_id = p.id AND r.rn = 1
            {where}
            GROUP BY p.id, p.display_name, p.category, r.price, r.site
            ORDER BY p.display_name
            """;

        return _db.Query<ProductSummaryRow>(sql,
                search is null ? null : new { like = $"%{search.ToLower()}%" })
            .Select(r => new ProductSummaryItem(
                Guid.Parse(r.Id), r.DisplayName, r.ListingCount,
                r.BestPrice, r.BestSite, r.LastUpdated, r.Category))
            .ToList();
    }

    public Product? GetProduct(Guid productId)
    {
        var row = _db.QueryFirstOrDefault<ProductRow>(
            "SELECT * FROM products WHERE id = @id", new { id = G(productId) });
        return row?.ToDomain();
    }

    public IReadOnlyList<ListingAdminItem> GetListingsWithCurrentPrice(Guid productId)
    {
        var sql = """
            SELECT l.id, l.site, l.site_id, l.title, l.url, l.seller, l.image_url,
                   l.match_score, l.link_status,
                   ps.price         AS current_price,
                   ps.original_price,
                   ps.fetched_at    AS last_fetched_at
            FROM listings l
            LEFT JOIN price_snapshots ps ON ps.id = (
                SELECT id FROM price_snapshots
                WHERE listing_id = l.id
                ORDER BY fetched_at DESC LIMIT 1
            )
            WHERE l.product_id = @productId
            ORDER BY
                CASE l.link_status
                    WHEN 'confirmed' THEN 1
                    WHEN 'auto'      THEN 2
                    WHEN 'pending'   THEN 3
                    ELSE 4
                END,
                ps.price ASC NULLS LAST
            """;

        return _db.Query<ListingAdminRow>(sql, new { productId = G(productId) })
            .Select(r => new ListingAdminItem(
                Guid.Parse(r.Id), r.Site, r.SiteId, r.Title, r.Url,
                r.Seller, r.ImageUrl, r.MatchScore, r.LinkStatus,
                r.CurrentPrice, r.OriginalPrice, r.LastFetchedAt))
            .ToList();
    }

    public Dictionary<string, List<PricePoint>> GetPriceHistory(Guid productId)
    {
        var sql = """
            SELECT l.site AS title,
                   substr(ps.fetched_at, 1, 10) AS fetched_at,
                   MIN(ps.price) AS price
            FROM listings l
            JOIN price_snapshots ps ON ps.listing_id = l.id
            WHERE l.product_id = @productId
              AND l.link_status IN ('auto','confirmed')
              AND ps.price IS NOT NULL
            GROUP BY l.site, substr(ps.fetched_at, 1, 10)
            ORDER BY l.site, ps.fetched_at
            """;

        var rows = _db.Query<PriceHistoryRow>(sql, new { productId = G(productId) });

        return rows
            .GroupBy(r => r.Title)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new PricePoint(r.FetchedAt[..10], r.Price)).ToList());
    }

    // ── Public queries ────────────────────────────────────────────────────────

    public IReadOnlyList<ProductPublicItem> ListProductsPublicView(string? search = null)
    {
        var where = string.IsNullOrWhiteSpace(search)
            ? ""
            : "WHERE lower(p.display_name) LIKE @like";

        var sql = $"""
            WITH latest_snap AS (
                SELECT listing_id, price,
                       ROW_NUMBER() OVER (PARTITION BY listing_id ORDER BY fetched_at DESC) AS snap_rn
                FROM price_snapshots
            ),
            latest_price AS (
                SELECT listing_id, price FROM latest_snap WHERE snap_rn = 1
            ),
            ranked_prices AS (
                SELECT l.product_id, l.site, lp.price,
                       ROW_NUMBER() OVER (PARTITION BY l.product_id ORDER BY lp.price ASC) AS rn
                FROM listings l
                JOIN latest_price lp ON lp.listing_id = l.id
                WHERE l.link_status IN ('auto','confirmed') AND lp.price IS NOT NULL
            ),
            best AS (
                SELECT product_id, price AS best_price, site AS best_site
                FROM ranked_prices
                WHERE rn = 1
            ),
            img AS (
                SELECT product_id, image_url
                FROM listings
                WHERE link_status IN ('auto','confirmed') AND image_url IS NOT NULL
                GROUP BY product_id
                HAVING MAX(match_score)
            )
            SELECT p.id, p.display_name, p.category,
                   b.best_price, b.best_site,
                   COUNT(DISTINCT l2.site) AS store_count,
                   i.image_url
            FROM products p
            LEFT JOIN best b ON b.product_id = p.id
            LEFT JOIN img i ON i.product_id = p.id
            LEFT JOIN listings l2 ON l2.product_id = p.id AND l2.link_status IN ('auto','confirmed')
            {where}
            GROUP BY p.id, p.display_name, p.category, b.best_price, b.best_site, i.image_url
            ORDER BY b.best_price ASC NULLS LAST, p.display_name
            """;

        return _db.Query<ProductPublicRow>(sql,
                search is null ? null : new { like = $"%{search.ToLower()}%" })
            .Select(r => new ProductPublicItem(
                Guid.Parse(r.Id), r.DisplayName, r.BestPrice, r.BestSite,
                r.StoreCount, r.ImageUrl, string.IsNullOrEmpty(r.Category) ? null : r.Category))
            .ToList();
    }

    public IReadOnlyList<ProductPublicItem> ListProductsByCategory(
        string category, string? brand = null,
        decimal? minPrice = null, decimal? maxPrice = null,
        string sort = "price_asc")
    {
        var p = new DynamicParameters();
        p.Add("category", category);

        var conditions = new List<string> { "p.category = @category" };

        if (!string.IsNullOrWhiteSpace(brand))
        {
            var keywords = BrandKeywords(brand);
            var clauses  = keywords.Select((_, i) => $"lower(p.display_name) LIKE @bk{i}").ToList();
            conditions.Add("(" + string.Join(" OR ", clauses) + ")");
            for (int i = 0; i < keywords.Length; i++)
                p.Add($"bk{i}", $"%{keywords[i]}%");
        }

        if (minPrice.HasValue) { conditions.Add("b.best_price >= @minPrice"); p.Add("minPrice", minPrice); }
        if (maxPrice.HasValue) { conditions.Add("b.best_price <= @maxPrice"); p.Add("maxPrice", maxPrice); }

        var orderBy = sort switch
        {
            "price_desc" => "b.best_price DESC",
            "name_asc"   => "p.display_name ASC",
            _            => "b.best_price ASC",
        };

        var sql = $"""
            WITH latest_snap AS (
                SELECT listing_id, price,
                       ROW_NUMBER() OVER (PARTITION BY listing_id ORDER BY fetched_at DESC) AS snap_rn
                FROM price_snapshots
            ),
            latest_price AS (
                SELECT listing_id, price FROM latest_snap WHERE snap_rn = 1
            ),
            ranked_prices AS (
                SELECT l.product_id, l.site, lp.price,
                       ROW_NUMBER() OVER (PARTITION BY l.product_id ORDER BY lp.price ASC) AS rn
                FROM listings l
                JOIN latest_price lp ON lp.listing_id = l.id
                WHERE l.link_status IN ('auto','confirmed') AND lp.price IS NOT NULL
            ),
            best AS (
                SELECT product_id, price AS best_price, site AS best_site
                FROM ranked_prices
                WHERE rn = 1
            ),
            img AS (
                SELECT product_id, image_url
                FROM listings
                WHERE link_status IN ('auto','confirmed') AND image_url IS NOT NULL
                GROUP BY product_id
                HAVING MAX(match_score)
            )
            SELECT p.id, p.display_name, p.category,
                   b.best_price, b.best_site,
                   COUNT(DISTINCT l2.site) AS store_count,
                   i.image_url
            FROM products p
            LEFT JOIN best b ON b.product_id = p.id
            LEFT JOIN img i ON i.product_id = p.id
            LEFT JOIN listings l2 ON l2.product_id = p.id AND l2.link_status IN ('auto','confirmed')
            WHERE {string.Join(" AND ", conditions)}
            GROUP BY p.id, p.display_name, p.category, b.best_price, b.best_site, i.image_url
            ORDER BY {orderBy} NULLS LAST, p.display_name
            """;

        return _db.Query<ProductPublicRow>(sql, p)
            .Select(r => new ProductPublicItem(
                Guid.Parse(r.Id), r.DisplayName, r.BestPrice, r.BestSite,
                r.StoreCount, r.ImageUrl, string.IsNullOrEmpty(r.Category) ? null : r.Category))
            .ToList();
    }

    // Mapeia marca do filtro para keywords de busca no nome do produto.
    // Necessário porque produtos como "iPhone" não contêm a palavra "Apple" no nome.
    private static string[] BrandKeywords(string brand) => brand.ToLower() switch
    {
        "apple"     => ["apple", "iphone", "ipad", "macbook", "airpods"],
        "samsung"   => ["samsung"],
        "motorola"  => ["motorola", "moto"],
        "xiaomi"    => ["xiaomi", "redmi", "poco"],
        "lg"        => ["lg "],
        "nvidia"    => ["nvidia", "rtx", "gtx", "geforce"],
        "amd"       => ["amd", "radeon"],
        "gigabyte"  => ["gigabyte", "aorus"],
        "asus"      => ["asus", "rog", "tuf"],
        "msi"       => ["msi"],
        "sony"      => ["sony", "playstation", "ps5", "ps4"],
        "microsoft" => ["microsoft", "xbox"],
        "nintendo"  => ["nintendo", "switch"],
        _           => [brand.ToLower()],
    };

    public IReadOnlyList<ListingComparisonItem> GetListingsForComparison(Guid productId)
    {
        // Prioritise confirmed > auto; within each status, listings with a price beat
        // those without, and cheaper beats dearer. This ensures manually-verified links
        // are always shown even when the crawler has not fetched a price yet.
        var sql = """
            WITH latest_snap AS (
                SELECT listing_id, price, original_price,
                       ROW_NUMBER() OVER (PARTITION BY listing_id ORDER BY fetched_at DESC) AS snap_rn
                FROM price_snapshots
            ),
            latest_price AS (
                SELECT listing_id, price, original_price FROM latest_snap WHERE snap_rn = 1
            ),
            ranked AS (
                SELECT l.id, l.site, l.site_id, l.title, l.url, l.seller, l.image_url,
                       lp.price AS current_price, lp.original_price,
                       ROW_NUMBER() OVER (
                           PARTITION BY l.site
                           ORDER BY
                               CASE l.link_status WHEN 'confirmed' THEN 0 ELSE 1 END ASC,
                               CASE WHEN lp.price IS NOT NULL THEN 0 ELSE 1 END ASC,
                               lp.price ASC
                       ) AS rn
                FROM listings l
                LEFT JOIN latest_price lp ON lp.listing_id = l.id
                WHERE l.product_id = @productId
                  AND l.link_status IN ('auto','confirmed')
            )
            SELECT id, site, site_id, title, url, seller, image_url, current_price, original_price
            FROM ranked
            WHERE rn = 1
            ORDER BY
                CASE WHEN current_price IS NOT NULL THEN 0 ELSE 1 END ASC,
                current_price ASC
            """;

        return _db.Query<ComparisonRow>(sql, new { productId = G(productId) })
            .Select(r => new ListingComparisonItem(
                Guid.Parse(r.Id), r.Site, r.SiteId, r.Title, r.Url,
                r.Seller, r.ImageUrl, r.CurrentPrice, r.OriginalPrice))
            .ToList();
    }

    public void Dispose() => _db.Dispose();

    // ── Private row types ─────────────────────────────────────────────────────

    private sealed class ProductRow
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string? ReferenceModel { get; init; }
        public string? Notes { get; init; }
        public string? Category { get; init; }
        public string CreatedAt { get; init; } = "";

        public Product ToDomain() => new(
            Guid.Parse(Id), Name, DisplayName, ReferenceModel, Notes,
            string.IsNullOrEmpty(Category) ? null : Category,
            DateTime.Parse(CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    private sealed class ListingRow
    {
        public string Id { get; init; } = "";
        public string ProductId { get; init; } = "";
        public string Site { get; init; } = "";
        public string SiteId { get; init; } = "";
        public string Title { get; init; } = "";
        public string Url { get; init; } = "";
        public string? Seller { get; init; }
        public string? ImageUrl { get; init; }
        public double MatchScore { get; init; }
        public string LinkStatus { get; init; } = "";
        public string FirstSeenAt { get; init; } = "";
        public string LastSeenAt { get; init; } = "";

        public Listing ToDomain() => new(
            Guid.Parse(Id), Guid.Parse(ProductId), Site, SiteId, Title, Url,
            Seller, ImageUrl, MatchScore, LinkStatus,
            DateTime.Parse(FirstSeenAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTime.Parse(LastSeenAt,  null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    private sealed class ProductSummaryRow
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public int ListingCount { get; init; }
        public decimal? BestPrice { get; init; }
        public string? BestSite { get; init; }
        public string? LastUpdated { get; init; }
        public string? Category { get; init; }
    }

    private sealed class ListingAdminRow
    {
        public string Id { get; init; } = "";
        public string Site { get; init; } = "";
        public string SiteId { get; init; } = "";
        public string Title { get; init; } = "";
        public string Url { get; init; } = "";
        public string? Seller { get; init; }
        public string? ImageUrl { get; init; }
        public double MatchScore { get; init; }
        public string LinkStatus { get; init; } = "";
        public decimal? CurrentPrice { get; init; }
        public decimal? OriginalPrice { get; init; }
        public string? LastFetchedAt { get; init; }
    }

    private sealed class PriceHistoryRow
    {
        public string Title { get; init; } = "";
        public string FetchedAt { get; init; } = "";
        public decimal Price { get; init; }
    }

    private sealed class ProductPublicRow
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public decimal? BestPrice { get; init; }
        public string? BestSite { get; init; }
        public int StoreCount { get; init; }
        public string? ImageUrl { get; init; }
        public string? Category { get; init; }
    }

    private sealed class ComparisonRow
    {
        public string Id { get; init; } = "";
        public string Site { get; init; } = "";
        public string SiteId { get; init; } = "";
        public string Title { get; init; } = "";
        public string Url { get; init; } = "";
        public string? Seller { get; init; }
        public string? ImageUrl { get; init; }
        public decimal? CurrentPrice { get; init; }
        public decimal? OriginalPrice { get; init; }
    }
}

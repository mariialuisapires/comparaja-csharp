using Microsoft.AspNetCore.Mvc;
using ComparadorPrecos.Domain;
using ComparadorPrecos.Ports;

namespace ComparadorPrecos.Controllers;

[Route("[controller]")]
public class PublicController : Controller
{
    private readonly IProductRepository _repo;
    public PublicController(IProductRepository repo) => _repo = repo;

    [HttpGet]
    public IActionResult Index(string? q)
    {
        ViewBag.Search     = q;
        ViewBag.Products   = _repo.ListProductsPublicView(q);
        ViewBag.Categories = Categories.All;
        return View();
    }

    [HttpGet("category/{slug}")]
    public IActionResult Category(string slug, string? brand = null,
        decimal? minPrice = null, decimal? maxPrice = null, string sort = "price_asc")
    {
        var catDef = Categories.Find(slug);
        if (catDef is null) return NotFound();

        ViewBag.Category = catDef;
        ViewBag.Brand    = brand;
        ViewBag.MinPrice = minPrice;
        ViewBag.MaxPrice = maxPrice;
        ViewBag.Sort     = sort;
        ViewBag.Products = _repo.ListProductsByCategory(slug, brand, minPrice, maxPrice, sort);
        return View();
    }

    [HttpGet("favorites")]
    public IActionResult Favorites()
    {
        ViewBag.Title = "Meus Favoritos — ComparaJá";
        return View();
    }

    [HttpGet("[action]/{id}")]
    public IActionResult Product(string id)
    {
        if (!Guid.TryParse(id, out var guid)) return NotFound();
        var product = _repo.GetProduct(guid);
        if (product is null) return NotFound();

        ViewBag.Product  = product;
        ViewBag.Listings = _repo.GetListingsForComparison(guid);
        ViewBag.History  = _repo.GetPriceHistory(guid);
        return View();
    }
}

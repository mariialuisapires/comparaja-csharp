using Microsoft.AspNetCore.Mvc;
using ComparadorPrecos.Ports;

namespace ComparadorPrecos.Controllers;

[Route("[controller]")]
public class AdminController : Controller
{
    private readonly IProductRepository _repo;
    private readonly AuthService        _auth;

    public AdminController(IProductRepository repo, AuthService auth)
    {
        _repo = repo;
        _auth = auth;
    }

    [HttpGet]
    public IActionResult Index(string? q)
    {
        if (!IsAdmin()) return Redirect("/admin/login");
        ViewBag.Search   = q;
        ViewBag.Products = _repo.ListProductsSummary(q);
        return View();
    }

    [HttpGet("[action]/{id}")]
    public IActionResult Product(string id)
    {
        if (!IsAdmin()) return Redirect("/admin/login");
        if (!Guid.TryParse(id, out var guid)) return NotFound();
        var product = _repo.GetProduct(guid);
        if (product is null) return NotFound();

        ViewBag.Product  = product;
        ViewBag.Listings = _repo.GetListingsWithCurrentPrice(guid);
        ViewBag.History  = _repo.GetPriceHistory(guid);
        return View();
    }

    [HttpGet("[action]")]
    public IActionResult Login()
    {
        if (IsAdmin()) return Redirect("/admin");
        return View();
    }

    [HttpPost("[action]")]
    public IActionResult Login(string email, string password)
    {
        if (_auth.ValidateCredentials(email, password))
        {
            HttpContext.Session.SetString("user", email);
            return Redirect("/admin");
        }
        ViewBag.Error = "Credenciais inválidas.";
        return View();
    }

    [HttpGet("[action]")]
    public IActionResult Logout()
    {
        HttpContext.Session.Remove("user");
        return Redirect("/admin/login");
    }

    [HttpPost("set-price")]
    public IActionResult SetPrice(string id, string price)
    {
        if (!IsAdmin()) return Redirect("/admin/login");
        if (!Guid.TryParse(id, out var guid)) return NotFound();
        var normalized = (price ?? "").Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
            return BadRequest("Preço inválido");
        _repo.SetManualPrice(guid, value);
        var referer = Request.Headers["Referer"].ToString();
        return Redirect(string.IsNullOrEmpty(referer) ? "/admin" : referer);
    }

    [HttpPost("[action]")]
    public IActionResult Confirm(string id) => SetStatus(id, "confirmed");

    [HttpPost("[action]")]
    public IActionResult Reject(string id) => SetStatus(id, "rejected");

    [HttpPost("[action]")]
    public IActionResult Unobserve(string id) => SetStatus(id, "rejected");

    [HttpPost("[action]")]
    public IActionResult Reactivate(string id) => SetStatus(id, "confirmed");

    private IActionResult SetStatus(string id, string status)
    {
        if (!IsAdmin()) return Redirect("/admin/login");
        if (!Guid.TryParse(id, out var guid)) return NotFound();
        _repo.SetListingStatus(guid, status);
        var referer = Request.Headers["Referer"].ToString();
        return Redirect(string.IsNullOrEmpty(referer) ? "/admin" : referer);
    }

    private bool IsAdmin() => HttpContext.Session.GetString("user") != null;
}

using ComparadorPrecos.Adapters.Storage.Sqlite;
using ComparadorPrecos.Controllers;
using ComparadorPrecos.Ports;

namespace ComparadorPrecos;

public static class WebApp
{
    public static void Run(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var dbPath = Environment.GetEnvironmentVariable("COMPARADOR_DB") ?? "data/comparador.db";

        builder.Services.AddSingleton<IProductRepository>(_ => new SqliteProductRepository(dbPath));
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddControllersWithViews();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(o =>
        {
            o.Cookie.HttpOnly  = true;
            o.Cookie.IsEssential = true;
            o.IdleTimeout      = TimeSpan.FromHours(8);
        });

        var app = builder.Build();

        app.UseDeveloperExceptionPage();
        app.UseStaticFiles();
        app.UseSession();
        app.MapGet("/", () => Results.Redirect("/public"));
        app.MapControllers();

        app.Run();
    }
}

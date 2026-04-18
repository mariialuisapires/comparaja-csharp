#!/usr/bin/env dotnet-script
// dotnet script diagnose.csx
// Quick selector check — run with: dotnet script diagnose.csx

using System.Net.Http;

var http = new HttpClient();
http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
http.DefaultRequestHeaders.Add("Accept-Language", "pt-BR,pt;q=0.9");

var q = Uri.EscapeDataString("iphone 15 pro");
var html = await http.GetStringAsync($"https://lista.mercadolivre.com.br/{q}");

// Check for various selectors
var selectors = new[] {
    "ui-search-result__wrapper",
    "ui-search-results",
    "ui-search-item__title",
    "andes-card",
    "poly-card",
    "ui-search-layout__item",
    "data-testid=\"result\"",
};

Console.WriteLine($"HTML length: {html.Length}");
foreach (var s in selectors)
    Console.WriteLine($"  {s}: {(html.Contains(s) ? "FOUND" : "not found")}");

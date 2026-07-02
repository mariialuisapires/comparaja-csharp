using Microsoft.Playwright;

namespace ComparadorPrecos.Adapters.Sources.Crawler;

public sealed class RateLimitedFetcher : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser    _browser;
    private readonly Dictionary<string, IBrowserContext> _contexts = new();
    private readonly Dictionary<string, SemaphoreSlim>  _locks    = new();
    private readonly bool   _headful;
    private readonly double _minDelay;
    private readonly double _maxDelay;

    private RateLimitedFetcher(IPlaywright pw, IBrowser browser,
        bool headful, double minDelay, double maxDelay)
    {
        _playwright = pw;
        _browser    = browser;
        _headful    = headful;
        _minDelay   = minDelay;
        _maxDelay   = maxDelay;
    }

    public static async Task<RateLimitedFetcher> CreateAsync(
        bool headful = false, double minDelay = 3.0, double maxDelay = 8.0)
    {
        var pw = await Playwright.CreateAsync();
        var browser = await pw.Chromium.LaunchAsync(new() { Headless = !headful });
        return new RateLimitedFetcher(pw, browser, headful, minDelay, maxDelay);
    }

    public async Task<string> FetchHtmlAsync(string url, string domain,
        string? waitSelector = null, CancellationToken ct = default)
    {
        var sem = GetLock(domain);
        await sem.WaitAsync(ct);
        try
        {
            var ctx  = await GetContextAsync(domain);
            var page = await ctx.NewPageAsync();
            try
            {
                await page.AddInitScriptAsync(AntiBotHelper.StealthScript);
                await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

                if (waitSelector is not null)
                {
                    try
                    {
                        await page.WaitForSelectorAsync(waitSelector,
                            new() { Timeout = 15_000, State = WaitForSelectorState.Attached });
                    }
                    catch (TimeoutException)
                    {
                        Console.Error.WriteLine($"  [fetcher] wait selector '{waitSelector}' não encontrado em {domain} — continuando com HTML parcial");
                    }
                }

                await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 2)");
                await Task.Delay(500, ct);

                return await page.ContentAsync();
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        finally
        {
            await AntiBotHelper.HumanDelayAsync(_minDelay, _maxDelay, ct);
            sem.Release();
        }
    }

    private SemaphoreSlim GetLock(string domain)
    {
        lock (_locks)
        {
            if (!_locks.TryGetValue(domain, out var s))
                _locks[domain] = s = new SemaphoreSlim(1, 1);
            return s;
        }
    }

    private async Task<IBrowserContext> GetContextAsync(string domain)
    {
        lock (_contexts)
        {
            if (_contexts.TryGetValue(domain, out var ctx)) return Task.FromResult(ctx).Result;
        }
        var newCtx = await _browser.NewContextAsync(new()
        {
            UserAgent = AntiBotHelper.RandomUserAgent(),
            Locale    = "pt-BR",
            TimezoneId = "America/Sao_Paulo",
            ViewportSize = new() { Width = 1920, Height = 1080 },
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7",
                ["DNT"] = "1",
                ["Upgrade-Insecure-Requests"] = "1",
                ["sec-ch-ua"] = "\"Google Chrome\";v=\"136\", \"Chromium\";v=\"136\", \"Not.A/Brand\";v=\"24\"",
                ["sec-ch-ua-mobile"] = "?0",
                ["sec-ch-ua-platform"] = "\"Windows\"",
            }
        });
        await newCtx.RouteAsync("**/*", async route =>
        {
            var type = route.Request.ResourceType;
            if (type is "media" or "font")
                await route.AbortAsync();
            else
                await route.ContinueAsync();
        });
        lock (_contexts) { _contexts[domain] = newCtx; }
        return newCtx;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var ctx in _contexts.Values)
            await ctx.DisposeAsync();
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }
}

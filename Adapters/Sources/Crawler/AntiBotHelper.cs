namespace ComparadorPrecos.Adapters.Sources.Crawler;

public static class AntiBotHelper
{
    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:137.0) Gecko/20100101 Firefox/137.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_4) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.4 Safari/605.1.15",
    ];

    private static readonly Random Rng = new();

    public static string RandomUserAgent() => UserAgents[Rng.Next(UserAgents.Length)];

    public static Task HumanDelayAsync(double minSec = 3.0, double maxSec = 8.0,
        CancellationToken ct = default)
    {
        var ms = (int)((minSec + Rng.NextDouble() * (maxSec - minSec)) * 1000);
        return Task.Delay(ms, ct);
    }

    public const string StealthScript = """
        // Hide webdriver
        Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
        delete navigator.__proto__.webdriver;

        // Realistic plugins
        Object.defineProperty(navigator, 'plugins', { get: () => [
            { name: 'Chrome PDF Plugin' }, { name: 'Chrome PDF Viewer' },
            { name: 'Native Client' }
        ]});
        Object.defineProperty(navigator, 'languages', { get: () => ['pt-BR','pt','en-US','en'] });

        // WebGL vendor spoofing
        const origGetParameter = WebGLRenderingContext.prototype.getParameter;
        WebGLRenderingContext.prototype.getParameter = function(p) {
            if (p === 37445) return 'Intel Inc.';
            if (p === 37446) return 'Intel Iris OpenGL Engine';
            return origGetParameter.apply(this, arguments);
        };

        // Full chrome object
        window.chrome = {
            runtime: {
                connect: () => ({}),
                sendMessage: () => {},
            },
            app: { isInstalled: false },
            csi: () => {},
            loadTimes: () => {},
        };

        // Permissions API
        const origQuery = window.Notification && Notification.requestPermission;
        if (window.Permissions) {
            const origPermQuery = Permissions.prototype.query;
            Permissions.prototype.query = function(p) {
                if (p.name === 'notifications') return Promise.resolve({ state: 'prompt' });
                return origPermQuery.apply(this, arguments);
            };
        }

        // Realistic screen properties
        Object.defineProperty(screen, 'colorDepth', { get: () => 24 });
        Object.defineProperty(screen, 'pixelDepth', { get: () => 24 });
        """;
}

namespace ComparadorPrecos.Adapters.Cli;

public static class ServeCommand
{
    public static Task RunAsync(string[] args)
    {
        var db   = GetOpt(args, "--db")     ?? "data/comparador.db";
        var host = GetOpt(args, "--host")   ?? "127.0.0.1";
        var port = GetOpt(args, "--port")   ?? "5000";

        Environment.SetEnvironmentVariable("COMPARADOR_DB", db);

        var webArgs = new[] { "--urls", $"http://{host}:{port}" };
        WebApp.Run(webArgs);
        return Task.CompletedTask;
    }

    private static string? GetOpt(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}

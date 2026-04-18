using ComparadorPrecos.Adapters.Cli;

var command = args.Length > 0 ? args[0] : "serve";
var rest    = args.Skip(1).ToArray();

await (command switch
{
    "track" => TrackCommand.RunAsync(rest),
    "serve" => ServeCommand.RunAsync(rest),
    _ => Task.Run(() =>
    {
        Console.Error.WriteLine($"Comando desconhecido: {command}. Use 'track' ou 'serve'.");
        Environment.Exit(1);
    })
});

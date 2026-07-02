namespace ComparadorPrecos.Domain;

public sealed record CategoryDef(string Slug, string Label, string Icon, string[] Brands);

public static class Categories
{
    public static readonly CategoryDef[] All =
    [
        new("smartphone",    "Celulares",      "📱", ["Apple", "Samsung", "Motorola", "Xiaomi", "LG"]),
        new("console",       "Consoles",       "🎮", ["Sony", "Microsoft", "Nintendo"]),
        new("placa-de-video","Placas de Vídeo","🖥️", ["NVIDIA", "AMD", "Gigabyte", "ASUS", "MSI"]),
    ];

    public static CategoryDef? Find(string slug) =>
        All.FirstOrDefault(c => c.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
}

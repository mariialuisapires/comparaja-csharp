namespace ComparadorPrecos.Domain;

public sealed record CategoryDef(string Slug, string Label, string Icon, string[] Brands);

public static class Categories
{
    public static readonly CategoryDef[] All =
    [
        new("smartphone",  "Smartphones",     "📱", ["Apple", "Samsung", "Motorola", "Xiaomi", "LG"]),
        new("notebook",    "Notebooks",        "💻", ["Apple", "Dell", "HP", "Lenovo", "Samsung", "Asus"]),
        new("console",     "Consoles",         "🎮", ["Sony", "Microsoft", "Nintendo"]),
        new("audio",       "Áudio",            "🎧", ["Sony", "JBL", "Bose", "Apple", "Samsung"]),
        new("geladeira",   "Geladeiras",       "🧊", ["Brastemp", "Electrolux", "Consul", "Samsung", "LG"]),
        new("fogao",       "Fogões",           "🔥", ["Brastemp", "Electrolux", "Consul", "Fischer", "Atlas"]),
        new("lavadora",    "Lavadoras",        "🫧", ["Electrolux", "Samsung", "Brastemp", "LG", "Consul"]),
        new("forno",       "Fornos Elétricos", "⚡", ["Electrolux", "Brastemp", "Consul", "Mueller"]),
        new("microondas",  "Microondas",       "📡", ["LG", "Electrolux", "Consul", "Panasonic", "Samsung"]),
        new("chuveiro",    "Chuveiros",        "🚿", ["Lorenzetti", "Corona", "Komeco", "Fame", "Hydra"]),
        new("lava-loucas", "Lava-Louças",      "🍽️", ["Electrolux", "Brastemp"]),
        new("secadora",    "Secadoras",        "💨", ["Electrolux", "Brastemp", "Samsung", "LG"]),
    ];

    public static CategoryDef? Find(string slug) =>
        All.FirstOrDefault(c => c.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
}

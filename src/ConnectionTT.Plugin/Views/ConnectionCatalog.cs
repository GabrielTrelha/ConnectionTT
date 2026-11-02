using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ConnectionTT.Plugin;

// Catalogo das conexoes mostradas na paleta (botao "Conexões" / CTTCONEXOES).
// Fonte: Palette\catalog.json ao lado da DLL do plugin. Para incluir uma conexao nova basta acrescentar
// um item em "connections" (e as imagens em Palette\images) - nao precisa recompilar o plugin.
// Se o arquivo faltar ou estiver invalido, usa o catalogo embutido abaixo (so a CTT_Handrail).
public sealed class CatalogCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
}

public sealed class CatalogConnection
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string? Icon { get; set; }
    public string? Preview { get; set; }
    public string? Selection { get; set; }
    public string? Profiles { get; set; }
    public string? Description { get; set; }
    public string? Options { get; set; }
}

public sealed class ConnectionCatalog
{
    public List<CatalogCategory> Categories { get; set; } = new();
    public List<CatalogConnection> Connections { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Folder =>
        Path.Combine(Path.GetDirectoryName(typeof(ConnectionCatalog).Assembly.Location) ?? AppContext.BaseDirectory, "Palette");

    public static string? ImagePath(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? null : Path.Combine(Folder, "images", fileName);

    public static ConnectionCatalog Load()
    {
        var file = Path.Combine(Folder, "catalog.json");
        try
        {
            if (!File.Exists(file))
            {
                Log.Write($"Paleta: catalogo nao encontrado em '{file}' - usando o catalogo embutido");
                return BuiltIn();
            }

            var catalog = JsonSerializer.Deserialize<ConnectionCatalog>(File.ReadAllText(file), Options);
            if (catalog == null || catalog.Connections.Count == 0)
            {
                Log.Write($"Paleta: '{file}' vazio - usando o catalogo embutido");
                return BuiltIn();
            }

            catalog.Connections = catalog.Connections
                .Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Command))
                .ToList();

            // Toda conexao precisa de uma categoria existente; senao cria "Outras".
            var known = new HashSet<string>(catalog.Categories.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
            if (catalog.Connections.Any(c => !known.Contains(c.Category)))
            {
                foreach (var c in catalog.Connections.Where(c => !known.Contains(c.Category)))
                {
                    c.Category = "outras";
                }

                if (!known.Contains("outras"))
                {
                    catalog.Categories.Add(new CatalogCategory { Id = "outras", Name = "Outras" });
                }
            }

            Log.Write($"Paleta: catalogo carregado ({catalog.Categories.Count} categorias, {catalog.Connections.Count} conexoes)");
            return catalog;
        }
        catch (Exception ex)
        {
            Log.Write($"Paleta: erro lendo '{file}' - usando o catalogo embutido: {ex}");
            return BuiltIn();
        }
    }

    public static ConnectionCatalog BuiltIn() => new()
    {
        Categories =
        {
            new CatalogCategory { Id = "corrimao", Name = "Corrimão", Icon = "cat_railing.png" },
        },
        Connections =
        {
            new CatalogConnection
            {
                Id = "CTT_Handrail",
                Category = "corrimao",
                Name = "Suporte de corrimão",
                Command = "AstM4CrConByVb CTT_Handrail",
                Icon = "CTT_Handrail_icon.png",
                Preview = "CTT_Handrail_preview.png",
                Selection = "1. Viga, 2. Tubo do corrimão",
                Profiles = "Viga = perfil I; Tubo = tubo redondo",
                Description = "Chapa de ancoragem soldada na alma da viga e chapa de conexão soldada no tubo, unidas por parafusos.",
                Options = "Ancoragem antes/depois do tubo, alturas iguais, parafusos, soldas, ajuste da ponta do tubo, chapa no fundo do tubo.",
            },
        },
    };
}

// Favoritos do usuario (por computador/usuario do Windows): %APPDATA%\ConnectionTT\favoritos.json
public static class Favorites
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConnectionTT", "favoritos.json");

    private static HashSet<string>? _ids;

    private static HashSet<string> Ids
    {
        get
        {
            if (_ids != null)
            {
                return _ids;
            }

            _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                {
                    var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath));
                    if (list != null)
                    {
                        _ids.UnionWith(list);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Paleta: erro lendo favoritos '{FilePath}': {ex}");
            }

            return _ids;
        }
    }

    public static bool Contains(string id) => Ids.Contains(id);

    public static void Toggle(string id)
    {
        if (!Ids.Remove(id))
        {
            Ids.Add(id);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Ids.OrderBy(x => x).ToList()));
        }
        catch (Exception ex)
        {
            Log.Write($"Paleta: erro gravando favoritos '{FilePath}': {ex}");
        }
    }
}

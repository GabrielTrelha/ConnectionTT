namespace ConnectionTT.Setup;

/// <summary>
/// O que muda de uma versao do Advance Steel para outra. So o 2026 foi validado; para suportar outra
/// versao, acrescente uma entrada aqui depois de confirmar a instancia do LocalDB nos error.log do SQL.
/// </summary>
public sealed record AdvanceSteelVersion(
    int Year,
    string AutoCadFolder,          // pasta em C:\Program Files\Autodesk
    string DataFolder,             // pasta em C:\ProgramData\Autodesk
    string LocalDbInstance,        // instancia que o Advance Steel usa para abrir os bancos
    IReadOnlyList<string> InstancesToStop) // instancias que podem travar o .mdf
{
    public static readonly IReadOnlyList<AdvanceSteelVersion> Known = new[]
    {
        // Confirmado em 2026-10-29: o Advance Steel 2026 abre os bancos pela instancia AdvanceSteel2025.
        // Se a instancia AdvanceSteel2026 abrir o mesmo .mdf, o arquivo fica travado (OS error 32).
        new AdvanceSteelVersion(2026, "AutoCAD 2026", @"Advance Steel 2026", "AdvanceSteel2025",
            new[] { "AdvanceSteel2026", "AdvanceSteel2025" }),
    };
}

/// <summary>Uma instalacao encontrada nesta maquina: pasta ADVS + um AstorRules por idioma (INT, USA...).</summary>
public sealed record AdvanceSteelInstall(AdvanceSteelVersion Version, string AdvsFolder, IReadOnlyList<string> AstorRulesFiles)
{
    public static List<AdvanceSteelInstall> Detect(int? year, string? advsOverride, string? astorRulesOverride)
    {
        var found = new List<AdvanceSteelInstall>();
        foreach (var v in AdvanceSteelVersion.Known)
        {
            if (year is not null && v.Year != year) continue;

            var advs = advsOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", v.AutoCadFolder, "ADVS");
            if (!File.Exists(Path.Combine(advs, "ASObjectsMgd.dll")))
            {
                Log.Info($"Advance Steel {v.Year}: nao encontrado (sem ASObjectsMgd.dll em {advs}).");
                continue;
            }

            var dbs = new List<string>();
            if (astorRulesOverride is not null)
            {
                dbs.Add(Path.GetFullPath(astorRulesOverride));
            }
            else
            {
                var dataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", v.DataFolder);
                if (Directory.Exists(dataRoot))
                {
                    foreach (var locale in Directory.EnumerateDirectories(dataRoot))
                    {
                        var mdf = Path.Combine(locale, "Steel", "Data", "AstorRules.mdf");
                        if (File.Exists(mdf)) dbs.Add(mdf);
                    }
                }
            }

            if (dbs.Count == 0)
            {
                Log.Info($"Advance Steel {v.Year}: ADVS encontrado, mas nenhum AstorRules.mdf. Abra o Advance Steel uma vez e tente de novo.");
                continue;
            }

            found.Add(new AdvanceSteelInstall(v, advs, dbs));
        }
        return found;
    }
}

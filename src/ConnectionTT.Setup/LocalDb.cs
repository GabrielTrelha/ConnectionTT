using System.Diagnostics;
using System.Text.Json;

namespace ConnectionTT.Setup;

/// <summary>Controle das instancias do SQL LocalDB e backup/restauracao dos arquivos do AstorRules.</summary>
public static class LocalDb
{
    public static string FindSqlLocalDb()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft SQL Server");
        var exe = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "SqlLocalDB.exe", SearchOption.AllDirectories)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase) // pasta de versao maior primeiro (160 > 150)
                .FirstOrDefault()
            : null;
        return exe ?? throw new SetupException(
            "SqlLocalDB.exe nao encontrado em " + root + ". O Advance Steel instala o SQL LocalDB; reinstale-o ou repare a instalacao.");
    }

    /// <summary>Para as instancias que podem estar segurando o .mdf. Instancia inexistente nao e erro.</summary>
    public static void StopInstances(IEnumerable<string> instances)
    {
        var exe = FindSqlLocalDb();
        foreach (var name in instances)
        {
            var (code, output) = Run(exe, $"stop \"{name}\"");
            Log.Info($"SqlLocalDB stop {name} -> codigo {code}: {output.Trim()}");
        }
    }

    private static (int Code, string Output) Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new SetupException("Nao foi possivel iniciar " + exe);
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(60_000))
        {
            p.Kill(true);
            throw new SetupException($"{exe} {args} nao terminou em 60 s.");
        }
        return (p.ExitCode, stdout + stderr);
    }

    /// <summary>
    /// Copia o .mdf e o .ldf do AstorRules para %ProgramData%\ConnectionTT\backups\&lt;data&gt;.
    /// As instancias precisam estar paradas antes (senao o arquivo esta em uso e a copia falha).
    /// </summary>
    public static string BackupFiles(string mdf, string action)
    {
        var sourceDir = Path.GetDirectoryName(mdf)!;
        var stem = Path.GetFileNameWithoutExtension(mdf); // AstorRules
        var files = Directory.EnumerateFiles(sourceDir, stem + "*")
            .Where(f => f.EndsWith(".mdf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ldf", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count == 0) throw new SetupException("Nenhum arquivo do banco encontrado ao lado de " + mdf);

        var locale = new DirectoryInfo(sourceDir).Parent?.Parent?.Name ?? "db"; // ...\INT\Steel\Data -> INT
        var target = Path.Combine(Log.DataFolder, "backups", $"{DateTime.Now:yyyyMMdd-HHmmss}-{action}-{locale}");
        Directory.CreateDirectory(target);
        foreach (var f in files)
        {
            File.Copy(f, Path.Combine(target, Path.GetFileName(f)), overwrite: false);
        }

        var manifest = new BackupManifest(DateTime.Now, action, sourceDir, files.Select(Path.GetFileName).ToList()!);
        File.WriteAllText(Path.Combine(target, "backup.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Log.Info($"Backup de {files.Count} arquivo(s) em {target}");
        return target;
    }

    /// <summary>Devolve os arquivos de um backup para a pasta original (antes faz um backup do estado atual).</summary>
    public static void RestoreFiles(string backupDir, IEnumerable<string> instancesToStop)
    {
        var manifestPath = Path.Combine(backupDir, "backup.json");
        if (!File.Exists(manifestPath)) throw new SetupException("Pasta de backup invalida (sem backup.json): " + backupDir);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath))
            ?? throw new SetupException("backup.json ilegivel em " + backupDir);

        StopInstances(instancesToStop);
        var mdf = manifest.Files.FirstOrDefault(f => f.EndsWith(".mdf", StringComparison.OrdinalIgnoreCase))
            ?? throw new SetupException("O backup nao contem o .mdf.");
        BackupFiles(Path.Combine(manifest.SourceFolder, mdf), "antes-de-restaurar");

        foreach (var name in manifest.Files)
        {
            File.Copy(Path.Combine(backupDir, name), Path.Combine(manifest.SourceFolder, name), overwrite: true);
            Log.Info($"Restaurado: {name} -> {manifest.SourceFolder}");
        }
    }

    public sealed record BackupManifest(DateTime CreatedAt, string Action, string SourceFolder, List<string> Files);
}

public sealed class SetupException(string message) : Exception(message);

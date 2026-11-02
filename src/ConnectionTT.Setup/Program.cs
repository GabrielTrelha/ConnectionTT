using System.Diagnostics;

namespace ConnectionTT.Setup;

public static class Program
{
    private const int Ok = 0, Unexpected = 1, HostRunning = 2, Refused = 3, NotFound = 4, BadArgs = 5;

    private const string Help = @"ConnectionTT.Setup - registra as conexoes do ConnectionTT no Advance Steel

Uso:
  ConnectionTT.Setup status
  ConnectionTT.Setup register   [--joints <ConnectionTT.Joints.dll>] [opcoes]
  ConnectionTT.Setup unregister [--purge-library] [opcoes]
  ConnectionTT.Setup backups
  ConnectionTT.Setup restore <pasta-do-backup>

Opcoes:
  --year <ano>           so esta versao do Advance Steel (hoje: 2026)
  --advs <pasta>         pasta ADVS, se nao for a padrao
  --astorrules <arquivo> AstorRules.mdf especifico, se nao for o padrao

register   copia o ConnectionTT.Joints.dll para a pasta ADVS (se --joints for informado) e grava
           as linhas em RulesDllSigned / HRLDefinition, criando a tabela da Library se faltar.
unregister remove as linhas do ConnectionTT e retira a DLL da pasta ADVS (vai para o backup).
           A tabela da Library fica, com as pre-definicoes, a menos que use --purge-library.
Antes de qualquer alteracao o banco inteiro (.mdf/.ldf) e copiado para
  %ProgramData%\ConnectionTT\backups. Use 'restore' para voltar a qualquer um deles.

O Advance Steel precisa estar FECHADO. Precisa de permissao de administrador.
Codigos de saida: 0 ok, 1 erro inesperado, 2 Advance Steel aberto, 3 recusado (conflito ou schema),
4 Advance Steel nao encontrado, 5 argumentos invalidos. Log: %ProgramData%\ConnectionTT\setup.log";

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "/?" or "help")
        {
            Console.WriteLine(Help);
            return args.Length == 0 ? BadArgs : Ok;
        }

        Options o;
        try
        {
            o = Options.Parse(args);
        }
        catch (SetupException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Use --help para ver as opcoes.");
            return BadArgs;
        }

        Log.Info($"===== ConnectionTT.Setup {typeof(Program).Assembly.GetName().Version} - {string.Join(' ', args)}");
        try
        {
            return o.Command switch
            {
                "status" => Status(o),
                "register" => Register(o),
                "unregister" => Unregister(o),
                "backups" => ListBackups(),
                "restore" => Restore(o),
                _ => throw new SetupException("Comando desconhecido: " + o.Command),
            };
        }
        catch (HostRunningException ex)
        {
            Log.Error(ex.Message);
            return HostRunning;
        }
        catch (SetupException ex)
        {
            Log.Error(ex.Message);
            return Refused;
        }
        catch (Exception ex)
        {
            Log.Error("Erro inesperado.", ex);
            return Unexpected;
        }
    }

    // ---------------------------------------------------------------- comandos

    private static int Status(Options o)
    {
        var installs = Detect(o);
        if (installs.Count == 0) return NotFound;
        foreach (var inst in installs)
        {
            var dll = Path.Combine(inst.AdvsFolder, RuleCatalog.Joints.FileName);
            Log.Info($"Advance Steel {inst.Version.Year}: ADVS = {inst.AdvsFolder}");
            Log.Info($"  {RuleCatalog.Joints.FileName}: {(File.Exists(dll) ? $"presente ({File.GetLastWriteTime(dll):yyyy-MM-dd HH:mm})" : "ausente")}");
            foreach (var mdf in inst.AstorRulesFiles)
            {
                Log.Info($"  AstorRules: {mdf}");
                if (IsHostRunning())
                {
                    Log.Info("  (Advance Steel aberto: consulta ao banco pulada para nao travar o arquivo)");
                    continue;
                }
                WithDatabase(inst, mdf, action: null, db => db.PrintStatus(RuleCatalog.Joints));
            }
        }
        return Ok;
    }

    private static int Register(Options o)
    {
        EnsureHostClosed();
        if (o.JointsDll is not null && !File.Exists(o.JointsDll))
            throw new SetupException("Arquivo nao encontrado: " + o.JointsDll);

        var installs = Detect(o);
        if (installs.Count == 0) return NotFound;

        foreach (var inst in installs)
        {
            if (o.JointsDll is not null) CopyJoints(o.JointsDll, inst.AdvsFolder);
            foreach (var mdf in inst.AstorRulesFiles)
            {
                WithDatabase(inst, mdf, action: "register", db =>
                {
                    db.Register(RuleCatalog.Joints);
                    db.PrintStatus(RuleCatalog.Joints);
                });
            }
        }
        Log.Info("Registro concluido. Teste no Advance Steel: AstM4CrConByVb CTT_Handrail");
        return Ok;
    }

    private static int Unregister(Options o)
    {
        EnsureHostClosed();
        var installs = Detect(o);
        if (installs.Count == 0) return NotFound;

        foreach (var inst in installs)
        {
            var dllFree = true;
            foreach (var mdf in inst.AstorRulesFiles)
            {
                WithDatabase(inst, mdf, action: "unregister", db =>
                {
                    dllFree &= db.Unregister(RuleCatalog.Joints, o.PurgeLibrary);
                });
            }
            if (dllFree) RemoveJoints(inst.AdvsFolder);
        }
        Log.Info("Remocao concluida.");
        return Ok;
    }

    private static int ListBackups()
    {
        var root = Path.Combine(Log.DataFolder, "backups");
        if (!Directory.Exists(root))
        {
            Log.Info("Nenhum backup em " + root);
            return Ok;
        }
        foreach (var d in Directory.GetDirectories(root).OrderBy(d => d))
        {
            Log.Info(d);
        }
        return Ok;
    }

    private static int Restore(Options o)
    {
        EnsureHostClosed();
        var dir = o.Positional ?? throw new SetupException("Informe a pasta do backup (veja o comando 'backups').");
        var instances = AdvanceSteelVersion.Known.SelectMany(v => v.InstancesToStop).Distinct().ToList();
        LocalDb.RestoreFiles(Path.GetFullPath(dir), instances);
        Log.Info("Banco restaurado. Abra o Advance Steel para conferir.");
        return Ok;
    }

    // ---------------------------------------------------------------- etapas

    private static List<AdvanceSteelInstall> Detect(Options o)
    {
        var installs = AdvanceSteelInstall.Detect(o.Year, o.Advs, o.AstorRules);
        if (installs.Count == 0)
        {
            var known = string.Join(", ", AdvanceSteelVersion.Known.Select(v => v.Year));
            Log.Error($"Nenhuma instalacao suportada do Advance Steel encontrada (versoes suportadas: {known}).");
        }
        return installs;
    }

    /// <summary>Para as instancias, faz backup (se for alterar), abre o banco, executa e para a instancia no fim.</summary>
    private static void WithDatabase(AdvanceSteelInstall inst, string mdf, string? action, Action<AstorRules> work)
    {
        LocalDb.StopInstances(inst.Version.InstancesToStop);
        if (action is not null) LocalDb.BackupFiles(mdf, action);
        try
        {
            using var db = AstorRules.Open(inst.Version, mdf);
            work(db);
        }
        finally
        {
            // Libera o .mdf para o Advance Steel (regra documentada: nao deixar a instancia segurando o arquivo).
            LocalDb.StopInstances(new[] { inst.Version.LocalDbInstance });
        }
    }

    private static void CopyJoints(string source, string advs)
    {
        var target = Path.Combine(advs, RuleCatalog.Joints.FileName);
        if (File.Exists(target))
        {
            var keep = Path.Combine(Log.DataFolder, "backups", $"{DateTime.Now:yyyyMMdd-HHmmss}-advs");
            Directory.CreateDirectory(keep);
            File.Copy(target, Path.Combine(keep, RuleCatalog.Joints.FileName), overwrite: true);
            Log.Info($"DLL anterior guardada em {keep}");
        }
        File.Copy(source, target, overwrite: true);
        Log.Info($"Copiado: {source} -> {target}");
    }

    private static void RemoveJoints(string advs)
    {
        var target = Path.Combine(advs, RuleCatalog.Joints.FileName);
        if (!File.Exists(target)) return;
        var keep = Path.Combine(Log.DataFolder, "backups", $"{DateTime.Now:yyyyMMdd-HHmmss}-advs-removida");
        Directory.CreateDirectory(keep);
        File.Move(target, Path.Combine(keep, RuleCatalog.Joints.FileName), overwrite: true);
        Log.Info($"{target} retirada da pasta ADVS (guardada em {keep}).");
    }

    private static bool IsHostRunning() => Process.GetProcessesByName("acad").Length > 0;

    private static void EnsureHostClosed()
    {
        if (IsHostRunning())
            throw new HostRunningException("O AutoCAD / Advance Steel esta aberto (acad.exe). Feche-o e rode de novo.");
    }

    private sealed class HostRunningException(string message) : Exception(message);

    // ---------------------------------------------------------------- argumentos

    private sealed class Options
    {
        public string Command { get; private set; } = "";
        public string? Positional { get; private set; }
        public string? JointsDll { get; private set; }
        public int? Year { get; private set; }
        public string? Advs { get; private set; }
        public string? AstorRules { get; private set; }
        public bool PurgeLibrary { get; private set; }

        public static Options Parse(string[] args)
        {
            var o = new Options { Command = args[0].ToLowerInvariant() };
            for (var i = 1; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new SetupException($"Falta o valor de {args[i]}.");
                switch (args[i].ToLowerInvariant())
                {
                    case "--joints": o.JointsDll = Path.GetFullPath(Next()); break;
                    case "--advs": o.Advs = Next(); break;
                    case "--astorrules": o.AstorRules = Next(); break;
                    case "--purge-library": o.PurgeLibrary = true; break;
                    case "--year":
                        o.Year = int.TryParse(Next(), out var y) ? y : throw new SetupException("--year precisa de um numero.");
                        break;
                    default:
                        if (args[i].StartsWith("--")) throw new SetupException("Opcao desconhecida: " + args[i]);
                        o.Positional = args[i];
                        break;
                }
            }
            return o;
        }
    }
}

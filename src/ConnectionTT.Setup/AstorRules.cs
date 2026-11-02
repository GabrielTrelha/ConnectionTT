using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ConnectionTT.Setup;

/// <summary>
/// Leitura e escrita das tabelas RulesDllSigned / HRLDefinition do AstorRules.
/// Regras: confere o schema antes de gravar, nunca sobrescreve linhas de outra conexao,
/// grava tudo numa transacao e so remove o que o proprio ConnectionTT criou.
/// </summary>
public sealed class AstorRules : IDisposable
{
    private static readonly string[] DllColumns = { "Key", "FileName", "Tech", "Signature" };
    private static readonly string[] RuleColumns =
    {
        "Key", "RuleRunName", "InternalName", "Category", "Dll", "SubNameInDll", "ClassID",
        "UpdateOrderPriority", "UpdateWhenFeatChange", "Intention", "LibraryTable", "Version",
    };
    private static readonly Regex SafeName = new("^[A-Za-z_][A-Za-z0-9_]{0,127}$");

    private readonly SqlConnection _con;
    public string Mdf { get; }

    private AstorRules(SqlConnection con, string mdf) { _con = con; Mdf = mdf; }

    /// <summary>
    /// Abre o banco pela instancia que o Advance Steel usa. O nome do banco no LocalDB e o caminho do .mdf
    /// (mesma forma do scripts\library-ctt-handrail.ps1, validada). Se o banco ainda nao estiver anexado
    /// nessa instancia (erro 4060), anexa pelo arquivo.
    /// </summary>
    public static AstorRules Open(AdvanceSteelVersion v, string mdf)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = $@"(localdb)\{v.LocalDbInstance}",
            InitialCatalog = mdf.ToUpperInvariant(),
            IntegratedSecurity = true,
            ConnectTimeout = 60,
            Pooling = false,
            Encrypt = SqlConnectionEncryptOption.Optional,
        };
        var con = new SqlConnection(b.ConnectionString);
        try
        {
            con.Open();
        }
        catch (SqlException ex) when (ex.Number == 4060)
        {
            Log.Warn($"Banco nao anexado em (localdb)\\{v.LocalDbInstance} ({ex.Message}). Anexando pelo arquivo.");
            con.Dispose();
            b.AttachDBFilename = mdf;
            con = new SqlConnection(b.ConnectionString);
            con.Open();
        }
        Log.Info($"Conectado: (localdb)\\{v.LocalDbInstance} -> {mdf}");
        return new AstorRules(con, mdf);
    }

    public void Dispose() => _con.Dispose();

    // ---------------------------------------------------------------- schema

    /// <summary>Confere se as tabelas tem as colunas esperadas e se nao ha coluna obrigatoria desconhecida.</summary>
    public void VerifySchema(SqlTransaction? tx = null)
    {
        CheckTable("RulesDllSigned", DllColumns, tx);
        CheckTable("HRLDefinition", RuleColumns, tx);
    }

    private void CheckTable(string table, string[] expected, SqlTransaction? tx)
    {
        var cols = new Dictionary<string, (bool Nullable, bool HasDefault)>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = Cmd("SELECT COLUMN_NAME, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME=@t", tx))
        {
            cmd.Parameters.AddWithValue("@t", table);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                cols[r.GetString(0)] = (r.GetString(1) == "YES", !r.IsDBNull(2));
            }
        }

        if (cols.Count == 0) throw new SetupException($"Tabela {table} nao existe em {Mdf}. Este nao parece ser um AstorRules valido.");

        var missing = expected.Where(c => !cols.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new SetupException($"Schema diferente do esperado: {table} nao tem as colunas {string.Join(", ", missing)}. Nada foi alterado.");

        var unknownRequired = cols.Where(kv => !kv.Value.Nullable && !kv.Value.HasDefault
                                             && !expected.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                                  .Select(kv => kv.Key).ToList();
        if (unknownRequired.Count > 0)
            throw new SetupException($"Schema diferente do esperado: {table} exige as colunas {string.Join(", ", unknownRequired)}. Nada foi alterado.");

        Log.Info($"Schema de {table} conferido ({cols.Count} colunas).");
    }

    // ---------------------------------------------------------------- registro

    public void Register(RuleAssembly asm)
    {
        using var tx = _con.BeginTransaction();
        try
        {
            VerifySchema(tx);
            RegisterAssembly(asm, tx);
            foreach (var rule in asm.Rules)
            {
                RegisterRule(asm, rule, tx);
            }
            tx.Commit();
            Log.Info("Transacao confirmada.");
        }
        catch
        {
            tx.Rollback();
            Log.Warn("Transacao desfeita: nenhuma alteracao foi gravada neste banco.");
            throw;
        }
    }

    private void RegisterAssembly(RuleAssembly asm, SqlTransaction tx)
    {
        var current = Scalar<string>("SELECT FileName FROM RulesDllSigned WHERE [Key]=@k", tx, ("@k", asm.DllKey));
        if (current is null)
        {
            Exec("INSERT INTO RulesDllSigned ([Key], FileName, Tech, Signature) VALUES (@k, @f, @t, NULL)", tx,
                ("@k", asm.DllKey), ("@f", asm.FileName), ("@t", asm.Tech));
            Log.Info($"RulesDllSigned: inserido Key={asm.DllKey} ({asm.FileName}).");
        }
        else if (!string.Equals(current, asm.FileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new SetupException(
                $"Conflito: RulesDllSigned Key={asm.DllKey} ja pertence a '{current}'. O ConnectionTT nao sobrescreve registros de outros plugins.");
        }
        else
        {
            Exec("UPDATE RulesDllSigned SET Tech=@t WHERE [Key]=@k", tx, ("@k", asm.DllKey), ("@t", asm.Tech));
            Log.Info($"RulesDllSigned: Key={asm.DllKey} ja registrado ({current}).");
        }
    }

    private void RegisterRule(RuleAssembly asm, RuleDefinition rule, SqlTransaction tx)
    {
        var otherKey = Scalar<int?>("SELECT TOP 1 [Key] FROM HRLDefinition WHERE InternalName=@n AND [Key]<>@k", tx,
            ("@n", rule.InternalName), ("@k", rule.RuleKey));
        if (otherKey is not null)
            throw new SetupException($"Conflito: o InternalName '{rule.InternalName}' ja existe com Key={otherKey}.");

        var currentName = Scalar<string>("SELECT InternalName FROM HRLDefinition WHERE [Key]=@k", tx, ("@k", rule.RuleKey));
        var values = new (string, object?)[]
        {
            ("@k", rule.RuleKey), ("@run", rule.RuleRunName), ("@n", rule.InternalName), ("@cat", rule.Category),
            ("@dll", asm.DllKey), ("@cls", rule.ClassName), ("@cid", rule.ClassId), ("@ver", rule.Version),
        };

        if (currentName is null)
        {
            Exec(@"INSERT INTO HRLDefinition ([Key], RuleRunName, InternalName, Category, Dll, SubNameInDll, ClassID,
                     UpdateOrderPriority, UpdateWhenFeatChange, Intention, LibraryTable, Version)
                   VALUES (@k, @run, @n, @cat, @dll, @cls, @cid, 0, 0, 0, NULL, @ver)", tx, values);
            Log.Info($"HRLDefinition: inserido Key={rule.RuleKey} ({rule.InternalName} -> {rule.ClassName}).");
        }
        else if (!string.Equals(currentName, rule.InternalName, StringComparison.Ordinal))
        {
            throw new SetupException(
                $"Conflito: HRLDefinition Key={rule.RuleKey} ja pertence a '{currentName}'. O ConnectionTT nao sobrescreve registros de outros plugins.");
        }
        else
        {
            Exec(@"UPDATE HRLDefinition SET RuleRunName=@run, Category=@cat, Dll=@dll, SubNameInDll=@cls, ClassID=@cid, Version=@ver
                   WHERE [Key]=@k AND InternalName=@n", tx, values);
            Log.Info($"HRLDefinition: Key={rule.RuleKey} ({rule.InternalName}) ja existia; valores atualizados.");
        }

        if (rule.Library is not null)
        {
            EnsureLibraryTable(rule.Library, tx);
            Exec("UPDATE HRLDefinition SET LibraryTable=@t WHERE [Key]=@k AND InternalName=@n", tx,
                ("@t", rule.Library.Name), ("@k", rule.RuleKey), ("@n", rule.InternalName));
            Log.Info($"HRLDefinition: LibraryTable={rule.Library.Name}.");
        }
    }

    /// <summary>Cria a tabela da Library se faltar e acrescenta colunas que faltarem. Nunca apaga pre-definicoes.</summary>
    private void EnsureLibraryTable(LibraryTable lib, SqlTransaction tx)
    {
        RequireSafe(lib.Name);
        foreach (var (col, type) in lib.Columns)
        {
            RequireSafe(col);
            if (type is not ("int" or "float" or "nvarchar(max)" or "smallint"))
                throw new SetupException($"Tipo de coluna nao permitido: {type}");
        }

        var exists = Scalar<int?>("SELECT 1 WHERE OBJECT_ID(@t) IS NOT NULL", tx, ("@t", "dbo." + lib.Name)) is not null;
        if (exists)
        {
            var oldFormat = Scalar<int?>("SELECT 1 WHERE COL_LENGTH(@t, '_anchorSide') IS NOT NULL", tx, ("@t", "dbo." + lib.Name)) is not null;
            if (oldFormat)
                throw new SetupException(
                    $"A tabela {lib.Name} esta no formato antigo (antes da v14). Rode scripts\\library-ctt-handrail.ps1 para converte-la mantendo as pre-definicoes.");
        }
        else
        {
            var cols = string.Join(",\n  ", lib.Columns.Select(c => $"[{c.Column}] {c.SqlType} NULL"));
            Exec($"CREATE TABLE dbo.[{lib.Name}] (\n  [Key] int NOT NULL CONSTRAINT [PK_{lib.Name}] PRIMARY KEY,\n  [Comment] nvarchar(255) NULL,\n  {cols}\n)", tx);
            Log.Info($"Library: tabela {lib.Name} criada ({lib.Columns.Count} colunas de parametros).");
            return;
        }

        var added = 0;
        foreach (var (col, type) in lib.Columns)
        {
            var has = Scalar<int?>("SELECT 1 WHERE COL_LENGTH(@t, @c) IS NOT NULL", tx, ("@t", "dbo." + lib.Name), ("@c", col)) is not null;
            if (has) continue;
            Exec($"ALTER TABLE dbo.[{lib.Name}] ADD [{col}] {type} NULL", tx);
            added++;
        }
        var rows = Scalar<int?>($"SELECT COUNT(*) FROM dbo.[{lib.Name}]", tx) ?? 0;
        Log.Info($"Library: tabela {lib.Name} ja existia ({rows} pre-definicao(oes) mantidas, {added} coluna(s) acrescentada(s)).");
    }

    // ---------------------------------------------------------------- remocao

    /// <summary>Remove as linhas do ConnectionTT. Devolve true se a linha do assembly saiu (a DLL pode ser retirada do ADVS).</summary>
    public bool Unregister(RuleAssembly asm, bool purgeLibrary)
    {
        using var tx = _con.BeginTransaction();
        try
        {
            VerifySchema(tx);
            foreach (var rule in asm.Rules)
            {
                var n = Exec("DELETE FROM HRLDefinition WHERE [Key]=@k AND InternalName=@n", tx,
                    ("@k", rule.RuleKey), ("@n", rule.InternalName));
                Log.Info($"HRLDefinition: {n} linha(s) removida(s) para {rule.InternalName}.");

                if (rule.Library is null) continue;
                RequireSafe(rule.Library.Name);
                if (purgeLibrary)
                {
                    Exec($"IF OBJECT_ID('dbo.{rule.Library.Name}') IS NOT NULL DROP TABLE dbo.[{rule.Library.Name}]", tx);
                    Log.Info($"Library: tabela {rule.Library.Name} removida (--purge-library). O backup do banco guarda as pre-definicoes.");
                }
                else
                {
                    Log.Info($"Library: tabela {rule.Library.Name} mantida (pre-definicoes preservadas para uma reinstalacao).");
                }
            }

            var stillUsed = Scalar<int?>("SELECT COUNT(*) FROM HRLDefinition WHERE Dll=@k", tx, ("@k", asm.DllKey)) ?? 0;
            var dllRemoved = false;
            if (stillUsed == 0)
            {
                var n = Exec("DELETE FROM RulesDllSigned WHERE [Key]=@k AND FileName=@f", tx, ("@k", asm.DllKey), ("@f", asm.FileName));
                dllRemoved = n > 0;
                Log.Info($"RulesDllSigned: {n} linha(s) removida(s) para {asm.FileName}.");
            }
            else
            {
                Log.Warn($"RulesDllSigned Key={asm.DllKey} mantido: ainda ha {stillUsed} conexao(oes) apontando para ele.");
            }

            tx.Commit();
            Log.Info("Transacao confirmada.");
            return dllRemoved;
        }
        catch
        {
            tx.Rollback();
            Log.Warn("Transacao desfeita: nenhuma alteracao foi gravada neste banco.");
            throw;
        }
    }

    // ---------------------------------------------------------------- consulta

    public void PrintStatus(RuleAssembly asm)
    {
        var file = Scalar<string>("SELECT FileName FROM RulesDllSigned WHERE [Key]=@k", null, ("@k", asm.DllKey));
        Log.Info($"  RulesDllSigned Key={asm.DllKey}: {file ?? "(ausente)"}");
        foreach (var rule in asm.Rules)
        {
            using var cmd = Cmd("SELECT InternalName, RuleRunName, SubNameInDll, LibraryTable FROM HRLDefinition WHERE [Key]=@k", null);
            cmd.Parameters.AddWithValue("@k", rule.RuleKey);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                Log.Info($"  HRLDefinition Key={rule.RuleKey}: {r[0]} / {r[1]} / {r[2]} / Library={(r.IsDBNull(3) ? "-" : r.GetString(3))}");
            }
            else
            {
                Log.Info($"  HRLDefinition Key={rule.RuleKey}: (ausente) - {rule.InternalName} nao esta registrada");
            }
        }
    }

    // ---------------------------------------------------------------- utilitarios

    private SqlCommand Cmd(string sql, SqlTransaction? tx) => new(sql, _con, tx) { CommandTimeout = 120 };

    private int Exec(string sql, SqlTransaction? tx, params (string Name, object? Value)[] ps)
    {
        using var cmd = Cmd(sql, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    private T? Scalar<T>(string sql, SqlTransaction? tx, params (string Name, object? Value)[] ps)
    {
        using var cmd = Cmd(sql, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var o = cmd.ExecuteScalar();
        if (o is null || o is DBNull) return default;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(o, target);
    }

    private static void RequireSafe(string name)
    {
        if (!SafeName.IsMatch(name)) throw new SetupException($"Nome de tabela/coluna invalido: {name}");
    }
}

namespace ConnectionTT.Setup;

/// <summary>Uma conexao .NET a registrar no AstorRules (uma linha em HRLDefinition).</summary>
public sealed record RuleDefinition(
    int RuleKey,
    string RuleRunName,
    string InternalName,
    string Category,
    string ClassName,
    string ClassId,
    string Version,
    LibraryTable? Library);

/// <summary>Assembly com conexoes (uma linha em RulesDllSigned).</summary>
public sealed record RuleAssembly(int DllKey, string FileName, int Tech, IReadOnlyList<RuleDefinition> Rules);

/// <summary>Tabela de pre-definicoes (aba Library). Colunas = chaves do IFiler gravadas na Library.</summary>
public sealed record LibraryTable(string Name, IReadOnlyList<(string Column, string SqlType)> Columns);

/// <summary>
/// Fonte unica do que o instalador registra. Para uma conexao nova: acrescente um RuleDefinition
/// (Key livre acima de 150000, InternalName unico, classe completa que implementa IRule).
/// </summary>
public static class RuleCatalog
{
    // Valores identicos aos gravados manualmente em 2026-10-19 e validados no Advance Steel 2026
    // (ver claude/registro-astorrules.md). Version '24' copia as linhas .NET existentes do banco.
    public static readonly RuleAssembly Joints = new(
        DllKey: 150000,
        FileName: "ConnectionTT.Joints.dll",
        Tech: 2, // 2 = conexao da API .NET (documentacao da Autodesk)
        Rules: new[]
        {
            new RuleDefinition(
                RuleKey: 150000,
                RuleRunName: "Handrail Bracket",
                InternalName: "CTT_Handrail",
                Category: "Connection TT",
                ClassName: "ConnectionTT.Joints.HandrailBracketRule",
                ClassId: "{5C2A9E7B-4D31-4F8A-B6E2-9D0C1A3B7E4F}",
                Version: "24",
                Library: new LibraryTable("RULE_CTT_Handrail", new (string, string)[]
                {
                    // Mesma lista e tipos de scripts\library-ctt-handrail.ps1 (formato v14).
                    ("Ancoragem_Posicao", "int"),
                    ("Ancoragem_Espessura", "float"),
                    ("Ancoragem_Altura", "float"),
                    ("Ancoragem_DistTopo", "float"),
                    ("Ancoragem_Largura", "float"),
                    ("Alturas_Iguais", "int"),
                    ("Conexao_Espessura", "float"),
                    ("Conexao_Altura", "float"),
                    ("Conexao_DistTopo", "float"),
                    ("Sobreposicao", "float"),
                    ("Parafuso_Norma", "nvarchar(max)"),
                    ("Parafuso_Grau", "nvarchar(max)"),
                    ("Parafuso_Conjunto", "nvarchar(max)"),
                    ("Parafuso_Diametro", "float"),
                    ("Parafuso_Linhas", "int"),
                    ("Parafuso_Colunas", "int"),
                    ("Parafuso_EspVertical", "float"),
                    ("Parafuso_EspHorizontal", "float"),
                    ("Parafuso_DistTopo", "float"),
                    ("Parafuso_DeslLateral", "float"),
                    ("Parafuso_Inverter", "int"),
                    ("Parafuso_Montagem", "int"),
                    ("Solda_Ancoragem_Tipo", "int"),
                    ("Solda_Ancoragem_Espessura", "float"),
                    ("Solda_Ancoragem_Local", "int"),
                    ("Solda_Conexao_Tipo", "int"),
                    ("Solda_Conexao_Espessura", "float"),
                    ("Solda_Conexao_Local", "int"),
                    ("Tubo_AjustarPonta", "int"),
                    ("Tubo_Ultrapassagem", "float"),
                    ("ChapaFundo", "int"),
                    ("ChapaFundo_Espessura", "float"),
                })),
        });
}

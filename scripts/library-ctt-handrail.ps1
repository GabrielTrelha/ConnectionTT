# Library da conexao CTT_Handrail (pre-definicoes salvas pela aba Library > "Save values").
# Cria a tabela RULE_CTT_Handrail no AstorRules e liga em HRLDefinition.LibraryTable (Key 150000).
# Formato igual ao das conexoes .NET do Advance Steel (RULE_WallRail): [Key] PK, [Comment] = nome,
# uma coluna por chave do Save/Load da HandrailBracketRule. Desde a v14 as chaves tem nomes legiveis
# (Ancoragem_Espessura, Parafuso_Linhas, ...). Se a tabela antiga (colunas _anchorSide, ...) existir,
# ela e convertida mantendo as pre-definicoes ja salvas (backup em CSV antes).
#
# Uso (Advance Steel FECHADO, PowerShell normal):
#   .\library-ctt-handrail.ps1            -> cria / converte / atualiza
#   .\library-ctt-handrail.ps1 -Desfazer  -> remove a tabela (backup CSV antes) e limpa LibraryTable
#   .\library-ctt-handrail.ps1 -Exportar  -> grava as pre-definicoes em CSV
#
# IMPORTANTE: usa a instancia (localdb)\AdvanceSteel2025, que e a que o Advance Steel 2026 usa.
# Nunca abrir estes bancos pela instancia AdvanceSteel2026 (trava o arquivo - ver docs do projeto).
param([switch]$Desfazer, [switch]$Exportar)

$loc = (Get-ChildItem "C:\Program Files\Microsoft SQL Server" -Recurse -Filter SqlLocalDB.exe -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
if (Get-Process acad -ErrorAction SilentlyContinue) { Write-Host "Feche o Advance Steel antes." -ForegroundColor Red; return }
& $loc stop AdvanceSteel2026 | Out-Null

$cs = 'Server=(localdb)\AdvanceSteel2025;Database=C:\PROGRAMDATA\AUTODESK\ADVANCE STEEL 2026\INT\STEEL\DATA\ASTORRULES.MDF;Integrated Security=true;Connect Timeout=60;Pooling=false'
$tabela = 'RULE_CTT_Handrail'

$criar = @"
IF OBJECT_ID('dbo.$tabela') IS NOT NULL AND COL_LENGTH('dbo.$tabela','_anchorSide') IS NOT NULL
BEGIN
  -- Conversao da tabela antiga (nomes de variavel) para os nomes legiveis, mantendo as linhas
  CREATE TABLE dbo.[${tabela}_v14] (
    [Key] int NOT NULL,
    [Comment] nvarchar(255) NULL,
    [Ancoragem_Posicao] int NULL,
    [Ancoragem_Espessura] float NULL,
    [Ancoragem_Altura] float NULL,
    [Ancoragem_DistTopo] float NULL,
    [Ancoragem_Largura] float NULL,
    [Alturas_Iguais] int NULL,
    [Conexao_Espessura] float NULL,
    [Conexao_Altura] float NULL,
    [Conexao_DistTopo] float NULL,
    [Sobreposicao] float NULL,
    [Parafuso_Norma] nvarchar(max) NULL,
    [Parafuso_Grau] nvarchar(max) NULL,
    [Parafuso_Conjunto] nvarchar(max) NULL,
    [Parafuso_Diametro] float NULL,
    [Parafuso_Linhas] int NULL,
    [Parafuso_Colunas] int NULL,
    [Parafuso_EspVertical] float NULL,
    [Parafuso_EspHorizontal] float NULL,
    [Parafuso_DistTopo] float NULL,
    [Parafuso_DeslLateral] float NULL,
    [Parafuso_Inverter] int NULL,
    [Parafuso_Montagem] int NULL,
    [Solda_Ancoragem_Tipo] int NULL,
    [Solda_Ancoragem_Espessura] float NULL,
    [Solda_Ancoragem_Local] int NULL,
    [Solda_Conexao_Tipo] int NULL,
    [Solda_Conexao_Espessura] float NULL,
    [Solda_Conexao_Local] int NULL,
    [Tubo_AjustarPonta] int NULL,
    [Tubo_Ultrapassagem] float NULL,
    [ChapaFundo] int NULL,
    [ChapaFundo_Espessura] float NULL
  );
  EXEC('INSERT INTO dbo.[${tabela}_v14] ([Key], [Comment], [Ancoragem_Posicao], [Ancoragem_Espessura], [Ancoragem_Altura], [Ancoragem_DistTopo], [Ancoragem_Largura], [Alturas_Iguais], [Conexao_Espessura], [Conexao_Altura], [Conexao_DistTopo], [Sobreposicao], [Parafuso_Norma], [Parafuso_Grau], [Parafuso_Conjunto], [Parafuso_Diametro], [Parafuso_Linhas], [Parafuso_Colunas], [Parafuso_EspVertical], [Parafuso_EspHorizontal], [Parafuso_DistTopo], [Parafuso_DeslLateral], [Parafuso_Inverter], [Parafuso_Montagem], [Solda_Ancoragem_Tipo], [Solda_Ancoragem_Espessura], [Solda_Ancoragem_Local], [Solda_Conexao_Tipo], [Solda_Conexao_Espessura], [Solda_Conexao_Local], [Tubo_AjustarPonta], [Tubo_Ultrapassagem], [ChapaFundo], [ChapaFundo_Espessura]) SELECT [Key], [Comment], [_anchorSide], [_anchorThickness], [_anchorHeight], [_anchorTopOffset], [_anchorWidth], [_equalHeights], [_connThickness], [_connHeight], [_connTopOffset], [_overlap], [_boltStandard], [_boltGrade], [_boltAssembly], [_boltDiameter], [_boltNy], [_boltNx], [_boltDy], [_boltDx], [_boltTopEdge], [_boltLateralOffset], [_boltInvert], [_boltLocation], [_weldAnchorType], [_weldAnchorSize], [_weldAnchorLocation], [_weldConnType], [_weldConnSize], [_weldConnLocation], [_tubeAdjust], [_tubeOvershoot], [_capPlate], [_capThickness] FROM dbo.[$tabela]');
  DROP TABLE dbo.[$tabela];
  EXEC sp_rename 'dbo.${tabela}_v14', '$tabela';
  ALTER TABLE dbo.[$tabela] ADD CONSTRAINT PK_$tabela PRIMARY KEY ([Key]);
END
IF OBJECT_ID('dbo.$tabela') IS NULL
BEGIN
  CREATE TABLE dbo.[$tabela] (
    [Key] int NOT NULL CONSTRAINT PK_$tabela PRIMARY KEY,
    [Comment] nvarchar(255) NULL,
    [Ancoragem_Posicao] int NULL,
    [Ancoragem_Espessura] float NULL,
    [Ancoragem_Altura] float NULL,
    [Ancoragem_DistTopo] float NULL,
    [Ancoragem_Largura] float NULL,
    [Alturas_Iguais] int NULL,
    [Conexao_Espessura] float NULL,
    [Conexao_Altura] float NULL,
    [Conexao_DistTopo] float NULL,
    [Sobreposicao] float NULL,
    [Parafuso_Norma] nvarchar(max) NULL,
    [Parafuso_Grau] nvarchar(max) NULL,
    [Parafuso_Conjunto] nvarchar(max) NULL,
    [Parafuso_Diametro] float NULL,
    [Parafuso_Linhas] int NULL,
    [Parafuso_Colunas] int NULL,
    [Parafuso_EspVertical] float NULL,
    [Parafuso_EspHorizontal] float NULL,
    [Parafuso_DistTopo] float NULL,
    [Parafuso_DeslLateral] float NULL,
    [Parafuso_Inverter] int NULL,
    [Parafuso_Montagem] int NULL,
    [Solda_Ancoragem_Tipo] int NULL,
    [Solda_Ancoragem_Espessura] float NULL,
    [Solda_Ancoragem_Local] int NULL,
    [Solda_Conexao_Tipo] int NULL,
    [Solda_Conexao_Espessura] float NULL,
    [Solda_Conexao_Local] int NULL,
    [Tubo_AjustarPonta] int NULL,
    [Tubo_Ultrapassagem] float NULL,
    [ChapaFundo] int NULL,
    [ChapaFundo_Espessura] float NULL
  );
END
IF COL_LENGTH('dbo.$tabela','Ancoragem_Posicao') IS NULL ALTER TABLE dbo.[$tabela] ADD [Ancoragem_Posicao] int NULL;
IF COL_LENGTH('dbo.$tabela','Ancoragem_Espessura') IS NULL ALTER TABLE dbo.[$tabela] ADD [Ancoragem_Espessura] float NULL;
IF COL_LENGTH('dbo.$tabela','Ancoragem_Altura') IS NULL ALTER TABLE dbo.[$tabela] ADD [Ancoragem_Altura] float NULL;
IF COL_LENGTH('dbo.$tabela','Ancoragem_DistTopo') IS NULL ALTER TABLE dbo.[$tabela] ADD [Ancoragem_DistTopo] float NULL;
IF COL_LENGTH('dbo.$tabela','Ancoragem_Largura') IS NULL ALTER TABLE dbo.[$tabela] ADD [Ancoragem_Largura] float NULL;
IF COL_LENGTH('dbo.$tabela','Alturas_Iguais') IS NULL ALTER TABLE dbo.[$tabela] ADD [Alturas_Iguais] int NULL;
IF COL_LENGTH('dbo.$tabela','Conexao_Espessura') IS NULL ALTER TABLE dbo.[$tabela] ADD [Conexao_Espessura] float NULL;
IF COL_LENGTH('dbo.$tabela','Conexao_Altura') IS NULL ALTER TABLE dbo.[$tabela] ADD [Conexao_Altura] float NULL;
IF COL_LENGTH('dbo.$tabela','Conexao_DistTopo') IS NULL ALTER TABLE dbo.[$tabela] ADD [Conexao_DistTopo] float NULL;
IF COL_LENGTH('dbo.$tabela','Sobreposicao') IS NULL ALTER TABLE dbo.[$tabela] ADD [Sobreposicao] float NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_Norma') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_Norma] nvarchar(max) NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_Grau') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_Grau] nvarchar(max) NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_Conjunto') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_Conjunto] nvarchar(max) NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_Diametro') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_Diametro] float NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_Linhas') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_Linhas] int NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_Colunas') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_Colunas] int NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_EspVertical') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_EspVertical] float NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_EspHorizontal') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_EspHorizontal] float NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_DistTopo') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_DistTopo] float NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_DeslLateral') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_DeslLateral] float NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_Inverter') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_Inverter] int NULL;
IF COL_LENGTH('dbo.$tabela','Parafuso_Montagem') IS NULL ALTER TABLE dbo.[$tabela] ADD [Parafuso_Montagem] int NULL;
IF COL_LENGTH('dbo.$tabela','Solda_Ancoragem_Tipo') IS NULL ALTER TABLE dbo.[$tabela] ADD [Solda_Ancoragem_Tipo] int NULL;
IF COL_LENGTH('dbo.$tabela','Solda_Ancoragem_Espessura') IS NULL ALTER TABLE dbo.[$tabela] ADD [Solda_Ancoragem_Espessura] float NULL;
IF COL_LENGTH('dbo.$tabela','Solda_Ancoragem_Local') IS NULL ALTER TABLE dbo.[$tabela] ADD [Solda_Ancoragem_Local] int NULL;
IF COL_LENGTH('dbo.$tabela','Solda_Conexao_Tipo') IS NULL ALTER TABLE dbo.[$tabela] ADD [Solda_Conexao_Tipo] int NULL;
IF COL_LENGTH('dbo.$tabela','Solda_Conexao_Espessura') IS NULL ALTER TABLE dbo.[$tabela] ADD [Solda_Conexao_Espessura] float NULL;
IF COL_LENGTH('dbo.$tabela','Solda_Conexao_Local') IS NULL ALTER TABLE dbo.[$tabela] ADD [Solda_Conexao_Local] int NULL;
IF COL_LENGTH('dbo.$tabela','Tubo_AjustarPonta') IS NULL ALTER TABLE dbo.[$tabela] ADD [Tubo_AjustarPonta] int NULL;
IF COL_LENGTH('dbo.$tabela','Tubo_Ultrapassagem') IS NULL ALTER TABLE dbo.[$tabela] ADD [Tubo_Ultrapassagem] float NULL;
IF COL_LENGTH('dbo.$tabela','ChapaFundo') IS NULL ALTER TABLE dbo.[$tabela] ADD [ChapaFundo] int NULL;
IF COL_LENGTH('dbo.$tabela','ChapaFundo_Espessura') IS NULL ALTER TABLE dbo.[$tabela] ADD [ChapaFundo_Espessura] float NULL;
UPDATE HRLDefinition SET LibraryTable = '$tabela' WHERE [Key] = 150000 AND InternalName = 'CTT_Handrail';
"@

$desfazer = @"
UPDATE HRLDefinition SET LibraryTable = NULL WHERE [Key] = 150000 AND InternalName = 'CTT_Handrail';
IF OBJECT_ID('dbo.$tabela') IS NOT NULL DROP TABLE dbo.[$tabela];
"@

function Backup-Library($con, $sufixo) {
  $da = New-Object System.Data.SqlClient.SqlDataAdapter ("IF OBJECT_ID('dbo.$tabela') IS NOT NULL SELECT * FROM [$tabela] ORDER BY [Key]", $con)
  $t = New-Object System.Data.DataTable; [void]$da.Fill($t)
  if ($t.Rows.Count -gt 0) {
    $csv = Join-Path $PSScriptRoot "library-ctt-handrail-$sufixo-$(Get-Date -Format yyyyMMdd-HHmmss).csv"
    $t | Export-Csv $csv -NoTypeInformation -Encoding UTF8
    Write-Host "$($t.Rows.Count) pre-definicoes salvas em $csv" -ForegroundColor Yellow
  }
}

$con = New-Object System.Data.SqlClient.SqlConnection $cs
try {
  $con.Open()
  if ($Exportar) { Backup-Library $con "export"; return }
  Backup-Library $con "backup"

  $tx = $con.BeginTransaction()
  $cmd = $con.CreateCommand(); $cmd.Transaction = $tx
  $cmd.CommandText = $(if ($Desfazer) { $desfazer } else { $criar })
  [void]$cmd.ExecuteNonQuery()
  $tx.Commit()
  Write-Host $(if ($Desfazer) { "Library removida." } else { "Library criada/atualizada." }) -ForegroundColor Green

  foreach ($q in "SELECT [Key],InternalName,LibraryTable FROM HRLDefinition WHERE [Key]=150000",
                 "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='$tabela' ORDER BY ORDINAL_POSITION",
                 "IF OBJECT_ID('dbo.$tabela') IS NOT NULL SELECT [Key],[Comment] FROM [$tabela]") {
    $da = New-Object System.Data.SqlClient.SqlDataAdapter ($q, $con)
    $t = New-Object System.Data.DataTable; [void]$da.Fill($t)
    $t | Format-Table -AutoSize | Out-String | Write-Host
  }
} catch { if ($tx) { $tx.Rollback() }; Write-Host "ERRO (nada foi alterado): $($_.Exception.Message)" -ForegroundColor Red }
finally { $con.Close(); & $loc stop AdvanceSteel2025 | Out-Null }

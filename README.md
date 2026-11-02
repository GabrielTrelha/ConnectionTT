# ConnectionTT

Biblioteca de **conexões paramétricas para o Autodesk Advance Steel**, feitas com a API oficial de *.NET Custom Connections* (`IRule`). As conexões aparecem no sistema nativo do Advance Steel, têm janela própria de parâmetros, gravam os parâmetros dentro do DWG e continuam funcionando quando o desenho é aberto em outra máquina que tenha o ConnectionTT instalado.

O pacote também traz um painel na ribbon, uma paleta de conexões e um servidor **MCP** (Model Context Protocol), que permite a clientes de IA (Claude, opencode, Cursor...) consultar o modelo aberto.

> Projeto independente. Não é afiliado, patrocinado nem endossado pela Autodesk ou pela Graitec. Autodesk, AutoCAD e Advance Steel são marcas da Autodesk, Inc.

---

## Status

| Parte | Situação |
|---|---|
| **Suporte de corrimão** (`CTT_Handrail`) | Em teste no Advance Steel 2026. Chapa de ancoragem soldada na alma da viga, chapa de conexão soldada no tubo, parafusos atravessando as duas chapas, chapa de fundo opcional e ajuste da ponta do tubo. |
| Library (pré-definições da conexão) | Implementada, ainda em validação. |
| Ribbon e paleta de conexões | Funcionando (aba **Plug-ins**, comando `CTTCONEXOES`). |
| MCP: sessão e conexões | Funcionando: `get_session_info`, `list_connection_templates`, `create_connection_template`, `create_connection`, `export_connection_templates`, `repair_connection_templates`. |
| MCP: detalhamento, listas, configuração e modelo | **Ainda não implementado.** As ferramentas estão declaradas, mas respondem com erro. |

Versões do Advance Steel: **somente a 2026 foi testada.** A arquitetura prevê outras versões, mas cada uma precisa ser validada antes de entrar no instalador.

---

## Instalação

1. Baixe `ConnectionTT-Setup-<versão>.exe` na página de **Releases** do repositório.
2. **Feche o Advance Steel.**
3. Execute o instalador. Ele pede permissão de administrador.
4. Abra o Advance Steel e use o botão **Conexões** na aba **Plug-ins**, ou digite:
   ```
   AstM4CrConByVb CTT_Handrail
   ```
   Selecione a viga e depois o tubo do corrimão.

### O que o instalador faz

- Copia o pacote para `C:\Program Files\Autodesk\ApplicationPlugins\ConnectionTT.bundle\`. O AutoCAD carrega esse pacote sozinho.
- Copia `ConnectionTT.Joints.dll` para a pasta do Advance Steel (`C:\Program Files\Autodesk\AutoCAD 2026\ADVS\`).
- Registra as conexões no banco **AstorRules** do Advance Steel (tabelas `RulesDllSigned` e `HRLDefinition`) e cria a tabela da Library, se faltar.

**Antes de alterar o banco, o instalador faz uma cópia completa dele** em `C:\ProgramData\ConnectionTT\backups\`. Ele confere o schema das tabelas e não sobrescreve registros de outros plugins. Se algo der errado, nada é gravado. O log fica em `C:\ProgramData\ConnectionTT\setup.log`.

### Desinstalação

Pelo Windows: **Configurações > Aplicativos > ConnectionTT > Desinstalar** (com o Advance Steel fechado). O registro das conexões é removido e a DLL sai da pasta ADVS. As **pré-definições da Library são preservadas** para uma reinstalação, e os backups do banco ficam em `C:\ProgramData\ConnectionTT\backups\`.

### A conexão sumiu depois de uma atualização do Advance Steel?

Uma atualização ou reparo do Advance Steel pode recriar o banco AstorRules e apagar o registro. O sintoma é que `AstM4CrConByVb CTT_Handrail` abre só uma janela genérica. Para registrar de novo, com o Advance Steel fechado, abra o PowerShell como administrador e rode:

```powershell
& "C:\Program Files\Autodesk\ApplicationPlugins\ConnectionTT.bundle\Contents\Setup\ConnectionTT.Setup.exe" register --joints "C:\Program Files\Autodesk\ApplicationPlugins\ConnectionTT.bundle\Contents\Joints\ConnectionTT.Joints.dll"
```

Outros comandos do `ConnectionTT.Setup.exe`:

| Comando | Para quê |
|---|---|
| `status` | Mostra a instalação do Advance Steel encontrada e o que está registrado. |
| `register [--joints <dll>]` | Registra as conexões (com backup). |
| `unregister [--purge-library]` | Remove o registro. Com `--purge-library`, apaga também as pré-definições. |
| `backups` | Lista os backups do banco. |
| `restore <pasta>` | Devolve o banco ao estado de um backup. |

---

## Usando o MCP

O servidor MCP é `ConnectionTT.Bridge.exe`. Ele conversa com o plugin dentro do Advance Steel por um *named pipe*, então o Advance Steel precisa estar aberto. Ative-o pelo botão **MCP** do painel ConnectionTT.

**Claude Desktop** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "ConnectionTT": {
      "command": "C:\\Program Files\\Autodesk\\ApplicationPlugins\\ConnectionTT.bundle\\Contents\\Bridge\\ConnectionTT.Bridge.exe"
    }
  }
}
```

**opencode** (`opencode.json`):

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "ConnectionTT": {
      "type": "local",
      "command": ["C:\\Program Files\\Autodesk\\ApplicationPlugins\\ConnectionTT.bundle\\Contents\\Bridge\\ConnectionTT.Bridge.exe"],
      "enabled": true
    }
  }
}
```

---

## Compilando

Requisitos:

- Windows x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Advance Steel 2026 instalado. As DLLs da API não são distribuídas pela Autodesk como pacote, então o Plugin e o Joints só compilam numa máquina com o Advance Steel.
- [Inno Setup 6](https://jrsoftware.org/isinfo.php), só para gerar o instalador: `winget install JRSoftware.InnoSetup`

```powershell
.\publish.ps1
```

Gera em `artifacts\` a pasta `ConnectionTT.bundle`, o ZIP e `ConnectionTT-Setup-<versão>.exe`. Se o AutoCAD não estiver no caminho padrão:

```powershell
.\publish.ps1 -AutoCADPath "D:\Autodesk\AutoCAD 2026" -AdvanceSteelPath "D:\Autodesk\AutoCAD 2026\ADVS"
```

O Bridge, o Core, o IpcContracts e o Setup não dependem do Advance Steel e compilam em qualquer máquina com o SDK:

```powershell
dotnet build src\ConnectionTT.Bridge\ConnectionTT.Bridge.csproj
dotnet build src\ConnectionTT.Setup\ConnectionTT.Setup.csproj
```

A versão do pacote fica em `Directory.Build.props`.

---

## Estrutura do repositório

```
ConnectionTT/
├── Bundle/ConnectionTT.bundle/   PackageContents.xml (manifesto do Autodesk ApplicationPlugins)
├── connections/                  definições de conexão em JSON (experimental)
├── docs/                         arquitetura e decisões de design
├── installer/ConnectionTT.iss    script do instalador (Inno Setup)
├── scripts/                      ferramentas de desenvolvimento (Library da CTT_Handrail)
├── src/
│   ├── ConnectionTT.Joints/      conexões .NET (IRule), ex.: HandrailBracketRule   (net8.0)
│   ├── ConnectionTT.Plugin/      ribbon, paleta, servidor do pipe para o MCP       (net8.0-windows)
│   ├── ConnectionTT.Bridge/      servidor MCP (stdio JSON-RPC)                      (net8.0)
│   ├── ConnectionTT.Setup/       registro no AstorRules usado pelo instalador      (net8.0-windows)
│   ├── ConnectionTT.Core/        catálogo de ferramentas do MCP                    (netstandard2.0)
│   └── ConnectionTT.IpcContracts/ mensagens trocadas pelo pipe                     (netstandard2.0)
├── Directory.Build.props         versão
└── publish.ps1                   gera o bundle e o instalador
```

### Como uma conexão funciona

Cada conexão é uma classe que implementa `Autodesk.AdvanceSteel.ConstructionTypes.IRule`:

| Método | Função |
|---|---|
| `Query` | pede a seleção dos elementos (ex.: viga e tubo) |
| `GetRulePages` | monta a janela de parâmetros |
| `CreateObjects` | cria chapas, parafusos e soldas e os associa ao joint (`SetCreatedObjects`) |
| `Save` / `Load` | gravam e leem os parâmetros no próprio objeto da conexão, dentro do DWG |
| `GetTableName` | tabela da Library (pré-definições) |

O Advance Steel encontra a classe pelo registro no AstorRules. Para acrescentar uma conexão nova, crie a classe em `ConnectionTT.Joints` e adicione uma entrada em `src/ConnectionTT.Setup/RuleCatalog.cs`, com uma `Key` livre acima de 150000, um `InternalName` único e o nome completo da classe.

### Observação técnica: o banco AstorRules no Advance Steel 2026

O Advance Steel 2026 abre seus bancos pela instância `(localdb)\AdvanceSteel2025` do SQL LocalDB, apesar do nome. Se a instância `AdvanceSteel2026` abrir o mesmo arquivo, ele fica travado e o Advance Steel mostra erros de conexão com o banco. O `ConnectionTT.Setup` usa a instância correta e para as duas ao terminar. Quem for mexer no banco por outros meios deve fazer o mesmo, com o Advance Steel fechado.

---

## Licença

MIT. Veja [`LICENSE`](LICENSE).

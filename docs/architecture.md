# Arquitetura — Connection TT

Documento de design do projeto. Duas frentes, um único bundle instalável no Advance Steel.

---

## 1. Visão geral

```
┌─────────────────────┐          ┌──────────────────────────────┐
│  AI client           │   stdio   │  ConnectionTT.Bridge          │
│  (opencode, Claude,  │ ◄───────► │  - servidor MCP (JSON-RPC)    │
│   Cursor, ChatGPT)   │          │  - declara as tools (catálogo) │
└─────────────────────┘          │  - roteia tools/call           │
                                  └───────────────┬──────────────┘
                                                  │ Named Pipe (JSON)
                                  ┌───────────────▼──────────────┐
                                  │  ConnectionTT.Plugin          │
                                  │  (DLL dentro do Adv. Steel)   │
                                  │  ┌──────────────────────────┐ │
                                  │  │ Frente 1: ConnectionEngine│ │
                                  │  └──────────────────────────┘ │
                                  │  ┌──────────────────────────┐ │
                                  │  │ Frente 2: detalhamento +  │ │
                                  │  │ listas + config (tools)   │ │
                                  │  └──────────────────────────┘ │
                                  └───────────────────────────────┘
```

Por que dois processos? O cliente de IA inicia o Bridge como processo separado.
O Bridge não consegue tocar a API do Advance Steel diretamente — ela só existe
dentro do processo do Advance Steel. Por isso existe o Plugin, e os dois se
falam por Named Pipe.

---

## 2. Frente 1 — Conexões portáveis

### 2.1 O problema

A ferramenta nativa *Create connection template* (Tool Palette → Custom
Connections) grava o template de conexão no banco de dados **local** da
instalação (`AstorDetails` / pasta de dados), **fora do DWG**.

- Máquina A: template criado → fica no `AstorDetails` de A.
- DWG guarda apenas a **referência** (nome/GUID + parâmetros) do template.
- Máquina B: a referência existe, mas o template não → a conexão não resolve → erro.

### 2.2 A solução

Tornar a definição **autocontida**, com duas estratégias combinadas:

**Estratégia A — definição como dado versionado.**
Cada conexão é definida num arquivo JSON (pasta `connections/`). O plugin lê
essas definições em runtime. Como o plugin é instalado de forma idêntica em
todas as máquinas, todas têm as mesmas definições.

**Estratégia B — manifesto embutido no DWG (autorreparo).**
Quando o plugin cria/usa um template, ele serializa a definição completa e a
grava dentro do próprio DWG (XRecord / Extension Dictionary) com:

```
nome + versão + hash + definição (payload)
```

Ao abrir o documento (evento de startup), o plugin:

1. Lê o manifesto do DWG.
2. Verifica se o template existe no `AstorDetails` local.
3. Se faltar ou a versão divergir → **reimporta/reconstrói** o template via API.
4. Força a reconexão das joints que o referenciam.

Resultado: o DWG fica 100% autocontido. Qualquer máquina com o plugin instalado
abre o modelo e o template se auto-instala.

> **A validar na API:** os métodos exatos para importar/registrar um template de
> conexão programaticamente (Joint Design / AstorDetails). É o único trecho de
> API que precisa ser confirmado na documentação do Advance Steel quando formos
> implementar.

### 2.3 Esquema de definição (ponto de partida)

Exemplo em `connections/endplate.json`. Uma definição tem:

- `name`, `version`, `description`
- `inputs` (parâmetros com tipo/default)
- `generator` (referência à rotina que produz a geometria)

---

## 3. Frente 2 — MCP para detalhamento

O Bridge expõe as ferramentas como tools MCP. O catálogo é a fonte única de
verdade (`ConnectionTT.Core/ToolCatalog.cs`) e está organizado por categoria:

| Módulo | Tools | Escopo |
|---|---|---|
| `detailing` | `get_detail_drawing_info`, `list_detail_views`, `list_detail_dimensions`, `list_detail_labels`, `get_detail_view_geometry`, `add_detail_dimension`, `add_detail_label`, `save_annotation_template`, `apply_annotation_template` | desenho de detalhamento aberto |
| `lists` | `export_material_list`, `get_numbering_status`, `run_steel_numbering` | listas de peças e numeração |
| `config` | `get_steel_project_data`, `set_steel_project_data`, `list_drawing_processes`, `list_drawing_styles` | configuração do programa |
| `model` | `list_steel_objects`, `get_steel_object`, `get_selected_steel_objects`, `set_steel_object_properties` | leitura/edição do modelo |
| `connection` | `list_connection_templates`, `create_connection`, `export_connection_templates`, `repair_connection_templates` | frente 1 |

---

## 4. Protocolo MCP (Bridge)

O Bridge fala **JSON-RPC 2.0 sobre stdio**, uma mensagem JSON por linha. Métodos
implementados:

- `initialize` → retorna `protocolVersion`, `capabilities.tools`, `serverInfo`
- `notifications/initialized` → sem resposta
- `ping` → `{}`
- `tools/list` → lista as tools do catálogo com `inputSchema` (JSON Schema)
- `tools/call` → encaminha ao Plugin via pipe e devolve `{ content, isError }`
- `notifications/cancelled` → sem resposta

---

## 5. IPC (Named Pipe)

- Pipe name: `ConnectionTT.AdvanceSteel.v1` (definido em `ConnectionTT.Core/PipeNames.cs`)
- Mensagem request: `{ "id", "tool", "args" }`
- Mensagem response: `{ "id", "ok", "result", "error" }`
- Uma mensagem JSON por linha, mesma convenção do MCP.

O Plugin mantém um `NamedPipeServerStream` em thread de background, aceita uma
conexão por vez, despacha para o `ToolDispatcher` e responde.

---

## 6. Bundle do Autodesk

```
Bundle/ConnectionTT.bundle/
├── PackageContents.xml
└── Contents/
    ├── ConnectionTT.Plugin.dll      (compilado de src/.../Plugin)
    ├── Connections/*.json           (definições da frente 1)
    └── Bridge/ConnectionTT.Bridge.exe  (compilado de src/.../Bridge)
```

O `PackageContents.xml` declara o plugin com `LoadOnAutoCADStartup=True`. O
passo de *publish* copia os artefatos compilados para `Contents/`.

---

## 7. Roadmap

1. ✅ Esqueleto + catálogo de tools + IPC + protocolo MCP (este repositório)
2. Front 2 read-only: `list_steel_objects`, `get_steel_object`, `export_material_list`, `get_numbering_status`
3. Front 2 detalhamento: leitura de views/dimensões/labels do desenho aberto
4. Front 1: capturar definição → gravar manifesto no DWG → reimportar ao abrir
5. Front 2 escrita: criar dimensões/labels, aplicar templates, editar propriedades
6. Empacotar tudo no bundle + instalador

---

## 8. Decisões e trade-offs

- **netstandard2.0** para `Core` e `IpcContracts`: permite compartilhar código
  entre o Bridge (net8) e o Plugin (net8, exigido pelo AutoCAD/Advance Steel 2022+).
- **Entrada do plugin via API do AutoCAD**: o ponto de entrada é
  `Autodesk.AutoCAD.Runtime.IExtensionApplication` + `[ExtensionApplication]`
  (assemblies `acmgd`/`accoremgd`/`acdbmgd`), e as classes de aço ficam em
  `ASObjectsMgd.dll` (`Autodesk.AdvanceSteel.CADAccess`, `.Modelling`, `.Connection`).
  Confirmado contra o plugin AUTOM8LABS instalado nesta máquina.
- **Tool catalog centralizado**: documentação e `tools/list` nunca divergem.
- **Read-only primeiro**: as tools de escrita ficam para depois, reduzindo risco.

---

## 9. API do Advance Steel (confirmada via REA)

Verificado estaticamente nos assemblies de `C:\Program Files\Autodesk\AutoCAD 2026\ADVS`:

**Acesso ao banco atual**
- `Autodesk.AdvanceSteel.DocumentManagement.CADDocumentManager.GetCurrentDatabase() -> OpenDatabase`
- `DocumentManager.GetCurrentDocument() -> Document`
- `Document.Databases`, `registerDatabase(Database)`, `deregisterDatabase(OpenDatabase)`

**Conexões (Frente 1)**
- `UserDefinedConnection.CreateTemplate(OpenDatabase db, string name, Beam..., List<ObjectId>)`
- `UserDefinedConnectionInstance.CreateFrom(OpenDatabase db, string name, List<ObjectId>[, List<eEnd>])`
- `UserDefinedConnection.Name` / `UserDefinedConnectionInstance.Name`

**Serializacao**
- `IJoint.Save() -> IFiler` e `IJoint.Load(IFiler, FilerLoadOption)`
- `IFiler`: `WriteVersion`/`ReadVersion`, `WriteItem`/`ReadItem`, `GetItems()`
- Observacao: `UserDefinedConnection`/`UserDefinedConnectionInstance` **nao** implementam `IJoint`; o unico implementador direto e `UserAutoConstructionObject`.

**Evidencia do armazenamento**
- O IL de `CreateTemplate` chama `OpenDatabase.get_Native()` e passa o ponteiro para a rotina nativa `ASObjectsAPI.UserDefinedConnection.createTemplate`. Logo, o `OpenDatabase` informado e o banco usado na criacao do template. Passar o DB do desenho (`GetCurrentDatabase()`) torna o template portavel com o DWG.

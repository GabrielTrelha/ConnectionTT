# Gera o bundle completo do ConnectionTT e o instalador .exe.
#
#   .\publish.ps1                      -> tudo (bundle + ZIP + instalador, se o Inno Setup estiver instalado)
#   .\publish.ps1 -NoInstaller         -> so o bundle e o ZIP
#   .\publish.ps1 -AutoCADPath ... -AdvanceSteelPath ...   (se o AutoCAD nao estiver no caminho padrao)
#
# Resultado em artifacts\:
#   ConnectionTT.bundle\               pasta pronta (igual ao que o instalador copia)
#   ConnectionTT.bundle.zip
#   ConnectionTT-Setup-<versao>.exe    instalador (precisa do Inno Setup 6: winget install JRSoftware.InnoSetup)
#
# A versao vem de Directory.Build.props.
param(
    [string]$AutoCADPath = "C:\Program Files\Autodesk\AutoCAD 2026",
    [string]$AdvanceSteelPath = "C:\Program Files\Autodesk\AutoCAD 2026\ADVS",
    [switch]$NoZip,
    [switch]$NoInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifacts = Join-Path $root "artifacts"
$bundle = Join-Path $artifacts "ConnectionTT.bundle"
$contents = Join-Path $bundle "Contents"

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Done($text) { Write-Host "    $text" -ForegroundColor Green }
function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "Falhou: dotnet $($args -join ' ')" }
}

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup.Version | Select-Object -First 1).Trim()
Write-Host "ConnectionTT $version" -ForegroundColor White

# --------------------------------------------------------------- pre-requisitos
$acmgd = Join-Path $AutoCADPath "acmgd.dll"
$asObjects = Join-Path $AdvanceSteelPath "ASObjectsMgd.dll"
if (-not (Test-Path -LiteralPath $acmgd) -or -not (Test-Path -LiteralPath $asObjects)) {
    throw "AutoCAD/Advance Steel nao encontrado (procurei $acmgd e $asObjects). Informe -AutoCADPath e -AdvanceSteelPath."
}

if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Recurse -Force }
New-Item -ItemType Directory -Force -Path $contents | Out-Null

# --------------------------------------------------------------- Bridge (servidor MCP)
Step "Bridge (servidor MCP)"
Invoke-Dotnet publish (Join-Path $root "src\ConnectionTT.Bridge\ConnectionTT.Bridge.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $contents "Bridge")
Done "Contents\Bridge"

# --------------------------------------------------------------- Plugin (ribbon, paleta, pipe do MCP)
Step "Plugin"
Invoke-Dotnet build (Join-Path $root "src\ConnectionTT.Plugin\ConnectionTT.Plugin.csproj") `
    -c Release -p:AutoCADPath="$AutoCADPath" -p:AdvanceSteelPath="$AdvanceSteelPath"
$pluginBin = Join-Path $root "src\ConnectionTT.Plugin\bin\Release\net8.0-windows"
$pluginOut = Join-Path $contents "2026"
New-Item -ItemType Directory -Force -Path $pluginOut | Out-Null
foreach ($f in "ConnectionTT.Plugin.dll", "ConnectionTT.Plugin.deps.json", "ConnectionTT.Plugin.runtimeconfig.json",
               "ConnectionTT.Core.dll", "ConnectionTT.IpcContracts.dll") {
    Copy-Item -LiteralPath (Join-Path $pluginBin $f) -Destination $pluginOut -Force
}
Copy-Item -LiteralPath (Join-Path $pluginBin "Palette") -Destination $pluginOut -Recurse -Force
Done "Contents\2026 (com Palette)"

# --------------------------------------------------------------- Joints (conexoes .NET / IRule)
Step "Joints (conexoes)"
Invoke-Dotnet build (Join-Path $root "src\ConnectionTT.Joints\ConnectionTT.Joints.csproj") `
    -c Release -p:AdvanceSteelPath="$AdvanceSteelPath"
$jointsOut = Join-Path $contents "Joints"
New-Item -ItemType Directory -Force -Path $jointsOut | Out-Null
Copy-Item -LiteralPath (Join-Path $root "src\ConnectionTT.Joints\bin\Release\net8.0\ConnectionTT.Joints.dll") -Destination $jointsOut -Force
Done "Contents\Joints (o instalador copia a DLL para a pasta ADVS)"

# --------------------------------------------------------------- Setup (registro no AstorRules)
Step "ConnectionTT.Setup"
Invoke-Dotnet publish (Join-Path $root "src\ConnectionTT.Setup\ConnectionTT.Setup.csproj") -c Release -o (Join-Path $contents "Setup")
Remove-Item (Join-Path $contents "Setup\*.pdb") -ErrorAction SilentlyContinue
Done "Contents\Setup"

# --------------------------------------------------------------- manifesto e definicoes
Step "PackageContents.xml"
$pkg = Get-Content -Raw (Join-Path $root "Bundle\ConnectionTT.bundle\PackageContents.xml")
$pkg = $pkg -replace 'AppVersion="[^"]*"', "AppVersion=""$version""" -replace '(<ComponentEntry[^>]*?)Version="[^"]*"', "`$1Version=""$version"""
Set-Content -LiteralPath (Join-Path $bundle "PackageContents.xml") -Value $pkg -Encoding UTF8
New-Item -ItemType Directory -Force -Path (Join-Path $contents "Connections") | Out-Null
Copy-Item -Path (Join-Path $root "connections\*.json") -Destination (Join-Path $contents "Connections") -Force
Done "versao $version"

# --------------------------------------------------------------- ZIP
if (-not $NoZip) {
    Step "ZIP"
    $zip = Join-Path $artifacts "ConnectionTT.bundle.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path $bundle -DestinationPath $zip
    Done $zip
}

# --------------------------------------------------------------- instalador
if (-not $NoInstaller) {
    Step "Instalador (Inno Setup)"
    $iscc = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

    if (-not $iscc) {
        Write-Warning "Inno Setup 6 nao encontrado; instalador nao gerado. Instale com:  winget install JRSoftware.InnoSetup"
    }
    else {
        & $iscc /Q "/DAppVersion=$version" "/DBundleDir=$bundle" "/DOutputDir=$artifacts" (Join-Path $root "installer\ConnectionTT.iss")
        if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o instalador." }
        Done (Join-Path $artifacts "ConnectionTT-Setup-$version.exe")
    }
}

Write-Host ""
Write-Host "Pronto." -ForegroundColor Green

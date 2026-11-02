; Instalador do ConnectionTT (Inno Setup 6). Gerado pelo publish.ps1, que passa /DAppVersion e /DBundleDir.
; Para compilar a mao:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ConnectionTT.iss
;
; O que o instalador faz:
;   1. Confere se o AutoCAD/Advance Steel esta fechado e se o runtime do .NET 8 existe.
;   2. Copia o bundle para C:\Program Files\Autodesk\ApplicationPlugins\ConnectionTT.bundle
;      (ribbon, paleta, MCP Bridge, ConnectionTT.Setup e a DLL das conexoes).
;   3. Roda "ConnectionTT.Setup register": copia o ConnectionTT.Joints.dll para a pasta ADVS e grava
;      o registro no AstorRules, com backup do banco antes (ver src\ConnectionTT.Setup).
; A desinstalacao (Painel de Controle > Programas) roda "ConnectionTT.Setup unregister" antes de apagar os arquivos.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef BundleDir
  #define BundleDir "..\artifacts\ConnectionTT.bundle"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
; Nunca mude o AppId: e por ele que o Windows reconhece atualizacoes e a desinstalacao.
AppId={{4937BEA7-7236-4215-B736-EEDE9F915888}
AppName=ConnectionTT
AppVersion={#AppVersion}
AppVerName=ConnectionTT {#AppVersion} para Advance Steel
AppPublisher=ConnectionTT
DefaultDirName={commonpf64}\Autodesk\ApplicationPlugins\ConnectionTT.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=ConnectionTT-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=ConnectionTT {#AppVersion} para Advance Steel
SetupLogging=yes
CloseApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#BundleDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Code]
function IsAcadRunning(): Boolean;
var
  ResultCode: Integer;
begin
  { find devolve 0 quando acha acad.exe na lista de processos }
  Result := Exec(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq acad.exe" /NH | find /I "acad.exe" >nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function WaitForAcadClosed(): Boolean;
begin
  Result := True;
  while IsAcadRunning() do
  begin
    if MsgBox('O AutoCAD / Advance Steel est'#225' aberto.' + #13#10 +
              'Feche-o e clique em OK para continuar, ou em Cancelar para sair.',
              mbError, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function HasDotNet8(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App\8.*'), FindRec);
  if Result then FindClose(FindRec);
end;

function InitializeSetup(): Boolean;
begin
  Result := WaitForAcadClosed();
  if Result and not HasDotNet8() then
  begin
    MsgBox('O runtime do .NET 8 n'#227'o foi encontrado.' + #13#10 +
           'Ele vem com o Advance Steel 2026; confira se o Advance Steel est'#225' instalado nesta m'#225'quina.',
           mbError, MB_OK);
    Result := False;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := WaitForAcadClosed();
end;

function RunSetupTool(Params: String): Integer;
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{app}\Contents\Setup\ConnectionTT.Setup.exe'), Params, '',
              SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

function SetupLogPath(): String;
begin
  Result := ExpandConstant('{commonappdata}\ConnectionTT\setup.log');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption := 'Registrando as conex'#245'es no Advance Steel...';
    Code := RunSetupTool('register --joints "' + ExpandConstant('{app}\Contents\Joints\ConnectionTT.Joints.dll') + '"');
    if Code = 4 then
      MsgBox('Os arquivos foram instalados, mas o Advance Steel 2026 n'#227'o foi encontrado nesta m'#225'quina.' + #13#10 +
             'As conex'#245'es n'#227'o foram registradas. Log: ' + SetupLogPath(), mbError, MB_OK)
    else if Code <> 0 then
      MsgBox('Os arquivos foram instalados, mas o registro das conex'#245'es falhou (c'#243'digo ' + IntToStr(Code) + ').' + #13#10 +
             'Nada foi alterado no banco. Veja o log: ' + SetupLogPath(), mbError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Code := RunSetupTool('unregister');
    if Code <> 0 then
      MsgBox('N'#227'o foi poss'#237'vel remover o registro das conex'#245'es (c'#243'digo ' + IntToStr(Code) + ').' + #13#10 +
             'Os arquivos ser'#227'o removidos mesmo assim. Veja o log: ' + SetupLogPath(), mbError, MB_OK);
  end;
end;

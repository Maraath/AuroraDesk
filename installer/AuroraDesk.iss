; Instalador do Aurora Desk (Inno Setup 6).
; Gerado por tools\instalador.ps1, que publica o app AUTOCONTIDO (com o .NET 10 Desktop Runtime
; embutido) em artifacts\publish-autocontido e compila este script.
;
; - Instala só para o usuário atual, sem pedir administrador (%LOCALAPPDATA%\Programs\Aurora Desk).
; - Não precisa de internet nem de .NET instalado no PC.
; - Antes de atualizar ou desinstalar, fecha o app com calma (a barra de tarefas do Windows volta).
; - Ao desinstalar, remove a inicialização com o Windows e pergunta se apaga as configurações.

#ifndef AppVersion
  #define AppVersion "0.1.3"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish-autocontido"
#endif

[Setup]
AppId={{6F3C2A8E-4B1D-4E7A-9C55-AE21D3B7F0C4}
AppName=Aurora Desk
AppVersion={#AppVersion}
AppVerName=Aurora Desk {#AppVersion}
AppPublisher=Maraath
AppPublisherURL=https://github.com/Maraath/AuroraDesk
AppSupportURL=https://github.com/Maraath/AuroraDesk/issues
AppUpdatesURL=https://github.com/Maraath/AuroraDesk/releases
AppComments=Widgets de área de trabalho, dock e temas para o Windows 11, em um processo leve.
DefaultDirName={autopf}\Aurora Desk
DefaultGroupName=Aurora Desk
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\instalador
OutputBaseFilename=AuroraDesk-Setup-{#AppVersion}
SetupIconFile=..\src\AuroraDesk\Assets\aurora.ico
UninstallDisplayIcon={app}\AuroraDesk.exe
UninstallDisplayName=Aurora Desk
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern
CloseApplications=no
SetupLogging=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "iniciar"; Description: "Iniciar o Aurora Desk com o Windows"; GroupDescription: "Opções:"
Name: "atalhodesktop"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Opções:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Aurora Desk"; Filename: "{app}\AuroraDesk.exe"; Comment: "Widgets, dock e temas"
Name: "{autoprograms}\Configurações do Aurora Desk"; Filename: "{app}\AuroraDesk.exe"; Parameters: "--configuracoes"
Name: "{autodesktop}\Aurora Desk"; Filename: "{app}\AuroraDesk.exe"; Tasks: atalhodesktop

[Registry]
; Mesmo formato que o app usa (Configurações > Geral > Iniciar com o Windows), para ele reconhecer.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AuroraDesk"; ValueData: """{app}\AuroraDesk.exe"""; Tasks: iniciar; Flags: uninsdeletevalue

[Run]
Filename: "{app}\AuroraDesk.exe"; Description: "Abrir o Aurora Desk agora"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  ControlWindowTitle = 'AuroraDesk.Controle';
  CommandExit = 2;

function RegisterWindowMessage(lpString: String): Cardinal;
  external 'RegisterWindowMessageW@user32.dll stdcall';

{ Pede para a instância aberta fechar (o mesmo que "AuroraDesk.exe --sair") e espera até 15 s.
  Fechando com calma, o app devolve a barra de tarefas e o espaço reservado do dock. }
function CloseAuroraDesk(): Boolean;
var
  Wnd: HWND;
  Msg: Cardinal;
  Tries: Integer;
begin
  Result := True;
  Wnd := FindWindowByWindowName(ControlWindowTitle);
  if Wnd = 0 then Exit;
  Msg := RegisterWindowMessage('AuroraDesk.Comando');
  PostMessage(Wnd, Msg, CommandExit, 0);
  Tries := 0;
  while (FindWindowByWindowName(ControlWindowTitle) <> 0) and (Tries < 150) do
  begin
    Sleep(100);
    Tries := Tries + 1;
  end;
  { O processo vigia da barra termina logo depois do app; espera para os arquivos ficarem livres. }
  Sleep(1500);
  Result := FindWindowByWindowName(ControlWindowTitle) = 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CloseAuroraDesk() then
    Result := 'Não foi possível fechar o Aurora Desk. Feche-o pelo clique direito no dock (Sair) e tente de novo.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not CloseAuroraDesk() then
  begin
    SuppressibleMsgBox('Feche o Aurora Desk (clique direito no dock > Sair) antes de desinstalar.', mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { O app também pode ter criado a entrada (Configurações > Geral). }
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AuroraDesk');
    if SuppressibleMsgBox('Apagar também as suas configurações, widgets, temas e fontes do Aurora Desk?' + #13#10 + #13#10 +
         '(Pastas %APPDATA%\AuroraDesk e %LOCALAPPDATA%\AuroraDesk. Escolha "Não" para manter e reaproveitar numa reinstalação.)',
         mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\AuroraDesk'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\AuroraDesk'), True, True, True);
    end;
  end;
end;

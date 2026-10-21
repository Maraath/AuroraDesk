<#
.SYNOPSIS
  Testa o instalador em modo silencioso: instala, abre, atualiza com o app aberto e desinstala,
  conferindo arquivos, atalhos, registro e a barra de tarefas. Mantém os dados do usuário.
#>
param([string]$Setup = (Get-ChildItem (Join-Path $PSScriptRoot '..\artifacts\instalador') -Filter 'AuroraDesk-Setup-*.exe' | Sort-Object LastWriteTime | Select-Object -Last 1).FullName)

$dir = Join-Path $env:LOCALAPPDATA 'Programs\Aurora Desk'
$exe = Join-Path $dir 'AuroraDesk.exe'
$atalho = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Aurora Desk.lnk'
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F3C2A8E-4B1D-4E7A-9C55-AE21D3B7F0C4}_is1'
Add-Type -Namespace I -Name T -MemberDefinition '[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t); [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);'
function Barra { [I.T]::IsWindowVisible([I.T]::FindWindow('Shell_TrayWnd', $null)) }
function AppDoInstalado { @(Get-CimInstance Win32_Process -Filter "Name='AuroraDesk.exe'" | Where-Object { $_.ExecutablePath -eq $exe -and $_.CommandLine -notmatch '--vigia' }).Count }
function Check($nome, $ok) { '{0,-52} {1}' -f $nome, $(if ($ok) { 'OK' } else { 'FALHA' }) }

"Instalador: $Setup"
$log = Join-Path $env:TEMP 'aurora-setup.log'
Start-Process $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS="iniciar"', "/LOG=`"$log`"" -Wait
Check 'Instalado em %LOCALAPPDATA%\Programs\Aurora Desk' (Test-Path $exe)
Check 'Runtime embutido (coreclr.dll e wpfgfx)' ((Test-Path (Join-Path $dir 'coreclr.dll')) -and (Get-ChildItem $dir -Filter 'wpfgfx*.dll'))
Check 'Atalho no menu Iniciar' (Test-Path $atalho)
Check 'Iniciar com o Windows (chave Run)' ((Get-ItemProperty $run -Name AuroraDesk -ErrorAction SilentlyContinue).AuroraDesk -eq "`"$exe`"")
Check 'Aparece em Aplicativos instalados' (Test-Path $uninstKey)

Start-Process $exe; Start-Sleep 7
Check 'App instalado abre' ((AppDoInstalado) -eq 1)
Check 'Barra do Windows oculta com o app aberto' (-not (Barra))

# Atualização com o app aberto: o instalador fecha o app com calma antes de copiar.
Start-Process $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS="iniciar"' -Wait
Check 'Atualização fechou o app e concluiu' ((AppDoInstalado) -eq 0 -and (Test-Path $exe))
Check 'Barra do Windows voltou ao fechar' (Barra)

Start-Process $exe; Start-Sleep 7
$uninst = (Get-ItemProperty $uninstKey).UninstallString.Trim('"')
Start-Process $uninst -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
Start-Sleep 3
Check 'Desinstalação fechou o app' ((AppDoInstalado) -eq 0)
Check 'Barra do Windows visível depois' (Barra)
Check 'Arquivos removidos' (-not (Test-Path $exe))
Check 'Atalho removido' (-not (Test-Path $atalho))
Check 'Chave Run removida' (-not (Get-ItemProperty $run -Name AuroraDesk -ErrorAction SilentlyContinue))
Check 'Registro de desinstalação removido' (-not (Test-Path $uninstKey))
Check 'Dados do usuário mantidos (resposta padrão: Não)' (Test-Path (Join-Path $env:APPDATA 'AuroraDesk\config.json'))

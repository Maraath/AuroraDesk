<#
.SYNOPSIS
  Testa a regra "barra do Windows oculta enquanto o Aurora Desk estiver aberto": abrir, fechar,
  encerrar à força (simula queda) e reiniciar o Explorer. Atenção: reinicia o explorer.exe.
#>
param([string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\publish\AuroraDesk.exe'))
$Exe = (Resolve-Path $Exe).Path
Add-Type -Namespace B -Name T -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public int msg; public uint edge; public RECT rc; public IntPtr lParam; }
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint m, ref APPBARDATA d);
[DllImport("user32.dll")] public static extern bool SystemParametersInfo(int a, int b, ref RECT r, int c);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@
[B.T]::SetProcessDPIAware() | Out-Null
$backup = Join-Path $env:APPDATA 'AuroraDesk\backup\barra-de-tarefas.json'

function Estado {
    $tray = [B.T]::FindWindow('Shell_TrayWnd', $null)
    $d = New-Object B.T+APPBARDATA; $d.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($d); $d.hWnd = $tray
    $st = [B.T]::SHAppBarMessage(4, [ref]$d).ToUInt32()
    $r = New-Object B.T+RECT; [B.T]::SystemParametersInfo(0x30, 0, [ref]$r, 0) | Out-Null
    $procs = @(Get-CimInstance Win32_Process -Filter "Name='AuroraDesk.exe'")
    [pscustomobject]@{
        BarraVisivel = [B.T]::IsWindowVisible($tray); Estado = $st; AreaBase = $r.B
        App = @($procs | Where-Object { $_.CommandLine -notmatch '--vigia' }).Count
        Vigia = @($procs | Where-Object { $_.CommandLine -match '--vigia' }).Count
        Backup = Test-Path $backup
    }
}
function Mostrar($rotulo, $esperaVisivel) {
    $e = Estado
    $ok = $e.BarraVisivel -eq $esperaVisivel
    '{0,-36} barra visível={1,-5} estado={2} área até y={3,-5} app={4} vigia={5} backup={6}  {7}' -f $rotulo, $e.BarraVisivel, $e.Estado, $e.AreaBase, $e.App, $e.Vigia, $e.Backup, $(if ($ok) { 'OK' } else { 'FALHA' })
}
function Parar { & $Exe --sair; Start-Sleep 3 }

Get-Process AuroraDesk -ErrorAction SilentlyContinue | ForEach-Object { Parar }
$original = Estado
Mostrar 'Antes (sem o app)' $true

Start-Process $Exe; Start-Sleep 5
Mostrar 'App aberto' $false

Parar
Mostrar 'App fechado normalmente' $true
if ((Estado).Estado -ne $original.Estado) { "FALHA: estado da barra não voltou ao original ($($original.Estado))" }

Start-Process $Exe; Start-Sleep 5
$main = Get-CimInstance Win32_Process -Filter "Name='AuroraDesk.exe'" | Where-Object { $_.CommandLine -notmatch '--vigia' } | Select-Object -First 1
Stop-Process -Id $main.ProcessId -Force
Start-Sleep 3
Mostrar 'App encerrado à força (queda)' $true

Start-Process $Exe; Start-Sleep 5
Stop-Process -Name explorer -Force; Start-Sleep 8
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe; Start-Sleep 6 }
Mostrar 'Explorer reiniciado com o app aberto' $false

Parar
Mostrar 'App fechado depois do reinício' $true
if ((Estado).Estado -ne $original.Estado) { "FALHA: estado da barra não voltou ao original ($($original.Estado))" } else { "Estado final igual ao original ($($original.Estado))" }

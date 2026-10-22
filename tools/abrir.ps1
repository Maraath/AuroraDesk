# Abre o Aurora Desk instalado FORA de qualquer pacote MSIX (via WMI).
# Use sempre este script para abrir o app a partir do Claude desktop (ou de outro app empacotado):
# aberto direto daí, o Windows redireciona o AppData para a pasta do pacote, e a configuração
# gravada não é a que o app vê ao abrir pelo logon.
param([string]$Argumentos = '')
$exe = Join-Path $env:LOCALAPPDATA 'Programs\Aurora Desk\AuroraDesk.exe'
if (-not (Test-Path $exe)) { "Aurora Desk não está instalado em $exe"; exit 1 }
$linha = "`"$exe`" $Argumentos".Trim()
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $linha }
if ($r.ReturnValue -ne 0) { "Falha ao abrir (código $($r.ReturnValue))"; exit 1 }
"Aberto fora do pacote: pid $($r.ProcessId)"

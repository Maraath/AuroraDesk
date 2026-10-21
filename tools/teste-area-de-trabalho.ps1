<#
.SYNOPSIS
  Teste da camada "na área de trabalho": Win+D, volta, reinício do Explorer e Win+D de novo,
  conferindo a ordem Z em cada passo (tools\ordem-z.ps1). Atenção: reinicia o explorer.exe.
#>
param([string]$Saida = $env:TEMP)
$z = Join-Path $PSScriptRoot 'ordem-z.ps1'
$winD = Join-Path $PSScriptRoot 'tecla-win-d.ps1'
$cap = Join-Path $PSScriptRoot 'capturar.ps1'
function Veredito($rotulo) { "{0,-32} {1}" -f $rotulo, (& powershell -NoProfile -ExecutionPolicy Bypass -File $z | Select-Object -Last 1) }

Veredito 'Início'
& powershell -NoProfile -ExecutionPolicy Bypass -File $winD; Start-Sleep 2
Veredito 'Win+D (área de trabalho)'
& powershell -NoProfile -ExecutionPolicy Bypass -File $cap -Saida (Join-Path $Saida 'teste-wind-1.png') -X 1560 -Y 0 -Largura 360 -Altura 260 | Out-Null
& powershell -NoProfile -ExecutionPolicy Bypass -File $winD; Start-Sleep 2
Veredito 'Win+D de novo (janelas voltam)'

Stop-Process -Name explorer -Force
Start-Sleep 7
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe; Start-Sleep 6 }
"Aurora Desk vivo após reiniciar o Explorer: $([bool](Get-Process AuroraDesk -ErrorAction SilentlyContinue))"
Veredito 'Explorer reiniciado'
& powershell -NoProfile -ExecutionPolicy Bypass -File $winD; Start-Sleep 2
Veredito 'Win+D após reinício'
& powershell -NoProfile -ExecutionPolicy Bypass -File $cap -Saida (Join-Path $Saida 'teste-wind-2.png') -X 1560 -Y 0 -Largura 360 -Altura 260 | Out-Null
& powershell -NoProfile -ExecutionPolicy Bypass -File $winD; Start-Sleep 2
Veredito 'Win+D de novo após reinício'

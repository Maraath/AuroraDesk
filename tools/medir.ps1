<#
.SYNOPSIS
  Mede o consumo do Aurora Desk: inicia o app, espera N segundos amostrando a CPU a cada segundo
  e imprime Private Bytes, Working Set (total e privado), handles, threads, CPU ociosa e tempo de início.
  O resultado é anexado em docs\medicoes.md.

.EXAMPLE
  .\tools\medir.ps1 -Publicar -Rotulo "M1 - bandeja"
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\publish\AuroraDesk.exe'),
    [int]$Segundos = 60,
    [switch]$Publicar,
    [string]$Rotulo = '',
    [switch]$ManterAberto,
    [switch]$SemRegistro
)

$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..')

if ($Publicar) { & (Join-Path $PSScriptRoot 'publicar.ps1') }
if (-not (Test-Path $Exe)) { throw "Executável não encontrado: $Exe (use -Publicar)" }
$Exe = (Resolve-Path $Exe).Path

# Encerra uma instância anterior (o app é de instância única).
if (Get-Process AuroraDesk -ErrorAction SilentlyContinue) {
    & $Exe --sair
    Start-Sleep -Seconds 2
    Get-Process AuroraDesk -ErrorAction SilentlyContinue | Stop-Process -Force
}

$log = Join-Path $env:LOCALAPPDATA 'AuroraDesk\logs\aurora.log'
$linhasAntes = if (Test-Path $log) { @(Get-Content $log -Encoding UTF8).Count } else { 0 }

$nucleos = [Environment]::ProcessorCount
$inicio = Get-Date
$p = Start-Process -FilePath $Exe -PassThru
Write-Host "Aurora Desk iniciado (PID $($p.Id)). Medindo por $Segundos s..."

$amostras = New-Object System.Collections.Generic.List[object]
for ($s = 1; $s -le $Segundos; $s++) {
    Start-Sleep -Seconds 1
    if ($p.HasExited) { throw "O app fechou durante a medição (código $($p.ExitCode)). Veja $log" }
    $p.Refresh()
    $amostras.Add([pscustomobject]@{ T = $s; Cpu = $p.TotalProcessorTime.TotalMilliseconds; Hora = Get-Date })
    if ($s % 10 -eq 0) { Write-Host "  $s s" }
}

$p.Refresh()
# CPU média ociosa: dos 10 s em diante (exclui a inicialização) e média total.
$ini = $amostras | Where-Object T -eq ([Math]::Min(10, $Segundos - 1))
$fim = $amostras[$amostras.Count - 1]
$cpuOcioso = ($fim.Cpu - $ini.Cpu) / (($fim.Hora - $ini.Hora).TotalMilliseconds * $nucleos) * 100
$cpuTotal = $fim.Cpu / (($fim.Hora - $inicio).TotalMilliseconds * $nucleos) * 100
$picoCpu = 0
for ($i = 1; $i -lt $amostras.Count; $i++) {
    $d = ($amostras[$i].Cpu - $amostras[$i - 1].Cpu) / (($amostras[$i].Hora - $amostras[$i - 1].Hora).TotalMilliseconds * $nucleos) * 100
    if ($i -ge 10 -and $d -gt $picoCpu) { $picoCpu = $d }
}

$privado = $p.PrivateMemorySize64 / 1MB
$ws = $p.WorkingSet64 / 1MB
$picoWs = $p.PeakWorkingSet64 / 1MB
$handles = $p.HandleCount
$threads = $p.Threads.Count

# "Memória (conjunto de trabalho privado)", como no Gerenciador de Tarefas.
$wsPrivado = $null
# Via classe WMI de desempenho (nome não traduzido, ao contrário do Get-Counter no Windows em português).
try {
    $perf = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess = $($p.Id)" -ErrorAction Stop
    if ($perf) { $wsPrivado = [double]$perf.WorkingSetPrivate / 1MB }
} catch { }

# Tempo de inicialização registrado pelo próprio app.
$inicioMs = $null
if (Test-Path $log) {
    $novas = @(Get-Content $log -Encoding UTF8) | Select-Object -Skip $linhasAntes
    $linha = $novas | Where-Object { $_ -match 'Inicialização concluída em (\d+) ms' } | Select-Object -Last 1
    if ($linha -and $linha -match 'em (\d+) ms') { $inicioMs = [int]$Matches[1] }
}

$fmt = { param($v) if ($null -eq $v) { 'n/d' } else { '{0:N1}' -f $v } }
Write-Host ''
Write-Host '=== Aurora Desk: medição ==='
Write-Host ("Rótulo:                     {0}" -f $Rotulo)
Write-Host ("Private Bytes:              {0} MB" -f (& $fmt $privado))
Write-Host ("Working Set:                {0} MB (pico {1} MB)" -f (& $fmt $ws), (& $fmt $picoWs))
Write-Host ("Working Set privado:        {0} MB" -f (& $fmt $wsPrivado))
Write-Host ("CPU média ociosa (10-{0} s): {1:N2}% (pico de 1 s: {2:N2}%)" -f $Segundos, $cpuOcioso, $picoCpu)
Write-Host ("CPU média total:            {0:N2}%" -f $cpuTotal)
Write-Host ("Handles / threads:          {0} / {1}" -f $handles, $threads)
Write-Host ("Inicialização:              {0} ms" -f $(if ($inicioMs) { $inicioMs } else { 'n/d' }))

if (-not $SemRegistro) {
    $arq = Join-Path $raiz 'docs\medicoes.md'
    if (-not (Test-Path $arq)) {
        $cab = @(
            '# Medições de consumo',
            '',
            '## Histórico (`tools\medir.ps1`)',
            '',
            '| Data | Rótulo | Private Bytes (MB) | Working Set (MB) | WS privado (MB) | CPU ociosa (%) | Pico CPU 1 s (%) | Handles | Threads | Início (ms) |',
            '|---|---|---|---|---|---|---|---|---|---|'
        )
        [IO.File]::WriteAllLines($arq, $cab, (New-Object Text.UTF8Encoding $false))
    }
    $linhaMd = '| {0} | {1} | {2} | {3} | {4} | {5:N2} | {6:N2} | {7} | {8} | {9} |' -f (Get-Date -Format 'yyyy-MM-dd HH:mm'), $Rotulo, (& $fmt $privado), (& $fmt $ws), (& $fmt $wsPrivado), $cpuOcioso, $picoCpu, $handles, $threads, $(if ($inicioMs) { $inicioMs } else { 'n/d' })
    [IO.File]::AppendAllText($arq, $linhaMd + [Environment]::NewLine, (New-Object Text.UTF8Encoding $false))
    Write-Host "Registrado em $arq"
}

if (-not $ManterAberto) {
    & $Exe --sair
    Start-Sleep -Seconds 2
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
}

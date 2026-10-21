<#
.SYNOPSIS
  Compara variações de configuração do runtime (.NET) no consumo do Aurora Desk.
  Cada variação roda o app publicado com variáveis DOTNET_* por N segundos e lê Private Bytes.
  Use para decidir o que vai para o runtimeconfig (resultados em docs\medicoes.md, seção Experimentos).
#>
param([int]$Segundos = 35, [string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\publish\AuroraDesk.exe'))
$Exe = (Resolve-Path $Exe).Path

$variacoes = [ordered]@{
    'padrão (runtimeconfig atual)'          = @{}
    'GCgen0size = 1 MB'                     = @{ DOTNET_GCgen0size = '100000' }
    'GCConserveMemory = 7'                  = @{ DOTNET_GCConserveMemory = '7' }
    'TieredCompilation desligada'           = @{ DOTNET_TieredCompilation = '0' }
    'TC_QuickJitForLoops desligado'         = @{ DOTNET_TC_QuickJitForLoops = '0' }
    'GCHeapHardLimit = 32 MB'               = @{ DOTNET_GCHeapHardLimit = '2000000' }
}

$resultado = foreach ($nome in $variacoes.Keys) {
    Get-Process AuroraDesk -ErrorAction SilentlyContinue | ForEach-Object { & $Exe --sair; Start-Sleep 2; $_ | Stop-Process -Force -ErrorAction SilentlyContinue }
    $vars = $variacoes[$nome]
    foreach ($k in $vars.Keys) { Set-Item "env:$k" $vars[$k] }
    $p = Start-Process $Exe -PassThru
    foreach ($k in $vars.Keys) { Remove-Item "env:$k" }
    Start-Sleep $Segundos
    $p.Refresh()
    [pscustomobject]@{
        'Variação'           = $nome
        'Private Bytes (MB)' = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
        'Pico WS (MB)'       = [math]::Round($p.PeakWorkingSet64 / 1MB, 1)
        'CPU total (s)'      = [math]::Round($p.TotalProcessorTime.TotalSeconds, 2)
    }
    & $Exe --sair; Start-Sleep 2
}
$resultado | Format-Table -AutoSize | Out-String -Width 200

# Gera o executável do Aurora Desk em artifacts\publish (win-x64, ReadyToRun, dependente do runtime .NET 10).
param([string]$Saida = (Join-Path $PSScriptRoot '..\artifacts\publish'))

$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..')
$projeto = Join-Path $raiz 'src\AuroraDesk\AuroraDesk.csproj'

# Fecha uma instância aberta para o .exe não ficar travado.
$aberto = Get-Process AuroraDesk -ErrorAction SilentlyContinue
if ($aberto) {
    $exeAtual = $aberto[0].Path
    if ($exeAtual) { & $exeAtual --sair; Start-Sleep -Milliseconds 1500 }
    Get-Process AuroraDesk -ErrorAction SilentlyContinue | Stop-Process -Force
}

dotnet publish $projeto -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $Saida -nologo
if ($LASTEXITCODE -ne 0) { throw "Falha na publicação (código $LASTEXITCODE)" }

$exe = Join-Path (Resolve-Path $Saida) 'AuroraDesk.exe'
$tamanho = (Get-ChildItem $Saida -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Publicado: {0} ({1:N1} MB no total)" -f $exe, $tamanho)

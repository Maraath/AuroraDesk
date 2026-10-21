<#
.SYNOPSIS
  Gera o instalador do Aurora Desk: publica o app autocontido (com o .NET 10 Desktop Runtime
  embutido, win-x64, ReadyToRun) e compila installer\AuroraDesk.iss com o Inno Setup 6.
  Saída: artifacts\instalador\AuroraDesk-Setup-<versão>.exe
#>
param([string]$Iscc = '')

$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..')
$saida = Join-Path $raiz 'artifacts\publish-autocontido'

if (-not $Iscc) {
    $Iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc) { throw 'Inno Setup 6 não encontrado. Instale em https://jrsoftware.org/isdl.php ou passe -Iscc <caminho do ISCC.exe>.' }

# Versão vem do Directory.Build.props.
$versao = ([xml](Get-Content (Join-Path $raiz 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $versao) { $versao = '0.1.0' }

Write-Host "Publicando Aurora Desk $versao autocontido..."
if (Test-Path $saida) { Remove-Item $saida -Recurse -Force }
dotnet publish (Join-Path $raiz 'src\AuroraDesk\AuroraDesk.csproj') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $saida -nologo
if ($LASTEXITCODE -ne 0) { throw "Falha na publicação (código $LASTEXITCODE)" }

Write-Host 'Compilando o instalador...'
& $Iscc "/DAppVersion=$versao" "/DSourceDir=$saida" (Join-Path $raiz 'installer\AuroraDesk.iss')
if ($LASTEXITCODE -ne 0) { throw "Falha no Inno Setup (código $LASTEXITCODE)" }

$exe = Get-Item (Join-Path $raiz "artifacts\instalador\AuroraDesk-Setup-$versao.exe")
Write-Host ("Instalador: {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB))

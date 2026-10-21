# Gera src\AuroraDesk\Assets\aurora.ico (16, 20, 24, 32, 48, 64, 256 px, em PNG dentro do ICO).
# Desenho: círculo com gradiente "aurora" (verde-azulado -> violeta) e um arco claro.
param([string]$Saida = (Join-Path $PSScriptRoot '..\src\AuroraDesk\Assets\aurora.ico'))

Add-Type -AssemblyName System.Drawing
$tamanhos = 16, 20, 24, 32, 48, 64, 256
$pngs = @()

foreach ($t in $tamanhos) {
    $bmp = New-Object System.Drawing.Bitmap $t, $t, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $m = [Math]::Max(1, $t * 0.04)
    $ret = New-Object System.Drawing.RectangleF $m, $m, ($t - 2 * $m), ($t - 2 * $m)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $ret, ([System.Drawing.Color]::FromArgb(255, 32, 201, 151)), ([System.Drawing.Color]::FromArgb(255, 124, 92, 255)), 45.0
    $g.FillEllipse($grad, $ret)
    $penW = [Math]::Max(1.5, $t * 0.09)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(235, 255, 255, 255)), $penW
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $i = $t * 0.27
    $arco = New-Object System.Drawing.RectangleF $i, $i, ($t - 2 * $i), ($t - 2 * $i)
    $g.DrawArc($pen, $arco, 200, 230)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

New-Item -ItemType Directory -Force (Split-Path $Saida) | Out-Null
$fs = [System.IO.File]::Create($Saida)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$tamanhos.Count)
$offset = 6 + 16 * $tamanhos.Count
for ($k = 0; $k -lt $tamanhos.Count; $k++) {
    $t = $tamanhos[$k]
    $b = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$k].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$k].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Host "Ícone gerado em $Saida"

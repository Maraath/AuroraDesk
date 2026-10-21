# Captura a tela principal (ou uma região) em PNG. Uso: .\tools\capturar.ps1 -Saida foto.png [-X 0 -Y 0 -Largura 800 -Altura 600]
param([Parameter(Mandatory)][string]$Saida, [int]$X = -1, [int]$Y = 0, [int]$Largura = 0, [int]$Altura = 0)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace Nativo -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[Nativo.Dpi]::SetProcessDPIAware() | Out-Null

$tela = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($X -lt 0) { $X = $tela.X; $Y = $tela.Y; $Largura = $tela.Width; $Altura = $tela.Height }
$bmp = New-Object System.Drawing.Bitmap $Largura, $Altura
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size $Largura, $Altura))
$g.Dispose()
$bmp.Save($Saida, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Tela $($tela.Width)x$($tela.Height); captura salva em $Saida"

# Gera, NO SEU PC, a skin "Azeroth" do dock e das ilhas no estilo da barra de ações do World of Warcraft.
#
# As imagens da skin são texturas da interface do World of Warcraft: a arte é da Blizzard Entertainment e
# NÃO vem com o Aurora Desk. Este script baixa as texturas do espelho público
# github.com/Gethe/wow-ui-textures (fixado numa versão exata, com conferência de SHA-256), monta as peças
# e instala a skin só para você. Uso pessoal: não redistribua as imagens geradas.
#
# Uso:
#   .\tools\gerar-skin-azeroth.ps1                     # gera e instala a skin (escolha-a em Configurações > Dock)
#   .\tools\gerar-skin-azeroth.ps1 -AplicarAparencia   # também deixa o visual igual ao do autor:
#                                                       # skin Azeroth, ícones em 24, tema Mocha e ilha
#                                                       # esquerda com CPU, RAM e clima (faz backup do config.json)
# Requer o Aurora Desk 0.1.6 ou mais novo (células, quadro inteiro e quadro solto).
param(
    [string]$Destino = (Join-Path $env:APPDATA 'AuroraDesk\skins-dock\Azeroth'),
    [string]$Cache = (Join-Path $env:TEMP 'AuroraDesk-texturas-wow'),
    [switch]$AplicarAparencia
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Versão exata do espelho e o SHA-256 esperado de cada textura: a skin sai igual em qualquer PC.
$Versao = 'd23deaf8f44a7d280dc974a5c9d5321c013db59b'
$Texturas = [ordered]@{
    'MainMenuBar/UI-MainMenuBar-EndCap-Dwarf.PNG' = 'FE6671A97DCEF044CCB3FF1F2ED9C5E5B8CA751026F68498DB82E353782DFBEF'
    'MainMenuBar/UI-MainMenuBar-EndCap-Human.PNG' = 'CA6E238077D54B3A9D0BCA1B90D62821DF421326D42696C7392DAEFDF60E7745'
    'MainMenuBar/UI-MAINMENUBAR-DWARF.PNG'        = '9D011395546B2DD6C16ED8227DA31658C44A284B70DAF2E5D442C33966AFEFC9'
    'Buttons/UI-Quickslot2.PNG'                   = '29D9DDC99D5B74870C2BFF797CF3AF8E4C280E0A9F7DF62A9B1EDD149B276CD0'
    'MINIMAP/UI-MINIMAP-BORDER.PNG'               = 'C941A1D64E2327EC69A2F2F86115BF2EBAC7C6B70D75590AD126D64BBDA1169C'
    'MINIMAP/MiniMap-TrackingBorder.PNG'          = '10BF8A756E507FBCE1DE57FD9670168BF559DFE6AFCA0B20CB92F5B41FA450B2'
    'TARGETINGFRAME/UI-TargetingFrame.PNG'        = '6637EEFAB1CB2EBC8AD0E713E719BC00D67B53E033DEC2840418D362156976B3'
    'TARGETINGFRAME/UI-StatusBar.PNG'             = '890DBFBDBEA16A523E1548B0DEBD94105CF8B4D00003A5B82AA592AD97BCF561'
}

Write-Host 'Aurora Desk: skin Azeroth (texturas do World of Warcraft, arte da Blizzard; uso pessoal).'

# ---------- 1. baixar e conferir as texturas ----------
New-Item -ItemType Directory -Force $Cache | Out-Null
foreach ($caminho in $Texturas.Keys) {
    $arquivo = Join-Path $Cache (Split-Path $caminho -Leaf)
    $esperado = $Texturas[$caminho]
    if (-not (Test-Path $arquivo) -or (Get-FileHash $arquivo -Algorithm SHA256).Hash -ne $esperado) {
        $url = "https://raw.githubusercontent.com/Gethe/wow-ui-textures/$Versao/$caminho"
        Write-Host "  baixando $(Split-Path $caminho -Leaf)"
        Invoke-WebRequest -Uri $url -OutFile $arquivo -UseBasicParsing
    }
    $hash = (Get-FileHash $arquivo -Algorithm SHA256).Hash
    if ($hash -ne $esperado) { Remove-Item $arquivo -Force; throw "A textura $(Split-Path $caminho -Leaf) veio diferente da esperada (SHA-256 $hash). Nada foi instalado." }
}
Write-Host '  8 texturas conferidas.'

# ---------- 2. montar as peças ----------
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class SkinAzeroth
{
    public static Bitmap Load(string p) { using (var b = new Bitmap(p)) return new Bitmap(b); }

    public static Bitmap Crop(Bitmap s, int x, int y, int w, int h)
    {
        var o = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(o)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(s, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel); }
        return o;
    }

    // Retângulo com os pixels visíveis (corta as bordas vazias das texturas).
    public static Rectangle Bounds(Bitmap b, int minAlpha)
    {
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++)
            if (b.GetPixel(x, y).A > minAlpha) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        return new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    public static Bitmap FlipX(Bitmap b) { var o = new Bitmap(b); o.RotateFlip(RotateFlipType.RotateNoneFlipX); return o; }

    // Disco escuro atrás de um aro (o relógio e os ícones vão por cima), com a borda suave.
    public static Bitmap DarkCenter(Bitmap ring, double cx, double cy, double r, Color c)
    {
        var o = new Bitmap(ring.Width, ring.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(o))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse((float)(cx - r), (float)(cy - r), (float)(2 * r), (float)(2 * r));
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(255, c.R + 10, c.G + 12, c.B + 10), SurroundColors = new[] { c } })
                    g.FillPath(pb, path);
            }
            g.DrawImage(ring, 0, 0);
        }
        return o;
    }

    // Barra do jogo (cinza com brilho) tingida como o jogo faz (cor multiplicada).
    public static Bitmap Tint(Bitmap b, double r, double gg, double bl)
    {
        var o = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++)
        {
            var c = b.GetPixel(x, y);
            o.SetPixel(x, y, Color.FromArgb(c.A, (int)(c.R * r), (int)(c.G * gg), (int)(c.B * bl)));
        }
        return o;
    }

    // Moldura das ilhas laterais na mesma família das células do dock: a borda de metal da célula
    // (cantos, bordas de cima e de baixo repetidas, lateral esquerda e a mesma espelhada à direita)
    // em volta da faixa de pedra limpa da barra clássica.
    public static Bitmap FramedPanel(Bitmap cell, Bitmap atlas, Rectangle stone, int w, int b)
    {
        int h = cell.Height;
        var o = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(o))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var st = Crop(atlas, stone.X, stone.Y, stone.Width, stone.Height))
            using (var sc = new Bitmap(st, new Size((int)Math.Round(st.Width * (double)(h - 2 * b) / st.Height), h - 2 * b)))
            using (var tb = new TextureBrush(sc, WrapMode.TileFlipX))
            {
                tb.TranslateTransform(b, b);
                g.FillRectangle(tb, b, b, w - 2 * b, h - 2 * b);
            }
            using (var sh = new LinearGradientBrush(new Rectangle(b, b, w - 2 * b, 6), Color.FromArgb(140, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical))
                g.FillRectangle(sh, b, b, w - 2 * b, 6);
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            int mid = cell.Width - 2 * b - 2;
            for (int x = b; x < w - b; x += mid)
            {
                int pw = Math.Min(mid, w - b - x);
                g.DrawImage(cell, new Rectangle(x, 0, pw, b), new Rectangle(b + 1, 0, pw, b), GraphicsUnit.Pixel);
                g.DrawImage(cell, new Rectangle(x, h - b, pw, b), new Rectangle(b + 1, h - b, pw, b), GraphicsUnit.Pixel);
            }
            g.DrawImage(cell, new Rectangle(0, 0, b, h), new Rectangle(0, 0, b, h), GraphicsUnit.Pixel);
            using (var left = Crop(cell, 0, 0, b, h)) { left.RotateFlip(RotateFlipType.RotateNoneFlipX); g.DrawImage(left, w - b, 0); }
        }
        return o;
    }

    public static void Save(Bitmap b, string p) { b.Save(p, ImageFormat.Png); }
}
'@
New-Item -ItemType Directory -Force $Destino | Out-Null
Get-ChildItem $Destino -File | Remove-Item -Force
function T([string]$nome) { [SkinAzeroth]::Load((Join-Path $Cache $nome)) }

# Grifo (Aliança) e leão alado (Horda): os endcaps sem as bordas vazias. O app espelha para a direita.
$cap = T 'UI-MainMenuBar-EndCap-Dwarf.PNG'; $cb = [SkinAzeroth]::Bounds($cap, 8)
[SkinAzeroth]::Save([SkinAzeroth]::Crop($cap, $cb.X, $cb.Y, $cb.Width, $cb.Height), "$Destino\ponta-esquerda.png")
$cap2 = T 'UI-MainMenuBar-EndCap-Human.PNG'; $cb2 = [SkinAzeroth]::Bounds($cap2, 8)
[SkinAzeroth]::Save([SkinAzeroth]::Crop($cap2, $cb2.X, $cb2.Y, $cb2.Width, $cb2.Height), "$Destino\ponta-horda.png")

# Célula da barra clássica (moldura + espiral; y 148..193) e, com a mesma borda, a moldura das ilhas.
$atlas = T 'UI-MAINMENUBAR-DWARF.PNG'
$cell = [SkinAzeroth]::Crop($atlas, 1, 148, 43, 46)
[SkinAzeroth]::Save([SkinAzeroth]::FramedPanel($cell, $atlas, (New-Object Drawing.Rectangle 40, 88, 210, 36), 128, 5), "$Destino\painel.png")
[SkinAzeroth]::Save($cell, "$Destino\celula.png")
# Divisor: o poste de metal com rebite, no lugar dos separadores do dock.
[SkinAzeroth]::Save([SkinAzeroth]::Crop($atlas, 20, 84, 18, 44), "$Destino\divisor.png")

# Borda dos botões de ação (sem as margens vazias).
$q = T 'UI-Quickslot2.PNG'; $qb = [SkinAzeroth]::Bounds($q, 8)
[SkinAzeroth]::Save([SkinAzeroth]::Crop($q, $qb.X, $qb.Y, $qb.Width, $qb.Height), "$Destino\encaixe.png")

# Medalhão do relógio (aro do minimapa) e botão redondo da bandeja (aro dos botões do minimapa).
$mm = [SkinAzeroth]::Crop((T 'UI-MINIMAP-BORDER.PNG'), 95, 31, 149, 148)
[SkinAzeroth]::Save([SkinAzeroth]::DarkCenter($mm, 73.7, 73.3, 67, [Drawing.Color]::FromArgb(255, 10, 12, 11)), "$Destino\medalhao.png")
$tb = [SkinAzeroth]::Crop((T 'MiniMap-TrackingBorder.PNG'), 1, 1, 38, 38)
[SkinAzeroth]::Save([SkinAzeroth]::DarkCenter($tb, 19, 19, 15, [Drawing.Color]::FromArgb(255, 12, 12, 14)), "$Destino\botao-redondo.png")

# Quadro do jogador: o quadro de alvo espelhado (retrato à esquerda), com o anel do nível escuro.
$tf = T 'UI-TargetingFrame.PNG'; $fb = [SkinAzeroth]::Bounds($tf, 8)
$tfc = [SkinAzeroth]::Crop($tf, $fb.X, $fb.Y, $fb.Width, $fb.Height)
$tfc = [SkinAzeroth]::DarkCenter($tfc, 202 - $fb.X, 65 - $fb.Y, 9.5, [Drawing.Color]::FromArgb(255, 14, 12, 10))
[SkinAzeroth]::Save([SkinAzeroth]::FlipX($tfc), "$Destino\quadro.png")
# Áreas (coordenadas do original) convertidas para a imagem recortada e espelhada.
function Area([double]$x, [double]$y, [double]$w, [double]$h) {
    $xr = ($fb.X + $fb.Width) - ($x + $w)
    [string]::Format([Globalization.CultureInfo]::InvariantCulture, '[{0}, {1}, {2}, {3}]', [math]::Round($xr, 1), [math]::Round($y - $fb.Y, 1), $w, $h)
}
$aRet = Area 154 17 54 54; $aNome = Area 31 23 116 16; $aCpu = Area 31 41.5 116 9.5; $aRam = Area 31 53.5 116 9.5

# Barras: a textura original das barras, tingida de verde (vida = CPU) e azul (mana = RAM).
$sb = T 'UI-StatusBar.PNG'
[SkinAzeroth]::Save([SkinAzeroth]::Tint($sb, 0, 1, 0), "$Destino\barra-cpu.png")
[SkinAzeroth]::Save([SkinAzeroth]::Tint($sb, 0, 0.35, 1), "$Destino\barra-ram.png")

$json = @"
// Skin "Azeroth" gerada por tools\gerar-skin-azeroth.ps1 com texturas da interface do World of Warcraft
// (arte da Blizzard Entertainment). Uso pessoal: não redistribua estas imagens.
{
  "nome": "Azeroth",
  "painel": { "imagem": "painel.png", "margens": [6, 6, 6, 6] },
  "encaixe": { "imagem": "encaixe.png", "celula": "celula.png", "divisor": "divisor.png", "recuo": 0.02, "margem": 0.07 },
  // Para a Horda, troque por "ponta-horda.png".
  "pontas": { "esquerda": "ponta-esquerda.png", "altura": 1.4, "sobreposicao": 0.26, "deslocamentoY": 0 },
  "quadro": { "imagem": "quadro.png", "areaRetrato": $aRet, "areaNome": $aNome, "areaCpu": $aCpu, "areaRam": $aRam,
              "nome": "Aurora", "barraCpu": "barra-cpu.png", "barraRam": "barra-ram.png" },
  "medalhao": "medalhao.png",
  "botaoRedondo": "botao-redondo.png",
  "fonte": "Georgia",
  "corTexto": "#FFD100",
  "corTextoSecundario": "#F2E8CC"
}
"@
[IO.File]::WriteAllText("$Destino\skin.json", $json, (New-Object Text.UTF8Encoding $false))
Write-Host "  skin instalada em $Destino"

if (-not $AplicarAparencia) {
    Write-Host 'Pronto. Escolha "Azeroth" em Configurações > Dock > Aparência > Skin de imagens.'
    Write-Host 'Para deixar o visual igual ao do autor, rode de novo com -AplicarAparencia.'
    return
}

# ---------- 3. aparência recomendada (opcional) ----------
$exe = Join-Path $env:LOCALAPPDATA 'Programs\Aurora Desk\AuroraDesk.exe'
if (Test-Path $exe) {
    $v = (Get-Item $exe).VersionInfo.ProductVersion -replace '[+-].*$', ''
    if ([version]$v -lt [version]'0.1.6') { Write-Warning "Seu Aurora Desk é a versão $v; a skin precisa da 0.1.6 ou mais nova. Atualize pelo instalador da página de Releases." }
}
$aberto = Get-CimInstance Win32_Process -Filter "Name='AuroraDesk.exe'" | Where-Object { $_.CommandLine -notmatch '--vigia' }
if ($aberto -and (Test-Path $exe)) {
    Write-Host '  fechando o Aurora Desk para aplicar a aparência...'
    & $exe --sair
    for ($i = 0; $i -lt 20 -and (Get-CimInstance Win32_Process -Filter "Name='AuroraDesk.exe'"); $i++) { Start-Sleep 1 }
}
$cfg = Join-Path $env:APPDATA 'AuroraDesk\config.json'
if (-not (Test-Path $cfg)) { Write-Warning 'config.json não encontrado: abra o Aurora Desk uma vez e rode o script de novo.'; return }
$backup = Join-Path $env:APPDATA ("AuroraDesk\backup\config-antes-da-skin-azeroth-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null
Copy-Item $cfg $backup
$c = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
function Set-Prop($obj, [string]$nome, $valor) {
    if ($obj.PSObject.Properties[$nome]) { $obj.$nome = $valor } else { $obj | Add-Member -NotePropertyName $nome -NotePropertyValue $valor }
}
Set-Prop $c 'theme' 'Mocha'
Set-Prop $c 'followWindowsTheme' $false
Set-Prop $c.dock 'skin' 'Azeroth'
Set-Prop $c.dock 'iconSize' 24
Set-Prop $c.dock 'islands' $true
Set-Prop $c.dock 'backdrop' 'nenhum'
Set-Prop $c.dock 'leftItems' @('cpu', 'ram', 'clima')
[IO.File]::WriteAllText($cfg, ($c | ConvertTo-Json -Depth 32), (New-Object Text.UTF8Encoding $false))
Write-Host "  aparência aplicada (backup do config.json em $backup)"
if (Test-Path $exe) { Start-Process $exe; Write-Host '  Aurora Desk aberto de novo.' }
Write-Host 'Pronto.'

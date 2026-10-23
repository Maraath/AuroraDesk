# Gera a skin "Pedra" (texturas procedurais próprias, sem imagens de terceiros) em src\AuroraDesk\skins-dock\Pedra.
# Pedra com ruído fractal, chumbo chanfrado, ouro envelhecido e rebites. Uso: .\tools\gerar-skin-pedra.ps1
param([string]$Saida = (Join-Path $PSScriptRoot '..\src\AuroraDesk\skins-dock\Pedra'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;

public static class Pedra
{
    static float Hash(int x, int y, int s) { unchecked { int h = x * 374761393 + y * 668265263 + s * 1442695041; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f; } }
    static float Smooth(float t) { return t * t * (3 - 2 * t); }
    static float Value(float x, float y, int s)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y); float fx = Smooth(x - xi), fy = Smooth(y - yi);
        float a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }
    public static float Fbm(float x, float y, int s, int oct)
    {
        float v = 0, amp = 0.5f, f = 1;
        for (int i = 0; i < oct; i++) { v += Value(x * f, y * f, s + i * 17) * amp; f *= 2.03f; amp *= 0.5f; }
        return v;
    }
    static int C(double v) { return v < 0 ? 0 : v > 255 ? 255 : (int)v; }
    static Color Lerp(Color a, Color b, double t) { t = Math.Max(0, Math.Min(1, t)); return Color.FromArgb(C(a.A + (b.A - a.A) * t), C(a.R + (b.R - a.R) * t), C(a.G + (b.G - a.G) * t), C(a.B + (b.B - a.B) * t)); }

    // Metal chanfrado: claro em cima/à esquerda, escuro embaixo/à direita, com ruído de desgaste.
    static Color Metal(Color baseC, double light, double noise)
    {
        double k = 0.55 + 0.75 * light + (noise - 0.5) * 0.35;
        return Color.FromArgb(255, C(baseC.R * k), C(baseC.G * k), C(baseC.B * k));
    }

    public static void Save(Bitmap b, string path) { b.Save(path, ImageFormat.Png); b.Dispose(); }

    static Color Stone(int x, int y, int seed)
    {
        float n = Fbm(x / 22f, y / 22f, seed, 5);
        float crack = Math.Abs(Fbm(x / 40f, y / 14f, seed + 99, 3) - 0.5f);
        double v = 34 + n * 34 - (crack < 0.025f ? 14 : 0);
        return Color.FromArgb(255, C(v * 1.02), C(v), C(v * 1.06));
    }

    // Painel: pedra no meio, moldura de chumbo de 'band' px com filete dourado e rebites.
    // A coluna do meio do painel é repetida pelo app: a textura precisa emendar sem costura.
    // Mistura o ruído com ele mesmo deslocado de um período (P) e usa o deslocado no canto direito.
    static Color Seamless(Func<int, Color> at, int x, int m, int w)
    {
        int p = w - 2 * m;
        if (x < m) return at(x);
        if (x >= w - m) return at(x - p);
        double t = (double)(x - m) / p;
        return Lerp(at(x), at(x - p), t);
    }

    public static Bitmap Panel(int w, int h, int band, int seed) { return Panel(w, h, band, seed, 26); }

    public static Bitmap Panel(int w, int h, int band, int seed, int margin)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        Color lead = Color.FromArgb(255, 78, 80, 86), gold = Color.FromArgb(255, 150, 112, 48);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int dx = Math.Min(x, w - 1 - x), dy = Math.Min(y, h - 1 - y), d = Math.Min(dx, dy);
                int yy = y;
                float n = (float)(Seamless(xx => { float v = Fbm(xx / 6f, yy / 6f, seed + 5, 3); return Color.FromArgb(255, C(v * 255), 0, 0); }, x, margin, w).R / 255.0);
                Color c;
                if (d <= 1) c = Color.FromArgb(255, 10, 10, 12);
                else if (d < band)
                {
                    double t = (double)(d - 2) / (band - 2);
                    double light = (y < h / 2 ? 0.75 : 0.25) * (1 - t) + 0.45 * t;
                    if (dx < dy) light = x < w / 2 ? 0.6 : 0.3;
                    c = Metal(lead, light, n);
                    if (d == band - 2) c = Lerp(c, gold, 0.85);
                    if (d == band - 1) c = Color.FromArgb(255, 14, 14, 16);
                }
                else
                {
                    c = Seamless(xx => Stone(xx, yy, seed), x, margin, w);
                    double shade = Math.Max(0, 1 - (d - band) / 10.0) * 0.45; // sombra interna da moldura
                    c = Lerp(c, Color.FromArgb(255, 0, 0, 0), shade);
                }
                b.SetPixel(x, y, c);
            }
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int r = band / 2 + 1;
            foreach (var p in new[] { new Point(band / 2 + 2, band / 2 + 2), new Point(w - band / 2 - 3, band / 2 + 2), new Point(band / 2 + 2, h - band / 2 - 3), new Point(w - band / 2 - 3, h - band / 2 - 3) })
                Rivet(g, p.X, p.Y, r);
        }
        return b;
    }

    public static void Rivet(Graphics g, float cx, float cy, float r)
    {
        using (var dark = new SolidBrush(Color.FromArgb(255, 40, 28, 10))) g.FillEllipse(dark, cx - r, cy - r, 2 * r, 2 * r);
        using (var body = new System.Drawing.Drawing2D.LinearGradientBrush(new PointF(cx - r, cy - r), new PointF(cx + r, cy + r), Color.FromArgb(255, 240, 210, 130), Color.FromArgb(255, 110, 78, 26)))
            g.FillEllipse(body, cx - r + 1, cy - r + 1, 2 * r - 2, 2 * r - 2);
        using (var hi = new SolidBrush(Color.FromArgb(200, 255, 245, 210))) g.FillEllipse(hi, cx - r * 0.45f, cy - r * 0.55f, r * 0.6f, r * 0.5f);
    }

    // Encaixe: moldura de ferro chanfrado com o meio transparente e sombra interna sobre o ícone.
    public static Bitmap Slot(int s, int frame, int seed)
    {
        var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        Color iron = Color.FromArgb(255, 70, 72, 78), gold = Color.FromArgb(255, 156, 118, 50);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                int dx = Math.Min(x, s - 1 - x), dy = Math.Min(y, s - 1 - y), d = Math.Min(dx, dy);
                float n = Fbm(x / 5f, y / 5f, seed, 3);
                Color c;
                if (d <= 1) c = Color.FromArgb(255, 6, 6, 8);
                else if (d < frame)
                {
                    bool topLeft = (dx == x && dx <= dy) || (dy == y && dy <= dx);
                    double light = topLeft ? 0.85 : 0.22;
                    c = Metal(iron, light, n);
                    if (d == frame - 3) c = Lerp(c, gold, 0.9);
                    if (d >= frame - 2) c = Color.FromArgb(255, 8, 8, 10);
                }
                else
                {
                    double shade = Math.Max(0, 1 - (d - frame) / 9.0);
                    c = Color.FromArgb(C(shade * 170), 0, 0, 0); // sombra interna (o ícone aparece por baixo)
                }
                b.SetPixel(x, y, c);
            }
        return b;
    }

    public static Bitmap SlotBackground(int s, int seed)
    {
        var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                double v = 10 + Fbm(x / 9f, y / 9f, seed, 4) * 16;
                b.SetPixel(x, y, Color.FromArgb(255, C(v), C(v), C(v * 1.1)));
            }
        return b;
    }

    // Anel redondo (medalhão, botão, moldura do retrato): ouro ou chumbo com relevo radial.
    public static Bitmap Ring(int s, double inner, bool goldRing, bool darkCenter, int rivets, int seed)
    {
        var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        double c0 = (s - 1) / 2.0, R = s / 2.0 - 1, ri = R * inner;
        Color metal = goldRing ? Color.FromArgb(255, 150, 110, 44) : Color.FromArgb(255, 80, 82, 88);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                double dx = x - c0, dy = y - c0, d = Math.Sqrt(dx * dx + dy * dy);
                Color c = Color.Transparent;
                if (d <= R)
                {
                    if (d >= ri)
                    {
                        double t = (d - ri) / (R - ri);                  // 0 por dentro, 1 por fora
                        double bevel = Math.Sin(t * Math.PI);            // abaulado
                        double dir = (-dx - dy) / (d + 1e-6) * 0.5 + 0.5; // luz de cima/esquerda
                        double light = 0.25 + bevel * 0.45 + dir * 0.35;
                        float n = Fbm(x / 4f, y / 4f, seed, 3);
                        c = Metal(metal, light, n);
                        if (Math.Abs(d - ri) < 1.2 || Math.Abs(d - R) < 1.4) c = Color.FromArgb(255, 18, 12, 6);
                        double ang = Math.Atan2(dy, dx);
                        if (goldRing && Math.Abs(Math.Sin(ang * 24)) < 0.06 && t > 0.2 && t < 0.8) c = Lerp(c, Color.FromArgb(255, 60, 40, 12), 0.7); // entalhes
                    }
                    else if (darkCenter)
                    {
                        double v = 8 + (1 - d / ri) * 14 + Fbm(x / 8f, y / 8f, seed + 3, 3) * 8;
                        c = Color.FromArgb(255, C(v * 0.8), C(v * 1.05), C(v * 0.85));
                    }
                }
                double edge = R - d;
                if (edge < 1 && edge > -1 && c.A > 0) c = Color.FromArgb(C(255 * (edge + 1) / 2), c.R, c.G, c.B); // borda suave
                b.SetPixel(x, y, c);
            }
        if (rivets > 0)
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                double rm = (R + ri) / 2;
                for (int i = 0; i < rivets; i++)
                {
                    double a = i * 2 * Math.PI / rivets - Math.PI / 2;
                    Rivet(g, (float)(c0 + Math.Cos(a) * rm), (float)(c0 + Math.Sin(a) * rm), (float)((R - ri) * 0.22));
                }
            }
        return b;
    }

    // Barras: fundo escuro rebaixado e preenchimentos com brilho de vidro (vida e mana).
    public static Bitmap Bar(int w, int h, Color top, Color bottom, bool background, int seed)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double t = (double)y / (h - 1);
                Color c = Lerp(top, bottom, t);
                float n = Fbm(x / 7f, y / 3f, seed, 3);
                if (background) c = Lerp(c, Color.FromArgb(255, 0, 0, 0), y < 3 ? 0.6 : 0);
                else
                {
                    c = Lerp(c, Color.FromArgb(255, 255, 255, 255), t < 0.38 ? 0.28 * (1 - t / 0.38) : 0); // brilho
                    c = Lerp(c, Color.FromArgb(255, 0, 0, 0), (n - 0.5) * 0.25 + (t > 0.85 ? 0.3 : 0));
                }
                if (y == 0 || y == h - 1 || x == 0 || x == w - 1) c = Color.FromArgb(255, 4, 4, 6);
                b.SetPixel(x, y, c);
            }
        return b;
    }
}
'@
New-Item -ItemType Directory -Force $Saida | Out-Null
[Pedra]::Save([Pedra]::Panel(256, 128, 14, 7), (Join-Path $Saida 'painel.png'))
[Pedra]::Save([Pedra]::Slot(128, 14, 11), (Join-Path $Saida 'encaixe.png'))
[Pedra]::Save([Pedra]::SlotBackground(128, 13), (Join-Path $Saida 'fundo-encaixe.png'))
[Pedra]::Save([Pedra]::Ring(256, 0.76, $true, $true, 8, 17), (Join-Path $Saida 'medalhao.png'))
[Pedra]::Save([Pedra]::Ring(96, 0.66, $false, $true, 0, 19), (Join-Path $Saida 'botao-redondo.png'))
[Pedra]::Save([Pedra]::Ring(256, 0.80, $false, $false, 6, 23), (Join-Path $Saida 'moldura-retrato.png'))
[Pedra]::Save([Pedra]::Bar(256, 32, [Drawing.Color]::FromArgb(255, 30, 30, 34), [Drawing.Color]::FromArgb(255, 12, 12, 14), $true, 29), (Join-Path $Saida 'barra-fundo.png'))
[Pedra]::Save([Pedra]::Bar(256, 32, [Drawing.Color]::FromArgb(255, 60, 200, 70), [Drawing.Color]::FromArgb(255, 10, 92, 24), $false, 31), (Join-Path $Saida 'barra-cpu.png'))
[Pedra]::Save([Pedra]::Bar(256, 32, [Drawing.Color]::FromArgb(255, 70, 130, 240), [Drawing.Color]::FromArgb(255, 14, 40, 140), $false, 37), (Join-Path $Saida 'barra-ram.png'))
$json = @'
// Skin "Pedra": texturas procedurais do próprio Aurora (tools\gerar-skin-pedra.ps1), livres para usar e modificar.
// Para os grifos, coloque um PNG com transparência nesta pasta e preencha "pontas".
{
  "nome": "Pedra",
  "painel": { "imagem": "painel.png", "margens": [26, 26, 26, 26] },
  "encaixe": { "imagem": "encaixe.png", "fundo": "fundo-encaixe.png", "recuo": 0.1, "margem": 0.06 },
  // "pontas": { "esquerda": "ponta-esquerda.png", "altura": 1.7, "sobreposicao": 0.3, "deslocamentoY": 0 },
  "quadro": { "moldura": "moldura-retrato.png", "fundoBarra": { "imagem": "barra-fundo.png", "margens": [6, 6] }, "barraCpu": "barra-cpu.png", "barraRam": "barra-ram.png" },
  "medalhao": "medalhao.png",
  "botaoRedondo": "botao-redondo.png",
  "fonte": "Georgia",
  "corTexto": "#FFD100",
  "corTextoSecundario": "#E8D9B0"
}
'@
[IO.File]::WriteAllText((Join-Path $Saida 'skin.json'), $json, (New-Object Text.UTF8Encoding $false))
Get-ChildItem $Saida | Select Name, @{n='KB';e={[math]::Round($_.Length/1KB,1)}} | Format-Table -AutoSize | Out-String

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Theming;

namespace AuroraDesk.Theming;

/// <summary>
/// Desenha uma <see cref="DockSkin"/>: imagens congeladas, molduras cortadas em 9 partes (os cantos
/// não esticam), pontas espelhadas e a fonte da pasta da skin. Imagens grandes são reduzidas ao
/// carregar, para a skin não pesar na memória.
/// </summary>
internal sealed class DockSkinRenderer
{
    private const int MaxPixels = 512;
    private readonly Dictionary<string, BitmapSource> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource?[]> _slices = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Fator da redução ao carregar (1 = tamanho original): as margens vêm em pixels do original.</summary>
    private readonly Dictionary<string, double> _decodeScale = new(StringComparer.OrdinalIgnoreCase);

    private DockSkinRenderer(DockSkin skin) => Skin = skin;

    public DockSkin Skin { get; }
    public FontFamily? Font { get; private set; }
    public ArgbColor? TextColor => Skin.TextColor;
    public ArgbColor? SecondaryTextColor => Skin.SecondaryTextColor;

    /// <summary>Carrega a skin da pasta; problemas viram avisos no log e nunca derrubam o dock.</summary>
    public static DockSkinRenderer? TryLoad(string folder)
    {
        try
        {
            var warnings = new List<string>();
            var skin = DockSkinStore.Load(folder, warnings);
            var r = new DockSkinRenderer(skin);
            foreach (var img in skin.Images()) r.Load(img.Path);
            r.Font = LoadFont(skin);
            foreach (var w in warnings) Log.Warn($"Skin '{skin.Name}': {w}");
            Log.Info($"Skin do dock '{skin.Name}' carregada ({r._images.Count} imagem(ns))");
            return r;
        }
        catch (Exception ex)
        {
            Log.Error($"Não foi possível carregar a skin em '{folder}'", ex);
            return null;
        }
    }

    private void Load(string path)
    {
        if (_images.ContainsKey(path)) return;
        try
        {
            var bmp = Decode(path, 0);
            int original = bmp.PixelWidth;
            if (bmp.PixelWidth > MaxPixels || bmp.PixelHeight > MaxPixels)
                bmp = Decode(path, bmp.PixelWidth >= bmp.PixelHeight ? MaxPixels : 0, bmp.PixelHeight > bmp.PixelWidth ? MaxPixels : 0);
            _images[path] = bmp;
            _decodeScale[path] = original > 0 ? (double)bmp.PixelWidth / original : 1;
        }
        catch (Exception ex) { Log.Warn($"Imagem da skin ignorada '{path}': {ex.Message}"); }
    }

    private static BitmapImage Decode(string path, int width, int height = 0)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad; // não prende o arquivo: dá para editar com o app aberto
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.UriSource = new Uri(path);
        if (width > 0) bmp.DecodePixelWidth = width;
        if (height > 0) bmp.DecodePixelHeight = height;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Fonte de um .ttf/.otf da pasta da skin (pelo nome, ou a primeira) ou instalada.</summary>
    private static FontFamily? LoadFont(DockSkin skin)
    {
        bool hasFiles = Directory.EnumerateFiles(skin.Folder).Any(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));
        if (hasFiles)
        {
            var families = Fonts.GetFontFamilies(new Uri(skin.Folder.TrimEnd('\\') + "\\"));
            var match = families.FirstOrDefault(f => skin.Font is null || f.FamilyNames.Values.Any(n => n.Equals(skin.Font, StringComparison.OrdinalIgnoreCase)))
                ?? families.FirstOrDefault();
            if (match is not null) return match;
        }
        return string.IsNullOrWhiteSpace(skin.Font) ? null : new FontFamily(skin.Font);
    }

    public BitmapSource? Image(SkinImage? img) => img is not null && _images.TryGetValue(img.Path, out var b) ? b : null;

    /// <summary>Largura que a imagem ocupa numa altura dada (mantendo a proporção).</summary>
    public double WidthFor(SkinImage? img, double height) =>
        Image(img) is { } b && b.PixelHeight > 0 ? height * b.PixelWidth / b.PixelHeight : 0;

    public void Draw(DrawingContext dc, SkinImage? img, Rect r, bool mirror = false)
    {
        if (Image(img) is not { } b) return;
        if (mirror) dc.PushTransform(new ScaleTransform(-1, 1, r.X + r.Width / 2, 0));
        dc.DrawImage(b, r);
        if (mirror) dc.Pop();
    }

    /// <summary>
    /// Moldura em 9 partes: os cantos mantêm a proporção (escalados pela altura do destino), as
    /// bordas esticam num sentido só e o meio estica nos dois. Sem margens, a imagem só estica.
    /// </summary>
    public void DrawNineSlice(DrawingContext dc, SkinImage? img, Rect r)
    {
        if (Image(img) is not { } b || r.Width <= 0 || r.Height <= 0) return;
        var m = img!.Margins;
        if (m.IsEmpty) { dc.DrawImage(b, r); return; }
        var parts = Slices(img, b);
        double k = r.Height / b.PixelHeight * _decodeScale.GetValueOrDefault(img.Path, 1);
        double l = m.Left * k, t = m.Top * k, rr = m.Right * k, bb = m.Bottom * k;
        double squeeze = Math.Min(1, r.Width / Math.Max(1, l + rr));
        l *= squeeze; rr *= squeeze;
        double[] xs = [r.X, r.X + l, r.Right - rr, r.Right];
        double[] ys = [r.Y, r.Y + t, r.Bottom - bb, r.Bottom];
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
            {
                if (parts[j * 3 + i] is not { } part) continue;
                double w = xs[i + 1] - xs[i], h = ys[j + 1] - ys[j];
                if (w <= 0 || h <= 0) continue;
                // Meio pixel a mais nas peças internas esconde as emendas da escala.
                var dest = new Rect(xs[i], ys[j], w + (i < 2 ? 0.5 : 0), h + (j < 2 ? 0.5 : 0));
                // Coluna do meio: a textura se repete no comprimento em vez de esticar (pedra e
                // metal esticados viram listras). Na altura, a peça já está na escala certa.
                if (i == 1) dc.DrawRectangle(Tile(img.Path, j, part, part.PixelWidth * k / _decodeScale.GetValueOrDefault(img.Path, 1), dest), null, dest);
                else dc.DrawImage(part, dest);
            }
    }

    private readonly Dictionary<(string Path, int Row, int Width, int X, int Y, int Height), ImageBrush> _tiles = [];

    /// <summary>
    /// Pincel que repete a peça na horizontal a partir do início do destino, congelado e guardado.
    /// O cache tem limite: na ampliação do dock o painel muda de posição a cada quadro.
    /// </summary>
    private ImageBrush Tile(string path, int row, BitmapSource part, double tileWidth, Rect dest)
    {
        static int Q(double v) => (int)Math.Round(v * 4);
        var key = (path, row, Q(tileWidth), Q(dest.X), Q(dest.Y), Q(dest.Height));
        if (_tiles.TryGetValue(key, out var brush)) return brush;
        if (_tiles.Count > 96) _tiles.Clear();
        brush = new ImageBrush(part)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.Absolute,
            // Absoluto em relação ao elemento: começa no início do destino, para emendar com o canto.
            Viewport = new Rect(dest.X, dest.Y, Math.Max(1, tileWidth), dest.Height),
        };
        brush.Freeze();
        _tiles[key] = brush;
        return brush;
    }

    private BitmapSource?[] Slices(SkinImage img, BitmapSource b)
    {
        if (_slices.TryGetValue(img.Path, out var cached)) return cached;
        double f = _decodeScale.GetValueOrDefault(img.Path, 1);
        int w = b.PixelWidth, h = b.PixelHeight;
        int l = Math.Clamp((int)Math.Round(img.Margins.Left * f), 0, w / 2), r = Math.Clamp((int)Math.Round(img.Margins.Right * f), 0, w / 2);
        int t = Math.Clamp((int)Math.Round(img.Margins.Top * f), 0, h / 2), bt = Math.Clamp((int)Math.Round(img.Margins.Bottom * f), 0, h / 2);
        int[] xs = [0, l, w - r, w];
        int[] ys = [0, t, h - bt, h];
        var parts = new BitmapSource?[9];
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
            {
                int pw = xs[i + 1] - xs[i], ph = ys[j + 1] - ys[j];
                if (pw <= 0 || ph <= 0) continue;
                var crop = new CroppedBitmap(b, new Int32Rect(xs[i], ys[j], pw, ph));
                crop.Freeze();
                parts[j * 3 + i] = crop;
            }
        _slices[img.Path] = parts;
        return parts;
    }

    // ---------- peças ----------

    public bool HasPanel(char island) => Image(Skin.PanelFor(island)) is not null;
    public void DrawPanel(DrawingContext dc, char island, Rect r) => DrawNineSlice(dc, Skin.PanelFor(island), r);

    public bool HasSlot => Image(Skin.Slot) is not null || Image(Skin.SlotBackground) is not null;

    /// <summary>Encaixe de um ícone: fundo, o ícone um pouco menor e a moldura por cima.</summary>
    public void DrawSlot(DrawingContext dc, Rect icon, Action<Rect> drawIcon)
    {
        double outset = icon.Width * Skin.SlotOutset;
        var frame = new Rect(icon.X - outset, icon.Y - outset, icon.Width + 2 * outset, icon.Height + 2 * outset);
        Draw(dc, Skin.SlotBackground, frame);
        double inset = icon.Width * Skin.SlotInset;
        drawIcon(new Rect(icon.X + inset, icon.Y + inset, Math.Max(1, icon.Width - 2 * inset), Math.Max(1, icon.Height - 2 * inset)));
        Draw(dc, Skin.Slot, frame);
    }

    public bool HasCaps => Image(Skin.Caps?.Left) is not null;

    /// <summary>Quanto cada ponta passa para fora do painel (para a janela abrir espaço).</summary>
    public double CapsExtent(double panelThickness)
    {
        if (Skin.Caps is not { } c || !HasCaps) return 0;
        return WidthFor(c.Left, panelThickness * c.Height) * (1 - c.Overlap);
    }

    public double CapsHeight(double panelThickness) => HasCaps ? panelThickness * Skin.Caps!.Height : 0;

    /// <summary>Pontas alinhadas ao pé do painel, uma de cada lado (a direita espelhada se não tiver imagem).</summary>
    public void DrawCaps(DrawingContext dc, Rect panel)
    {
        if (Skin.Caps is not { } c || !HasCaps) return;
        double h = panel.Height * c.Height;
        double wl = WidthFor(c.Left, h), wr = c.MirrorRight ? wl : WidthFor(c.Right, h);
        double y = panel.Bottom - h + c.OffsetY;
        Draw(dc, c.Left, new Rect(panel.Left - wl * (1 - c.Overlap), y, wl, h));
        if (c.MirrorRight) Draw(dc, c.Left, new Rect(panel.Right - wr * c.Overlap, y, wr, h), mirror: true);
        else Draw(dc, c.Right, new Rect(panel.Right - wr * c.Overlap, y, wr, h));
    }
}

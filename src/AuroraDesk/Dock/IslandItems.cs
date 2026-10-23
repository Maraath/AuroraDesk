using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Theming;
using AuroraDesk.Theming;

namespace AuroraDesk.Dock;

/// <summary>Tudo que um item de ilha precisa para se medir e se desenhar.</summary>
internal sealed class IslandContext(ThemeService themes, double pixelsPerDip, DockSkinRenderer? skin = null, double contentHeight = 53)
{
    /// <summary>Altura da área dos itens dentro do painel (DIPs).</summary>
    public double ContentHeight { get; } = contentHeight;
    public static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public ThemeService Themes { get; } = themes;
    public Theme Theme => Themes.Current;
    public double PixelsPerDip { get; } = pixelsPerDip;
    /// <summary>Skin de imagens das ilhas (null = desenho do tema).</summary>
    public DockSkinRenderer? Skin { get; } = skin;
    public ArgbColor TextColor => Skin?.TextColor ?? Theme.Text;
    public ArgbColor SecondaryColor => Skin?.SecondaryTextColor ?? Theme.TextSecondary;

    public FormattedText Text(string text, double size, ArgbColor color, bool glyph = false, bool medium = false) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(glyph ? Glyphs : Skin?.Font ?? Themes.DefaultFont, FontStyles.Normal, medium ? FontWeights.Medium : FontWeights.Normal, FontStretches.Normal),
            size, Themes.Brush(color), PixelsPerDip);

    /// <summary>Fundo redondo da skin atrás de um ícone (bandeja, sistema), centrado em <paramref name="center"/>.</summary>
    public double DrawRoundButton(DrawingContext dc, Point center, double size)
    {
        if (Skin?.Image(Skin.Skin.RoundButton) is null) return 0;
        Skin.Draw(dc, Skin.Skin.RoundButton, new Rect(center.X - size / 2, center.Y - size / 2, size, size));
        return size;
    }
}

/// <summary>
/// Um item de ilha do dock (CPU, relógio, volume...). Lê medidas do agendador central: as
/// mesmas instâncias dos widgets, então nada é lido duas vezes.
/// </summary>
internal abstract class IslandItem
{
    protected IslandItem(string key) => Key = key;

    public string Key { get; }

    /// <summary>Medidas necessárias (assinadas pelo dock no agendador).</summary>
    public virtual IEnumerable<MeasureSpec> Specs => [];

    /// <summary>Instâncias das medidas, preenchidas pelo dock (mesma ordem de <see cref="Specs"/>).</summary>
    public List<Measure> Measures { get; } = [];

    /// <summary>Some do dock quando não faz sentido (ex.: bateria num desktop, clima desligado).</summary>
    public virtual bool Visible => true;

    public virtual bool Clickable => false;
    public virtual string? Tooltip => null;

    public abstract double Width(IslandContext ctx);

    /// <summary>Altura que o item quer (DIPs); 0 = cabe no painel. Maior que o painel, a ilha cresce para cima.</summary>
    public virtual double Height(IslandContext ctx) => 0;
    public abstract void Draw(DrawingContext dc, IslandContext ctx, Rect area);

    public virtual void Click() { }
    public virtual void Wheel(int delta) { }

    protected Measure? M(int i) => i < Measures.Count ? Measures[i] : null;

    /// <summary>Diâmetro do fundo redondo da skin atrás de ícones (0 sem skin).</summary>
    protected static double RoundButtonSize(IslandContext ctx) => ctx.Skin?.Image(ctx.Skin.Skin.RoundButton) is not null ? 32 : 0;

    protected static MeasureSpec Spec(string type, params (string, string)[] options) =>
        new(type, options.ToDictionary(o => o.Item1, o => o.Item2));
}

/// <summary>Estatística da ilha esquerda: rótulo pequeno em cima, valor e (opcional) barrinha de uso.</summary>
internal sealed class StatItem(string key, string label, MeasureSpec spec, string sample, bool bar, Func<Measure, string> value, Action? click = null, string? tooltip = null)
    : IslandItem(key)
{
    public override IEnumerable<MeasureSpec> Specs => [spec];
    public override bool Clickable => click is not null;
    public override string? Tooltip => tooltip;
    /// <summary>Visibilidade pela medida (ex.: bateria só aparece se o PC tiver bateria).</summary>
    public Func<Measure?, bool>? IsVisible { get; init; }
    public override bool Visible => IsVisible?.Invoke(M(0)) ?? true;

    private string Label => label;
    private string Value => M(0) is { } m ? value(m) : "--";

    public override double Width(IslandContext ctx)
    {
        // Largura mínima pelo valor de exemplo: os números mudam sem a ilha "pular".
        double w = Math.Max(ctx.Text(Label, 11, ctx.SecondaryColor).Width, ctx.Text(sample, 13, ctx.TextColor).Width);
        return Math.Ceiling(Math.Max(w, ctx.Text(Value, 13, ctx.TextColor).Width));
    }

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        var labelText = ctx.Text(Label, 11, ctx.SecondaryColor);
        var valueText = ctx.Text(Value, 13, ctx.TextColor);
        valueText.MaxTextWidth = Math.Max(1, area.Width);
        valueText.Trimming = TextTrimming.CharacterEllipsis;
        valueText.MaxLineCount = 1;
        // Sempre reserva o espaço da barrinha: itens com e sem barra ficam com o texto alinhado.
        double total = labelText.Height + valueText.Height + 6;
        double y = area.Y + (area.Height - total) / 2;
        dc.DrawText(labelText, new Point(area.X, y));
        dc.DrawText(valueText, new Point(area.X, y + labelText.Height - 1));
        if (bar && M(0) is { } m)
        {
            double by = y + labelText.Height + valueText.Height + 2;
            var track = new Rect(area.X, by, area.Width, 3);
            dc.DrawRoundedRectangle(ctx.Themes.Brush(ctx.Theme.Surface), null, track, 1.5, 1.5);
            if (m.Fraction > 0)
                dc.DrawRoundedRectangle(ctx.Themes.Brush(ctx.Theme.Accent), null, new Rect(track.X, track.Y, Math.Max(3, track.Width * m.Fraction), 3), 1.5, 1.5);
        }
    }

    public override void Click() => click?.Invoke();
}

/// <summary>Ícone de sistema (conexão, volume, bateria) com texto opcional ao lado.</summary>
internal sealed class GlyphItem(string key, MeasureSpec spec, Func<Measure?, string> glyph, Func<Measure?, string?> text, Func<Measure?, string?> tooltip, Action click)
    : IslandItem(key)
{
    public override IEnumerable<MeasureSpec> Specs => [spec];
    public override bool Clickable => true;
    public override string? Tooltip => tooltip(M(0));
    public Func<Measure?, bool>? IsVisible { get; init; }
    public override bool Visible => IsVisible?.Invoke(M(0)) ?? true;
    public Action<int>? OnWheel { get; init; }

    public override double Width(IslandContext ctx)
    {
        double w = Math.Max(ctx.Text(glyph(M(0)), 16, ctx.TextColor, glyph: true).Width, RoundButtonSize(ctx));
        if (text(M(0)) is { Length: > 0 } t) w += 4 + Math.Max(ctx.Text(t, 13, ctx.TextColor).Width, ctx.Text("100%", 13, ctx.TextColor).Width);
        return Math.Ceiling(w);
    }

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        var g = ctx.Text(glyph(M(0)), 16, ctx.TextColor, glyph: true);
        double slot = Math.Max(g.Width, RoundButtonSize(ctx));
        double cy = area.Y + area.Height / 2;
        ctx.DrawRoundButton(dc, new Point(area.X + slot / 2, cy), RoundButtonSize(ctx));
        dc.DrawText(g, new Point(area.X + (slot - g.Width) / 2, cy - g.Height / 2));
        if (text(M(0)) is { Length: > 0 } t)
        {
            var ft = ctx.Text(t, 13, ctx.TextColor);
            dc.DrawText(ft, new Point(area.X + slot + 4, cy - ft.Height / 2));
        }
    }

    public override void Click() => click();
    public override void Wheel(int delta) => OnWheel?.Invoke(delta);
}

/// <summary>Botão só com ícone (ex.: a seta ^ dos ícones ocultos da bandeja).</summary>
internal sealed class IconButtonItem(string key, string glyph, string tooltip, Action click) : IslandItem(key)
{
    public override bool Clickable => true;
    public override string? Tooltip => tooltip;

    public override double Width(IslandContext ctx) => Math.Ceiling(Math.Max(ctx.Text(glyph, 14, ctx.SecondaryColor, glyph: true).Width, RoundButtonSize(ctx)));

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        var g = ctx.Text(glyph, 14, ctx.SecondaryColor, glyph: true);
        double cx = area.X + area.Width / 2, cy = area.Y + area.Height / 2;
        ctx.DrawRoundButton(dc, new Point(cx, cy), RoundButtonSize(ctx));
        dc.DrawText(g, new Point(cx - g.Width / 2, cy - g.Height / 2));
    }

    public override void Click() => click();
}

/// <summary>
/// "Mostrar área de trabalho", como o canto da barra do Windows: só um traço vertical fino no
/// fim da ilha direita. Minimiza tudo; outro clique devolve as janelas.
/// </summary>
internal sealed class ShowDesktopItem(Action click) : IslandItem("area-de-trabalho")
{
    public override bool Clickable => true;
    public override string? Tooltip => "Mostrar a área de trabalho";

    public override double Width(IslandContext ctx) => 4;

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        double h = Math.Min(18, area.Height - 8);
        var line = new Rect(area.X + 1.5, area.Y + (area.Height - h) / 2, 1, h);
        dc.DrawRectangle(ctx.Themes.Brush(ctx.SecondaryColor.WithOpacity(0.45)), null, line);
    }

    public override void Click() => click();
}

/// <summary>Relógio de duas linhas: hora e data curta ("qua, 21 out").</summary>
internal sealed class ClockItem(Action click) : IslandItem("relogio")
{
    private static readonly MeasureSpec TimeSpec = Spec("relogio", ("formato", "HH:mm"));
    private static readonly MeasureSpec DateSpec = Spec("relogio", ("formato", "ddd, d MMM"));

    public override IEnumerable<MeasureSpec> Specs => [TimeSpec, DateSpec];
    public override bool Clickable => true;
    public override string? Tooltip => M(0) is ClockMeasure c ? DateFormatter.Format(c.Time, "dddd, d 'de' MMMM 'de' yyyy", Strings.Language) : null;

    /// <summary>Diâmetro do medalhão da skin (o relógio fica dentro dele), ou 0 sem medalhão.</summary>
    private static double Medallion(IslandContext ctx) => ctx.Skin?.Image(ctx.Skin.Skin.Medallion) is not null ? 62 : 0;

    public override double Width(IslandContext ctx) =>
        Math.Ceiling(Math.Max(Medallion(ctx), Math.Max(ctx.Text(M(0)?.Text ?? "00:00", 13, ctx.TextColor, medium: true).Width, ctx.Text(M(1)?.Text ?? "qua, 21 out", 11, ctx.SecondaryColor).Width)));

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        var time = ctx.Text(M(0)?.Text ?? "--:--", 13, ctx.TextColor, medium: true);
        var date = ctx.Text(M(1)?.Text ?? "", 11, ctx.SecondaryColor);
        double y = area.Y + (area.Height - time.Height - date.Height + 1) / 2;
        if (Medallion(ctx) is > 0 and var d)
        {
            // Medalhão (como o do minimapa) passando um pouco das bordas do painel; texto no centro.
            double cx = area.X + area.Width / 2, cy = area.Y + area.Height / 2;
            ctx.Skin!.Draw(dc, ctx.Skin.Skin.Medallion, new Rect(cx - d / 2, cy - d / 2, d, d));
            // Dentro do aro cabe só a data curta ("qui, 22"), como no relógio do minimapa.
            string full = M(1)?.Text ?? "";
            int cut = full.LastIndexOf(' ');
            date = ctx.Text(cut > 0 ? full[..cut] : full, 11, ctx.SecondaryColor);
            dc.DrawText(time, new Point(cx - time.Width / 2, y));
            dc.DrawText(date, new Point(cx - date.Width / 2, y + time.Height - 1));
            return;
        }
        dc.DrawText(time, new Point(area.Right - time.Width, y));
        dc.DrawText(date, new Point(area.Right - date.Width, y + time.Height - 1));
    }

    public override void Click() => click();
}

/// <summary>Ícone do Aurora Desk (substitui o ícone da bandeja, que some com a barra do Windows).</summary>
internal sealed class AuroraItem(Action click) : IslandItem("aurora")
{
    private static BitmapSource? _icon;

    public override bool Clickable => true;
    public override string? Tooltip => "Aurora Desk";

    public override double Width(IslandContext ctx) => 16;

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        _icon ??= LoadIcon();
        var r = new Rect(area.X, area.Y + (area.Height - 16) / 2, 16, 16);
        if (_icon is not null) dc.DrawImage(_icon, r);
        else dc.DrawEllipse(ctx.Themes.Brush(ctx.Theme.Accent), null, new Point(r.X + 8, r.Y + 8), 7, 7);
    }

    /// <summary>Ícone do app (também o retrato padrão do quadro do jogador).</summary>
    internal static BitmapSource? Icon => _icon ??= LoadIcon();

    private static BitmapSource? LoadIcon()
    {
        try
        {
            var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/aurora.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    public override void Click() => click();
}

/// <summary>
/// Quadro do jogador (só com skin): retrato redondo com moldura e duas barras sobrepostas,
/// CPU como "vida" e RAM como "mana". Substitui os itens CPU e RAM da ilha esquerda.
/// </summary>
internal sealed class UnitFrameItem(MeasureSpec cpu, MeasureSpec ram, Action click) : IslandItem("quadro")
{
    private const double BarWidth = 132, BarHeight = 15, BarGap = 5;

    public override IEnumerable<MeasureSpec> Specs => [cpu, ram];
    public override bool Clickable => true;
    public override string? Tooltip => "Processador e memória · clique para abrir o Gerenciador de Tarefas";

    private static double PortraitSize => 54;

    /// <summary>Quadro inteiro numa imagem (como o de um jogo): altura de desenho e a imagem.</summary>
    private static (BitmapSource Image, double Height)? Full(IslandContext ctx)
    {
        var f = ctx.Skin?.Skin.UnitFrame;
        if (f is not { HasFullFrame: true } || ctx.Skin!.Image(f.Full) is not { } img) return null;
        // Maior que a ilha, como o quadro do jogador num jogo: as barras ficam legíveis.
        return (img, (ctx.ContentHeight + 12) * 1.45);
    }

    public override double Height(IslandContext ctx) => Full(ctx)?.Height ?? 0;

    public override double Width(IslandContext ctx) =>
        Full(ctx) is { } full ? Math.Ceiling(full.Height * full.Image.PixelWidth / full.Image.PixelHeight) : PortraitSize + 8 + BarWidth;

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        if (Full(ctx) is { } full) { DrawFull(dc, ctx, area, full.Image, full.Height); return; }
        var skin = ctx.Skin;
        var frame = skin?.Skin.UnitFrame;
        double d = PortraitSize, cy = area.Y + area.Height / 2;
        var portrait = new Rect(area.X, cy - d / 2, d, d);

        // Retrato: imagem da skin (ou o ícone do app) recortada em círculo, moldura por cima.
        double inset = d * 0.13;
        var inner = new Rect(portrait.X + inset, portrait.Y + inset, d - 2 * inset, d - 2 * inset);
        dc.PushClip(new EllipseGeometry(inner));
        dc.DrawRectangle(ctx.Themes.Brush(new ArgbColor(255, 0x1A, 0x14, 0x10)), null, inner);
        if (skin?.Image(frame?.Portrait) is { } img) dc.DrawImage(img, inner);
        else if (AuroraItem.Icon is { } icon) dc.DrawImage(icon, new Rect(inner.X + inner.Width * 0.15, inner.Y + inner.Height * 0.15, inner.Width * 0.7, inner.Height * 0.7));
        dc.Pop();
        if (skin?.Image(frame?.Frame) is not null) skin.Draw(dc, frame!.Frame, portrait);
        else dc.DrawEllipse(null, ctx.Themes.Pen(ctx.SecondaryColor, 2), new Point(portrait.X + d / 2, cy), d / 2 - 2, d / 2 - 2);

        double x = portrait.Right + 8;
        double top = cy - (2 * BarHeight + BarGap) / 2;
        Bar(dc, ctx, new Rect(x, top, BarWidth, BarHeight), M(0), frame?.CpuBar, new ArgbColor(255, 0x1E, 0xA8, 0x36), "CPU");
        Bar(dc, ctx, new Rect(x, top + BarHeight + BarGap, BarWidth, BarHeight), M(1), frame?.RamBar, new ArgbColor(255, 0x2A, 0x63, 0xD6), "RAM");
    }

    /// <summary>
    /// Quadro inteiro: retrato e barras por baixo, nos lugares que as áreas indicam, e a moldura
    /// por cima (os furos dela são transparentes), como o quadro do jogador num jogo.
    /// </summary>
    private void DrawFull(DrawingContext dc, IslandContext ctx, Rect area, BitmapSource img, double height)
    {
        var f = ctx.Skin!.Skin.UnitFrame!;
        double k = height / img.PixelHeight;
        // Pé do quadro rente ao pé da ilha; o que sobra cresce para cima.
        var box = new Rect(area.X, Math.Min(area.Y + (area.Height - height) / 2, area.Bottom + 6 - height), img.PixelWidth * k, height);
        Rect Map(SkinRect r) => new(box.X + r.X * k, box.Y + r.Y * k, r.Width * k, r.Height * k);

        var portrait = Map(f.PortraitArea!.Value);
        dc.PushClip(new EllipseGeometry(portrait));
        dc.DrawRectangle(ctx.Themes.Brush(new ArgbColor(255, 0x12, 0x0E, 0x0A)), null, portrait);
        if (ctx.Skin.Image(f.Portrait) is { } face) dc.DrawImage(face, portrait);
        else if (AuroraItem.Icon is { } icon) dc.DrawImage(icon, new Rect(portrait.X + portrait.Width * 0.18, portrait.Y + portrait.Height * 0.18, portrait.Width * 0.64, portrait.Height * 0.64));
        dc.Pop();

        FullBar(dc, ctx, Map(f.CpuArea!.Value), M(0), f.CpuBar, new ArgbColor(255, 0, 0xC8, 0), "CPU");
        FullBar(dc, ctx, Map(f.RamArea!.Value), M(1), f.RamBar, new ArgbColor(255, 0, 0x30, 0xE0), "RAM");
        if (f.NameArea is { } na)
        {
            var nr = Map(na);
            dc.DrawRectangle(ctx.Themes.Brush(new ArgbColor(150, 0, 0, 0)), null, nr);
            Outlined(dc, ctx, f.NameText ?? "Aurora", Math.Min(12, nr.Height * 0.8), ctx.TextColor, nr);
        }
        dc.DrawImage(img, box);
    }

    private static void FullBar(DrawingContext dc, IslandContext ctx, Rect r, Measure? m, SkinImage? fill, ArgbColor fallback, string label)
    {
        dc.DrawRectangle(ctx.Themes.Brush(new ArgbColor(200, 0, 0, 0)), null, r);
        double frac = Math.Clamp(m?.Fraction ?? 0, 0, 1);
        if (frac > 0)
        {
            dc.PushClip(new RectangleGeometry(new Rect(r.X, r.Y, r.Width * frac, r.Height)));
            if (ctx.Skin?.Image(fill) is not null) ctx.Skin.Draw(dc, fill, r);
            else dc.DrawRectangle(ctx.Themes.Brush(fallback), null, r);
            dc.Pop();
        }
        Outlined(dc, ctx, $"{label} {m?.Value ?? 0:0}%", Math.Min(11, r.Height * 0.95), new ArgbColor(255, 0xFF, 0xFF, 0xFF), r);
    }

    /// <summary>Texto centrado com contorno escuro (legível em cima de qualquer barra).</summary>
    private static void Outlined(DrawingContext dc, IslandContext ctx, string s, double size, ArgbColor color, Rect r)
    {
        var text = ctx.Text(s, size, color, medium: true);
        var shadow = ctx.Text(s, size, new ArgbColor(230, 0, 0, 0), medium: true);
        var at = new Point(r.X + (r.Width - text.Width) / 2, r.Y + (r.Height - text.Height) / 2);
        foreach (var (ox, oy) in new[] { (-1.0, 0.0), (1.0, 0.0), (0.0, -1.0), (0.0, 1.0) }) dc.DrawText(shadow, new Point(at.X + ox, at.Y + oy));
        dc.DrawText(text, at);
    }

    private static void Bar(DrawingContext dc, IslandContext ctx, Rect r, Measure? m, SkinImage? fill, ArgbColor fallback, string label)
    {
        var skin = ctx.Skin;
        var bg = skin?.Skin.UnitFrame?.BarBackground;
        if (skin?.Image(bg) is not null) skin.DrawNineSlice(dc, bg, r);
        else dc.DrawRectangle(ctx.Themes.Brush(new ArgbColor(255, 0x08, 0x08, 0x0A)), ctx.Themes.Pen(ctx.SecondaryColor.WithOpacity(0.6), 1), r);

        double frac = Math.Clamp(m?.Fraction ?? 0, 0, 1);
        var filled = new Rect(r.X + 1, r.Y + 1, Math.Max(0, (r.Width - 2) * frac), r.Height - 2);
        if (filled.Width > 0)
        {
            // A imagem da barra estica no tamanho cheio e é recortada no valor atual.
            dc.PushClip(new RectangleGeometry(filled));
            if (skin?.Image(fill) is not null) skin.Draw(dc, fill, new Rect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2));
            else dc.DrawRectangle(ctx.Themes.Brush(fallback), null, filled);
            dc.Pop();
        }

        var text = ctx.Text($"{label} {m?.Value ?? 0:0}%", 11, new ArgbColor(255, 0xFF, 0xFF, 0xFF), medium: true);
        var at = new Point(r.X + (r.Width - text.Width) / 2, r.Y + (r.Height - text.Height) / 2);
        // Contorno escuro, como o texto das barras de vida dos jogos: legível em qualquer cor.
        var outline = ctx.Text($"{label} {m?.Value ?? 0:0}%", 11, new ArgbColor(230, 0, 0, 0), medium: true);
        foreach (var (ox, oy) in new[] { (-1.0, 0.0), (1.0, 0.0), (0.0, -1.0), (0.0, 1.0) })
            dc.DrawText(outline, new Point(at.X + ox, at.Y + oy));
        dc.DrawText(text, at);
    }

    public override void Click() => click();
}
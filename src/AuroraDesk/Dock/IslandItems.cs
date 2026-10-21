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
internal sealed class IslandContext(ThemeService themes, double pixelsPerDip)
{
    public static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public ThemeService Themes { get; } = themes;
    public Theme Theme => Themes.Current;
    public double PixelsPerDip { get; } = pixelsPerDip;

    public FormattedText Text(string text, double size, ArgbColor color, bool glyph = false, bool medium = false) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(glyph ? Glyphs : Themes.DefaultFont, FontStyles.Normal, medium ? FontWeights.Medium : FontWeights.Normal, FontStretches.Normal),
            size, Themes.Brush(color), PixelsPerDip);
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
    public abstract void Draw(DrawingContext dc, IslandContext ctx, Rect area);

    public virtual void Click() { }
    public virtual void Wheel(int delta) { }

    protected Measure? M(int i) => i < Measures.Count ? Measures[i] : null;

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
        double w = Math.Max(ctx.Text(Label, 11, ctx.Theme.TextSecondary).Width, ctx.Text(sample, 13, ctx.Theme.Text).Width);
        return Math.Ceiling(Math.Max(w, ctx.Text(Value, 13, ctx.Theme.Text).Width));
    }

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        var labelText = ctx.Text(Label, 11, ctx.Theme.TextSecondary);
        var valueText = ctx.Text(Value, 13, ctx.Theme.Text);
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
        double w = ctx.Text(glyph(M(0)), 16, ctx.Theme.Text, glyph: true).Width;
        if (text(M(0)) is { Length: > 0 } t) w += 4 + Math.Max(ctx.Text(t, 13, ctx.Theme.Text).Width, ctx.Text("100%", 13, ctx.Theme.Text).Width);
        return Math.Ceiling(w);
    }

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        var g = ctx.Text(glyph(M(0)), 16, ctx.Theme.Text, glyph: true);
        dc.DrawText(g, new Point(area.X, area.Y + (area.Height - g.Height) / 2));
        if (text(M(0)) is { Length: > 0 } t)
        {
            var ft = ctx.Text(t, 13, ctx.Theme.Text);
            dc.DrawText(ft, new Point(area.X + g.Width + 4, area.Y + (area.Height - ft.Height) / 2));
        }
    }

    public override void Click() => click();
    public override void Wheel(int delta) => OnWheel?.Invoke(delta);
}

/// <summary>Relógio de duas linhas: hora e data curta ("qua, 21 out").</summary>
internal sealed class ClockItem(Action click) : IslandItem("relogio")
{
    private static readonly MeasureSpec TimeSpec = Spec("relogio", ("formato", "HH:mm"));
    private static readonly MeasureSpec DateSpec = Spec("relogio", ("formato", "ddd, d MMM"));

    public override IEnumerable<MeasureSpec> Specs => [TimeSpec, DateSpec];
    public override bool Clickable => true;
    public override string? Tooltip => M(0) is ClockMeasure c ? DateFormatter.Format(c.Time, "dddd, d 'de' MMMM 'de' yyyy", Strings.Language) : null;

    public override double Width(IslandContext ctx) =>
        Math.Ceiling(Math.Max(ctx.Text(M(0)?.Text ?? "00:00", 13, ctx.Theme.Text, medium: true).Width, ctx.Text(M(1)?.Text ?? "qua, 21 out", 11, ctx.Theme.TextSecondary).Width));

    public override void Draw(DrawingContext dc, IslandContext ctx, Rect area)
    {
        var time = ctx.Text(M(0)?.Text ?? "--:--", 13, ctx.Theme.Text, medium: true);
        var date = ctx.Text(M(1)?.Text ?? "", 11, ctx.Theme.TextSecondary);
        double y = area.Y + (area.Height - time.Height - date.Height + 1) / 2;
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

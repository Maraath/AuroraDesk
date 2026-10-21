using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Skins;
using AuroraDesk.Core.Theming;
using AuroraDesk.Theming;

namespace AuroraDesk.Widgets;

/// <summary>Tudo que um elemento precisa para se desenhar.</summary>
internal sealed class RenderContext(ThemeService themes, Theme theme, SkinDefinition skin, Func<string, Measure?> resolve, double pixelsPerDip, string? instanceFont = null)
{
    /// <summary>Ordem de precedência da fonte: elemento → widget (configurações) → skin → tema/global.</summary>
    private readonly string? _widgetFont = string.IsNullOrWhiteSpace(instanceFont) ? skin.FontFamily : instanceFont;

    public ThemeService Themes { get; } = themes;
    public Theme Theme { get; } = theme;
    public SkinDefinition Skin { get; } = skin;
    public Func<string, Measure?> Resolve { get; } = resolve;
    public double PixelsPerDip { get; } = pixelsPerDip;

    public SolidColorBrush Brush(string? spec, ArgbColor fallback) => Themes.Brush(Theme.Resolve(spec, fallback));
    public Pen Pen(string? spec, ArgbColor fallback, double thickness) => Themes.Pen(Theme.Resolve(spec, fallback), thickness);
    public FontFamily Font(string? element) => Themes.Font(element ?? _widgetFont);
    public double FontSize(double? element) => element ?? Skin.FontSize ?? Theme.FontSize;
}

/// <summary>
/// Um elemento visual (meter) = um DrawingVisual. Só os elementos cujas medidas mudaram
/// são redesenhados; os demais ficam como estão na árvore visual.
/// </summary>
internal abstract class ElementRenderer
{
    private bool _errorLogged;

    protected ElementRenderer(ElementDef def) => Def = def;

    public ElementDef Def { get; }
    public DrawingVisual Visual { get; } = new();

    /// <summary>Área ocupada (para clique). Texto calcula ao desenhar.</summary>
    public virtual Rect Bounds => new(Def.X, Def.Y, Def.Width, Def.Height);

    public void Render(RenderContext ctx)
    {
        using var dc = Visual.RenderOpen();
        try
        {
            if (Def.Opacity < 1) dc.PushOpacity(Def.Opacity);
            Draw(dc, ctx);
            if (Def.Opacity < 1) dc.Pop();
            _errorLogged = false;
        }
        catch (Exception ex)
        {
            if (!_errorLogged) Core.Logging.Log.Error($"Erro ao desenhar {Def.GetType().Name} em '{ctx.Skin.Name}'", ex);
            _errorLogged = true;
            dc.DrawRectangle(null, ctx.Themes.Pen(ArgbColor.Parse("#F28B82"), 1), Bounds);
        }
    }

    /// <summary>Chamado antes de redesenhar quando uma medida da qual depende foi lida.</summary>
    public virtual void OnMeasureRead(string name, Measure measure) { }

    protected abstract void Draw(DrawingContext dc, RenderContext ctx);

    protected static Measure? M(RenderContext ctx, string name) => ctx.Resolve(name);

    public static ElementRenderer Create(ElementDef def) => def switch
    {
        TextElementDef t => new TextRenderer(t),
        BarElementDef b => new BarRenderer(b),
        ArcElementDef a => new ArcRenderer(a),
        GraphElementDef g => new GraphRenderer(g),
        ImageElementDef i => new ImageRenderer(i),
        ShapeElementDef s => new ShapeRenderer(s),
        AnalogClockElementDef c => new AnalogClockRenderer(c),
        _ => throw new NotSupportedException(def.GetType().Name),
    };
}

internal sealed class TextRenderer(TextElementDef def) : ElementRenderer(def)
{
    private Rect _bounds;
    public override Rect Bounds => _bounds;

    protected override void Draw(DrawingContext dc, RenderContext ctx)
    {
        string text = def.Template.Render(ctx.Resolve);
        var typeface = new Typeface(ctx.Font(def.FontFamily), def.Italic ? FontStyles.Italic : FontStyles.Normal, ParseWeight(def.Weight), FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface,
            ctx.FontSize(def.FontSize), ctx.Brush(def.Color, ctx.Theme.Text), ctx.PixelsPerDip);

        double x = def.X;
        if (def.Width > 0)
        {
            // Caixa de largura fixa: alinhamento dentro dela, com reticências se não couber.
            ft.MaxTextWidth = def.Width;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            ft.MaxLineCount = def.Height > 0 ? Math.Max(1, (int)(def.Height / ft.Height)) : 1;
            ft.TextAlignment = def.Align switch { TextAlign.Center => TextAlignment.Center, TextAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
        }
        else
        {
            // Sem largura: "x" é a âncora (esquerda, centro ou direita do texto).
            x -= def.Align switch { TextAlign.Center => ft.Width / 2, TextAlign.Right => ft.Width, _ => 0 };
        }

        _bounds = new Rect(def.Width > 0 ? def.X : x, def.Y, def.Width > 0 ? def.Width : ft.Width, ft.Height);
        dc.DrawText(ft, new Point(x, def.Y));
    }

    internal static FontWeight ParseWeight(string weight) => weight.ToLowerInvariant() switch
    {
        "thin" or "fina" => FontWeights.Thin,
        "extralight" or "ultraleve" => FontWeights.ExtraLight,
        "light" or "leve" => FontWeights.Light,
        "medium" or "media" => FontWeights.Medium,
        "semibold" or "seminegrito" => FontWeights.SemiBold,
        "bold" or "negrito" => FontWeights.Bold,
        "extrabold" => FontWeights.ExtraBold,
        "black" or "pesada" => FontWeights.Black,
        _ => FontWeights.Normal,
    };
}

internal sealed class BarRenderer(BarElementDef def) : ElementRenderer(def)
{
    protected override void Draw(DrawingContext dc, RenderContext ctx)
    {
        double fraction = M(ctx, def.Measure)?.Fraction ?? 0;
        var rect = new Rect(def.X, def.Y, def.Width, def.Height);
        double r = def.Radius ?? Math.Min(def.Width, def.Height) / 2;

        if (def.Background is not null)
            dc.DrawRoundedRectangle(ctx.Brush(def.Background, ctx.Theme.Surface), null, rect, r, r);

        if (fraction <= 0) return;
        Rect fill = def.Vertical
            ? new Rect(rect.X, rect.Bottom - rect.Height * fraction, rect.Width, rect.Height * fraction)
            : new Rect(rect.X, rect.Y, rect.Width * fraction, rect.Height);
        double fr = Math.Min(r, Math.Min(fill.Width, fill.Height) / 2);
        dc.DrawRoundedRectangle(ctx.Brush(def.Color, ctx.Theme.Accent), null, fill, fr, fr);
    }
}

internal sealed class ArcRenderer(ArcElementDef def) : ElementRenderer(def)
{
    protected override void Draw(DrawingContext dc, RenderContext ctx)
    {
        double fraction = M(ctx, def.Measure)?.Fraction ?? 0;
        var center = new Point(def.X + def.Width / 2, def.Y + def.Height / 2);
        double radius = Math.Min(def.Width, def.Height) / 2 - def.Thickness / 2;
        if (radius <= 0) return;

        if (def.Background is not null)
            dc.DrawGeometry(null, ctx.Pen(def.Background, ctx.Theme.Surface, def.Thickness), Arc(center, radius, def.StartAngle, def.Sweep));
        if (fraction > 0)
            dc.DrawGeometry(null, ctx.Pen(def.Color, ctx.Theme.Accent, def.Thickness), Arc(center, radius, def.StartAngle, def.Sweep * fraction));
    }

    /// <summary>Arco em graus (0° = direita, sentido horário). 360° vira círculo completo.</summary>
    internal static Geometry Arc(Point c, double r, double start, double sweep)
    {
        if (sweep >= 359.99)
        {
            var circle = new EllipseGeometry(c, r, r);
            circle.Freeze();
            return circle;
        }
        static Point At(Point c, double r, double deg)
        {
            double rad = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(At(c, r, start), false, false);
            s.ArcTo(At(c, r, start + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        return g;
    }
}

internal sealed class GraphRenderer : ElementRenderer
{
    private readonly GraphElementDef _def;
    private readonly double[] _samples;
    private int _count, _next;
    private double _max = 1;

    public GraphRenderer(GraphElementDef def) : base(def)
    {
        _def = def;
        _samples = new double[def.Samples];
    }

    public override void OnMeasureRead(string name, Measure measure)
    {
        _samples[_next] = measure.Value;
        _next = (_next + 1) % _samples.Length;
        _count = Math.Min(_count + 1, _samples.Length);
        _max = measure.Max > 0 ? measure.Max : 1;
    }

    protected override void Draw(DrawingContext dc, RenderContext ctx)
    {
        if (_count < 2) return;
        double max = _max;
        for (int i = 0; i < _count; i++) max = Math.Max(max, _samples[Index(i)]);

        double step = _def.Width / (_samples.Length - 1);
        double x0 = _def.X + _def.Width - step * (_count - 1);
        double bottom = _def.Y + _def.Height;
        double Y(int i) => bottom - Math.Clamp(_samples[Index(i)] / max, 0, 1) * _def.Height;

        if (_def.Bars)
        {
            var brush = ctx.Brush(_def.Color, ctx.Theme.Accent);
            double w = Math.Max(1, step - 1);
            for (int i = 0; i < _count; i++)
            {
                double y = Y(i);
                if (bottom - y >= 0.5) dc.DrawRectangle(brush, null, new Rect(x0 + i * step - w / 2, y, w, bottom - y));
            }
            return;
        }

        var line = new StreamGeometry();
        using (var s = line.Open())
        {
            s.BeginFigure(new Point(x0, Y(0)), false, false);
            for (int i = 1; i < _count; i++) s.LineTo(new Point(x0 + i * step, Y(i)), true, true);
        }
        line.Freeze();

        if (_def.Fill is not null)
        {
            var area = new StreamGeometry();
            using (var s = area.Open())
            {
                s.BeginFigure(new Point(x0, bottom), true, true);
                for (int i = 0; i < _count; i++) s.LineTo(new Point(x0 + i * step, Y(i)), false, false);
                s.LineTo(new Point(x0 + (_count - 1) * step, bottom), false, false);
            }
            area.Freeze();
            dc.DrawGeometry(ctx.Brush(_def.Fill, ctx.Theme.Accent.WithOpacity(0.2)), null, area);
        }
        dc.DrawGeometry(null, ctx.Pen(_def.Color, ctx.Theme.Accent, _def.Thickness), line);
    }

    private int Index(int i) => (_next - _count + i + _samples.Length) % _samples.Length;
}

internal sealed class ImageRenderer(ImageElementDef def) : ElementRenderer(def)
{
    private BitmapSource? _image;
    private bool _failed;

    protected override void Draw(DrawingContext dc, RenderContext ctx)
    {
        if (_image is null && !_failed) _image = Load(ctx);
        if (_image is null) return;

        var box = new Rect(def.X, def.Y, def.Width, def.Height);
        Rect target = box;
        if (def.Fit == ImageFit.Uniform)
        {
            double scale = Math.Min(box.Width / _image.Width, box.Height / _image.Height);
            double w = _image.Width * scale, h = _image.Height * scale;
            target = new Rect(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }
        else if (def.Fit == ImageFit.None)
        {
            target = new Rect(box.X, box.Y, _image.Width, _image.Height);
            dc.PushClip(new RectangleGeometry(box));
            dc.DrawImage(_image, target);
            dc.Pop();
            return;
        }
        dc.DrawImage(_image, target);
    }

    private BitmapSource? Load(RenderContext ctx)
    {
        try
        {
            string path = Path.IsPathRooted(def.File) ? def.File : Path.Combine(ctx.Skin.Folder, def.File);
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path);
            img.CacheOption = BitmapCacheOption.OnLoad; // não mantém o arquivo aberto
            // Decodifica só no tamanho exibido (x2 para telas de alta densidade): economiza RAM.
            img.DecodePixelWidth = (int)Math.Ceiling(def.Width * Math.Max(ctx.PixelsPerDip, 1) * 2);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex)
        {
            _failed = true;
            Core.Logging.Log.Warn($"Imagem '{def.File}' de '{ctx.Skin.Name}': {ex.Message}");
            return null;
        }
    }
}

internal sealed class ShapeRenderer(ShapeElementDef def) : ElementRenderer(def)
{
    protected override void Draw(DrawingContext dc, RenderContext ctx)
    {
        var fill = def.Fill is null ? null : ctx.Brush(def.Fill, ctx.Theme.Surface);
        var pen = def.StrokeThickness > 0 ? ctx.Pen(def.Stroke, ctx.Theme.Border, def.StrokeThickness) : null;
        double inset = pen is null ? 0 : def.StrokeThickness / 2;
        var rect = new Rect(def.X + inset, def.Y + inset, Math.Max(0, def.Width - 2 * inset), Math.Max(0, def.Height - 2 * inset));

        if (def.Ellipse)
            dc.DrawEllipse(fill, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
        else
        {
            double r = Math.Min(def.Radius ?? ctx.Theme.CornerRadius, Math.Min(rect.Width, rect.Height) / 2);
            dc.DrawRoundedRectangle(fill, pen, rect, r, r);
        }
    }
}

internal sealed class AnalogClockRenderer(AnalogClockElementDef def) : ElementRenderer(def)
{
    protected override void Draw(DrawingContext dc, RenderContext ctx)
    {
        var time = (M(ctx, AnalogClockElementDef.ClockMeasureName) as ClockMeasure)?.Time ?? DateTime.Now;
        var c = new Point(def.X + def.Width / 2, def.Y + def.Height / 2);
        double r = Math.Min(def.Width, def.Height) / 2;

        if (def.Face is not null)
            dc.DrawEllipse(ctx.Brush(def.Face, ctx.Theme.Surface), null, c, r, r);

        var tickPen = ctx.Pen(def.Ticks, ctx.Theme.TextSecondary, Math.Max(1, r / 40));
        var hourTickPen = ctx.Pen(def.Ticks, ctx.Theme.TextSecondary, Math.Max(1.5, r / 22));
        for (int i = 0; i < 60; i += 5)
        {
            bool hour = i % 15 == 0;
            double a = i * 6 - 90;
            dc.DrawLine(hour ? hourTickPen : tickPen, Polar(c, r * (hour ? 0.80 : 0.86), a), Polar(c, r * 0.94, a));
        }

        double minutes = time.Minute + time.Second / 60.0;
        double hours = time.Hour % 12 + minutes / 60.0;
        dc.DrawLine(ctx.Pen(def.HourHand, ctx.Theme.Text, Math.Max(2, r / 14)), c, Polar(c, r * 0.5, hours * 30 - 90));
        dc.DrawLine(ctx.Pen(def.MinuteHand, ctx.Theme.Text, Math.Max(1.5, r / 22)), c, Polar(c, r * 0.74, minutes * 6 - 90));
        if (def.Seconds)
            dc.DrawLine(ctx.Pen(def.SecondHand, ctx.Theme.Accent, Math.Max(1, r / 60)), Polar(c, r * 0.12, time.Second * 6 + 90), Polar(c, r * 0.82, time.Second * 6 - 90));
        dc.DrawEllipse(ctx.Brush(def.SecondHand, ctx.Theme.Accent), null, c, Math.Max(2, r / 24), Math.Max(2, r / 24));
    }

    private static Point Polar(Point c, double r, double deg)
    {
        double rad = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}

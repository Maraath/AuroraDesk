using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Skins;
using AuroraDesk.Core.Theming;
using AuroraDesk.Theming;

namespace AuroraDesk.Widgets;

/// <summary>
/// Superfície de um widget: um FrameworkElement leve que hospeda um DrawingVisual por
/// elemento do skin (sem controles, sem Binding, sem layout aninhado).
/// </summary>
internal sealed class SkinCanvas : FrameworkElement
{
    private readonly VisualCollection _visuals;
    private readonly List<ElementRenderer> _renderers = [];
    private readonly Dictionary<Measure, List<string>> _namesByMeasure = [];
    private readonly Dictionary<string, List<ElementRenderer>> _renderersByName = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, Measure> _measures = new Dictionary<string, Measure>();
    private readonly DrawingVisual _errorVisual = new();
    private SkinDefinition? _skin;
    private ThemeService? _themes;
    private Theme? _theme;

    public SkinCanvas()
    {
        _visuals = new VisualCollection(this);
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    public SkinDefinition? Skin => _skin;

    /// <summary>Mensagem de erro exibida numa faixa (skin inválido ao recarregar).</summary>
    public string? Error { get; private set; }

    public void Load(SkinDefinition skin, IReadOnlyDictionary<string, Measure> measures, ThemeService themes)
    {
        _skin = skin;
        _themes = themes;
        _measures = measures;
        _theme = skin.ThemeName is { } themeName && themes.Store.Contains(themeName) ? themes.Store.Get(themeName) : themes.Current;

        _visuals.Clear();
        _renderers.Clear();
        _renderersByName.Clear();
        _namesByMeasure.Clear();

        foreach (var (name, measure) in measures)
        {
            if (!_namesByMeasure.TryGetValue(measure, out var names)) _namesByMeasure[measure] = names = [];
            names.Add(name);
        }

        foreach (var def in skin.Elements)
        {
            var r = ElementRenderer.Create(def);
            _renderers.Add(r);
            _visuals.Add(r.Visual);
            foreach (var dep in def.Dependencies)
            {
                if (!_renderersByName.TryGetValue(dep, out var list)) _renderersByName[dep] = list = [];
                list.Add(r);
                if (r is GraphRenderer && measures.TryGetValue(dep, out var m)) m.NotifyEveryRead = true;
            }
        }
        _visuals.Add(_errorVisual);
        InvalidateMeasure();
        RenderAll();
    }

    /// <summary>Tema ou fonte mudou: tudo precisa ser redesenhado.</summary>
    public void OnThemeChanged()
    {
        if (_skin is null || _themes is null) return;
        if (Parent is WidgetWindow w) FontOverride = w.Instance.Font;
        _theme = _skin.ThemeName is { } name && _themes.Store.Contains(name) ? _themes.Store.Get(name) : _themes.Current;
        RenderAll();
    }

    /// <summary>Medidas lidas pelo agendador: redesenha só os elementos afetados.</summary>
    public void OnMeasuresChanged(IReadOnlyList<Measure> changed)
    {
        if (_skin is null) return;
        HashSet<ElementRenderer>? dirty = null;
        foreach (var m in changed)
        {
            if (!_namesByMeasure.TryGetValue(m, out var names)) continue;
            foreach (var name in names)
            {
                if (!_renderersByName.TryGetValue(name, out var list)) continue;
                foreach (var r in list)
                {
                    r.OnMeasureRead(name, m);
                    (dirty ??= []).Add(r);
                }
            }
        }
        if (dirty is null) return;
        var ctx = Context();
        foreach (var r in dirty) r.Render(ctx);
    }

    public void SetError(string? error)
    {
        Error = error;
        using var dc = _errorVisual.RenderOpen();
        if (error is null || _skin is null) return;
        var ft = new FormattedText("⚠ " + error, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(40, _skin.Width - 12), MaxLineCount = 3, Trimming = TextTrimming.CharacterEllipsis };
        var rect = new Rect(0, Math.Max(0, _skin.Height - ft.Height - 8), _skin.Width, ft.Height + 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(230, 180, 40, 40)), null, rect, 6, 6);
        dc.DrawText(ft, new Point(6, rect.Y + 4));
    }

    /// <summary>Ação ("acao") do elemento mais acima sob o ponto, se houver.</summary>
    public string? ActionAt(Point p)
    {
        for (int i = _renderers.Count - 1; i >= 0; i--)
            if (_renderers[i].Def.Action is { } action && _renderers[i].Bounds.Contains(p))
                return action;
        return null;
    }

    private void RenderAll()
    {
        if (_skin is null) return;
        var ctx = Context();
        foreach (var r in _renderers) r.Render(ctx);
        SetError(Error);
    }

    /// <summary>Fonte escolhida para este widget nas configurações.</summary>
    public string? FontOverride { get; set; }

    private RenderContext Context() =>
        new(_themes!, _theme!, _skin!, name => _measures.TryGetValue(name, out var m) ? m : null, VisualTreeHelper.GetDpi(this).PixelsPerDip, FontOverride);

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size availableSize) => _skin is null ? new Size(1, 1) : new Size(_skin.Width, _skin.Height);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        RenderAll(); // texto depende de PixelsPerDip
    }

    /// <summary>Área inteira clicável (senão, pixels transparentes deixam o clique passar).</summary>
    protected override HitTestResult HitTestCore(PointHitTestParameters p) => new PointHitTestResult(this, p.HitPoint);
}

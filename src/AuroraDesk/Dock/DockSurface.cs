using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AuroraDesk.Core.Dock;
using AuroraDesk.Core.Theming;
using AuroraDesk.Theming;

namespace AuroraDesk.Dock;

internal enum DockEdge { Bottom, Top, Left, Right }

/// <summary>
/// Superfície do dock, desenhada à mão em OnRender (sem controles). As animações (ampliação,
/// ocultar) são curtas e param quando terminam: nada roda com o dock parado.
/// </summary>
internal sealed class DockSurface : FrameworkElement
{
    public const double Padding = 8;
    private const double IndicatorBand = 5;
    private const double LabelSpace = 34;

    private readonly ThemeService _themes;
    private readonly DispatcherTimer _animation;
    private IReadOnlyList<DockEntry> _entries = [];
    private bool[] _separators = [];
    private double? _mouseMain;
    private double _zoomBlend, _zoomTarget;
    private double _hide, _hideTarget;
    private int _hover = -1;
    private Rect _hitArea;

    // Arrastar para reordenar
    private int _pressIndex = -1;
    private Point _pressPoint;
    private bool _dragging;
    private int _insertIndex = -1;
    private int _dropGroup = -1; // grupo sob o ponteiro ao arrastar (soltar = entrar no grupo)

    public DockSurface(ThemeService themes)
    {
        _themes = themes;
        _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Animate(), Dispatcher) { IsEnabled = false };
        AllowDrop = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    public double IconSize { get; set; } = 44;
    public double Spacing { get; set; } = 6;
    public double Zoom { get; set; } = 1.4;
    public bool ShowLabels { get; set; } = true;
    /// <summary>Distância entre a borda da tela e o painel.</summary>
    public double EdgeGap { get; set; } = 8;
    /// <summary>Fundo desenhado pelo DWM (Mica/Acrílico): não pinta o painel.</summary>
    public bool SystemBackdrop { get; set; }
    /// <summary>Skin de imagens (null = desenho do tema).</summary>
    public DockSkinRenderer? Skin { get; set; }

    /// <summary>Quanto as pontas da skin (ex.: grifos) passam de cada lado do painel.</summary>
    public double CapsExtent => Horizontal && Skin is not null ? Skin.CapsExtent(PanelThickness) : 0;

    public bool Horizontal => Edge is DockEdge.Bottom or DockEdge.Top;
    public double PanelThickness => IconSize + 2 * Padding + IndicatorBand;
    public bool IsHidden => _hideTarget >= 1;

    /// <summary>Espessura total necessária para a janela (painel + ampliação + nome).</summary>
    public double RequiredThickness => Math.Max(EdgeGap + PanelThickness + (Zoom > 1 ? IconSize * (Zoom - 1) : 0) + (ShowLabels ? LabelSpace : 4),
        EdgeGap + (Horizontal && Skin is not null ? Skin.CapsHeight(PanelThickness) : 0) + 4);

    /// <summary>Comprimento máximo do painel (com a ampliação no meio).</summary>
    public double RequiredLength()
    {
        DockLayout.Compute(_separators, IconSize, Spacing, Padding, null, 1, out double baseLength);
        if (Zoom <= 1 || _separators.Length == 0) return baseLength;
        DockLayout.Compute(_separators, IconSize, Spacing, Padding, baseLength / 2, Zoom, out double zoomed);
        // Folga dos dois lados: com o mouse na ponta, o painel cresce todo para um lado só.
        return baseLength + 2 * Math.Max(0, zoomed - baseLength);
    }

    public event Action<int, MouseButton>? ItemClicked;
    public event Action<int>? ContextRequested;
    public event Action<int, int>? ItemMoved;
    public event Action<int>? ItemDraggedOut;
    public event Action<string[], int>? FilesDropped;
    /// <summary>Arquivos soltos em cima de um grupo (índice do grupo).</summary>
    public event Action<string[], int>? FilesDroppedOnGroup;
    /// <summary>Atalho do dock arrastado para dentro de um grupo (origem, grupo).</summary>
    public event Action<int, int>? ItemDroppedOnGroup;

    /// <summary>Grupo cujo centro está sob o ponto (no eixo do dock), ou -1.</summary>
    private int GroupAt(double main, DockSlot[] slots, double start)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (!_entries[i].IsGroup) continue;
            var s = slots[i];
            double m = main - start;
            if (m > s.Start + s.Size * 0.2 && m < s.End - s.Size * 0.2) return i;
        }
        return -1;
    }
    public event Action? MouseEntered;
    public event Action? MouseLeft;

    public void SetEntries(IReadOnlyList<DockEntry> entries)
    {
        _entries = entries;
        _separators = entries.Select(e => e.IsSeparator).ToArray();
        if (_hover >= entries.Count) _hover = -1;
        InvalidateVisual();
    }

    public void SetHidden(bool hidden, bool animate = true)
    {
        _hideTarget = hidden ? 1 : 0;
        if (!animate) _hide = _hideTarget;
        StartAnimation();
    }

    // ---------- geometria: (eixo principal, distância da borda da tela) -> pontos da janela ----------

    private double MainLength => Horizontal ? ActualWidth : ActualHeight;

    private Point Pt(double main, double fromEdge) => Edge switch
    {
        DockEdge.Bottom => new Point(main, ActualHeight - fromEdge),
        DockEdge.Top => new Point(main, fromEdge),
        DockEdge.Left => new Point(fromEdge, main),
        _ => new Point(ActualWidth - fromEdge, main),
    };

    private Rect R(double mainStart, double mainSize, double edge0, double edge1) =>
        new(Pt(mainStart, edge0), Pt(mainStart + mainSize, edge1));

    private double MainOf(Point p) => Horizontal ? p.X : p.Y;
    private double EdgeOf(Point p) => Edge switch
    {
        DockEdge.Bottom => ActualHeight - p.Y,
        DockEdge.Top => p.Y,
        DockEdge.Left => p.X,
        _ => ActualWidth - p.X,
    };

    private (DockSlot[] Slots, double Start, double Length) Layout()
    {
        DockLayout.Compute(_separators, IconSize, Spacing, Padding, null, 1, out double baseLength);
        double baseStart = (MainLength - baseLength) / 2;
        double? mouse = _mouseMain is { } m && !_dragging ? m - baseStart : null;
        double zoom = 1 + (Zoom - 1) * _zoomBlend;
        var slots = DockLayout.Compute(_separators, IconSize, Spacing, Padding, mouse, zoom, out double length, out double offset);
        return (slots, baseStart - offset, length);
    }

    private double HideShift => (EdgeGap + PanelThickness + 2) * _hide;

    protected override void OnRender(DrawingContext dc)
    {
        var theme = _themes.Current;
        double shift = HideShift;

        if (_hide > 0.99)
        {
            // Oculto: só uma faixa quase invisível na borda, para o mouse fazê-lo voltar.
            var (_, s0, l0) = Layout();
            var strip = R(s0, l0, 0, 2);
            dc.DrawRectangle(_themes.Brush(new ArgbColor(1, 0, 0, 0)), null, strip);
            _hitArea = strip;
            return;
        }

        var (slots, start, length) = Layout();
        double e0 = EdgeGap - shift, e1 = EdgeGap + PanelThickness - shift;
        var panel = R(start, length, e0, e1);
        double radius = Math.Min(theme.CornerRadius + 4, PanelThickness / 2);
        if (!SystemBackdrop && Skin?.HasPanel('c') == true) Skin.DrawPanel(dc, 'c', panel);
        else if (!SystemBackdrop)
            dc.DrawRoundedRectangle(_themes.Brush(theme.Panel), _themes.Pen(theme.Border.WithOpacity(0.6), 1), panel, radius, radius);

        Rect hit = panel;
        double iconEdge = EdgeGap + IndicatorBand + Padding - shift;
        var accent = _themes.Brush(theme.Accent);
        var dot = _themes.Brush(theme.TextSecondary);

        // Barra dividida (skin): uma célula por ícone, emendadas, e o divisor no lugar dos
        // separadores. Antes dos ícones, para a moldura de um ícone não ficar sob a célula vizinha.
        if (Skin?.HasCells == true && Horizontal)
        {
            double basePitch = IconSize + Spacing;
            for (int i = 0; i < _entries.Count; i++)
            {
                var slot = slots[i];
                var c = Pt(start + slot.Center, iconEdge + (_entries[i].IsSeparator ? IconSize : slot.Size) / 2);
                if (_entries[i].IsSeparator) Skin.DrawDivider(dc, c, basePitch);
                else Skin.DrawCell(dc, c, slot.Size + Spacing);
            }
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            var slot = slots[i];
            if (e.IsSeparator && Skin?.HasCells == true && Skin.Skin.Divider is not null && Horizontal) continue;
            if (e.IsSeparator)
            {
                var a = Pt(start + slot.Center, iconEdge + IconSize * 0.2);
                var b = Pt(start + slot.Center, iconEdge + IconSize * 0.8);
                dc.DrawLine(_themes.Pen(theme.Border, 1), a, b);
                continue;
            }

            var iconRect = R(start + slot.Start, slot.Size, iconEdge, iconEdge + slot.Size);
            hit.Union(iconRect);
            if (_dragging && i == _pressIndex) dc.PushOpacity(0.35);
            int index = i;
            void DrawIcon(Rect r)
            {
                if (e.IsGroup) DrawGroup(dc, theme, e.GroupIcons ?? [], r, index == _dropGroup);
                else if (e.Glyph is { } glyph) DrawGlyphButton(dc, theme, glyph, r, index == _hover);
                else if (e.Icon is { } icon) dc.DrawImage(icon, r);
                else dc.DrawRoundedRectangle(_themes.Brush(theme.Surface), null, r, 8, 8);
            }
            // Com skin, cada ícone vai num encaixe (fundo, ícone menor e moldura por cima), como uma barra de ações.
            if (Skin?.HasSlot == true) Skin.DrawSlot(dc, iconRect, DrawIcon);
            else DrawIcon(iconRect);
            if (_dragging && i == _pressIndex) dc.Pop();

            if (e.IsRunning)
            {
                var c = Pt(start + slot.Center, EdgeGap + IndicatorBand / 2 + 1 - shift);
                if (e.IsActive)
                    dc.DrawRoundedRectangle(accent, null, Horizontal ? new Rect(c.X - 7, c.Y - 1.5, 14, 3) : new Rect(c.X - 1.5, c.Y - 7, 3, 14), 1.5, 1.5);
                else
                    dc.DrawEllipse(dot, null, c, 2.2, 2.2);
            }
        }

        // Pontas (ex.: grifos) na frente dos ícones, como na barra de um jogo; só com o dock embaixo.
        if (!SystemBackdrop && Edge == DockEdge.Bottom) Skin?.DrawCaps(dc, panel);

        if (_dragging && _insertIndex >= 0 && _entries.Count > 0)
        {
            double at = _insertIndex < slots.Length ? slots[_insertIndex].Start - Spacing / 2 : slots[^1].End + Spacing / 2;
            dc.DrawLine(_themes.Pen(theme.Accent, 2), Pt(start + at, iconEdge - 2), Pt(start + at, iconEdge + IconSize + 2));
        }

        if (ShowLabels && !_dragging && _hover >= 0 && _hover < _entries.Count && _zoomBlend > 0.5)
            DrawLabel(dc, theme, _entries[_hover].Name, start + slots[_hover].Center, iconEdge + slots[_hover].Size + 8);

        _hitArea = hit;
    }

    private static readonly FontFamily GlyphFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Botão do próprio dock (ex.: pesquisa): quadrado arredondado discreto com o ícone no centro.</summary>
    private void DrawGlyphButton(DrawingContext dc, Theme theme, string glyph, Rect r, bool hover)
    {
        double inset = r.Width * 0.08;
        var box = new Rect(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset);
        double radius = Math.Min(theme.CornerRadius, box.Width / 3);
        var fill = hover ? theme.Accent.WithOpacity(0.25) : theme.Surface;
        dc.DrawRoundedRectangle(_themes.Brush(fill), _themes.Pen(theme.Border.WithOpacity(0.8), 1), box, radius, radius);
        var ft = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(GlyphFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), box.Width * 0.6,
            _themes.Brush(theme.Text), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(box.X + (box.Width - ft.Width) / 2, box.Y + (box.Height - ft.Height) / 2));
    }

    /// <summary>Grupo de atalhos: quadrado arredondado com até 4 miniaturas (2×2), como as pastas do celular.</summary>
    private void DrawGroup(DrawingContext dc, Theme theme, IReadOnlyList<System.Windows.Media.Imaging.BitmapSource?> icons, Rect r, bool dropTarget)
    {
        double inset = r.Width * 0.06;
        var box = new Rect(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset);
        double radius = Math.Min(theme.CornerRadius, box.Width / 3);
        var border = dropTarget ? _themes.Pen(theme.Accent, 2) : _themes.Pen(theme.Border.WithOpacity(0.8), 1);
        dc.DrawRoundedRectangle(_themes.Brush(dropTarget ? theme.Accent.WithOpacity(0.25) : theme.Surface), border, box, radius, radius);
        if (icons.Count == 0)
        {
            var ft = new FormattedText("", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(GlyphFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), box.Width * 0.5,
                _themes.Brush(theme.TextSecondary), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(box.X + (box.Width - ft.Width) / 2, box.Y + (box.Height - ft.Height) / 2));
            return;
        }
        double pad = box.Width * 0.14, gap = box.Width * 0.06;
        double cell = (box.Width - 2 * pad - gap) / 2;
        for (int k = 0; k < Math.Min(4, icons.Count); k++)
        {
            var cr = new Rect(box.X + pad + (k % 2) * (cell + gap), box.Y + pad + (k / 2) * (cell + gap), cell, cell);
            if (icons[k] is { } img) dc.DrawImage(img, cr);
            else dc.DrawRoundedRectangle(_themes.Brush(theme.Border), null, cr, 3, 3);
        }
    }

    /// <summary>Retângulo do ícone <paramref name="index"/> em pixels da tela (para abrir o painel do grupo em cima dele).</summary>
    public Rect? ItemScreenRect(int index)
    {
        if (index < 0 || index >= _entries.Count || PresentationSource.FromVisual(this) is null) return null;
        var (slots, start, _) = Layout();
        double iconEdge = EdgeGap + IndicatorBand + Padding;
        var r = R(start + slots[index].Start, slots[index].Size, iconEdge, iconEdge + slots[index].Size);
        return new Rect(PointToScreen(r.TopLeft), PointToScreen(r.BottomRight));
    }

    private void DrawLabel(DrawingContext dc, Theme theme, string text, double main, double fromEdge)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(_themes.DefaultFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12,
            _themes.Brush(theme.Text), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = 260, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        double w = ft.Width + 16, h = ft.Height + 8;
        var anchor = Pt(main, fromEdge);
        Rect box = Edge switch
        {
            DockEdge.Bottom => new Rect(anchor.X - w / 2, anchor.Y - h, w, h),
            DockEdge.Top => new Rect(anchor.X - w / 2, anchor.Y, w, h),
            DockEdge.Left => new Rect(anchor.X, anchor.Y - h / 2, w, h),
            _ => new Rect(anchor.X - w, anchor.Y - h / 2, w, h),
        };
        // Mantém o rótulo dentro da janela.
        box.X = Math.Clamp(box.X, 0, Math.Max(0, ActualWidth - w));
        box.Y = Math.Clamp(box.Y, 0, Math.Max(0, ActualHeight - h));
        dc.DrawRoundedRectangle(_themes.Brush(theme.Surface.WithOpacity(0.95)), _themes.Pen(theme.Border, 1), box, 6, 6);
        dc.DrawText(ft, new Point(box.X + 8, box.Y + 4));
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters p) =>
        _hitArea.Contains(p.HitPoint) ? new PointHitTestResult(this, p.HitPoint) : null;

    // ---------- animação curta ----------

    private void StartAnimation()
    {
        if (!_animation.IsEnabled) _animation.Start();
    }

    private void Animate()
    {
        const double Step = 0.16; // ~100 ms
        _zoomBlend = Approach(_zoomBlend, _zoomTarget, Step);
        _hide = Approach(_hide, _hideTarget, Step * 1.2);
        if (_zoomBlend == _zoomTarget && _hide == _hideTarget)
        {
            _animation.Stop();
            if (_zoomTarget == 0) _mouseMain = null;
        }
        InvalidateVisual();
    }

    private static double Approach(double v, double target, double step) =>
        v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);

    // ---------- mouse ----------

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _zoomTarget = Zoom > 1 || ShowLabels ? 1 : 0;
        StartAnimation();
        MouseEntered?.Invoke();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging) return;
        _hover = -1;
        _zoomTarget = 0;
        StartAnimation();
        MouseLeft?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        _mouseMain = MainOf(p);
        var (slots, start, _) = Layout();
        _hover = DockLayout.HitTest(slots, _separators, MainOf(p) - start, Spacing);

        if (_pressIndex >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragging && (Math.Abs(p.X - _pressPoint.X) > 6 || Math.Abs(p.Y - _pressPoint.Y) > 6) && _entries[_pressIndex].Pinned is not null)
            {
                _dragging = true;
                CaptureMouse();
            }
            if (_dragging)
            {
                // Em cima do centro de um grupo: soltar coloca o atalho dentro dele.
                int g = GroupAt(MainOf(p), slots, start);
                _dropGroup = g != _pressIndex && _entries[_pressIndex].Pinned is { IsGroup: false } ? g : -1;
                _insertIndex = _dropGroup >= 0 ? -1 : DockLayout.InsertIndex(slots, MainOf(p) - start);
            }
        }
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left) return;
        var p = e.GetPosition(this);
        var (slots, start, _) = Layout();
        _pressIndex = DockLayout.HitTest(slots, _separators, MainOf(p) - start, Spacing);
        _pressPoint = p;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        var p = e.GetPosition(this);
        var (slots, start, _) = Layout();
        int index = DockLayout.HitTest(slots, _separators, MainOf(p) - start, Spacing);

        if (e.ChangedButton == MouseButton.Left && _dragging)
        {
            int from = _pressIndex;
            bool outside = EdgeOf(p) > EdgeGap + PanelThickness + IconSize * 1.5 || EdgeOf(p) < -20;
            int to = _insertIndex, group = _dropGroup;
            EndDrag();
            if (group >= 0 && !outside) ItemDroppedOnGroup?.Invoke(from, group);
            else if (outside) ItemDraggedOut?.Invoke(from);
            else if (to >= 0 && to != from && to != from + 1) ItemMoved?.Invoke(from, to > from ? to - 1 : to);
            return;
        }
        if (e.ChangedButton == MouseButton.Left && index >= 0 && index == _pressIndex) ItemClicked?.Invoke(index, MouseButton.Left);
        else if (e.ChangedButton == MouseButton.Middle && index >= 0) ItemClicked?.Invoke(index, MouseButton.Middle);
        else if (e.ChangedButton == MouseButton.Right) { e.Handled = true; ContextRequested?.Invoke(index); }
        _pressIndex = -1;
    }

    private void EndDrag()
    {
        _dragging = false;
        _pressIndex = -1;
        _insertIndex = -1;
        _dropGroup = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    // ---------- arrastar arquivos de fora ----------

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Link;
        var (slots, start, _) = Layout();
        _dragging = true;
        _pressIndex = -1;
        _dropGroup = GroupAt(MainOf(e.GetPosition(this)), slots, start);
        _insertIndex = _dropGroup >= 0 ? -1 : DockLayout.InsertIndex(slots, MainOf(e.GetPosition(this)) - start);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        _dragging = false;
        _insertIndex = -1;
        _dropGroup = -1;
        InvalidateVisual();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        int index = _insertIndex, group = _dropGroup;
        _dragging = false;
        _insertIndex = -1;
        _dropGroup = -1;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            if (group >= 0) FilesDroppedOnGroup?.Invoke(files, group);
            else FilesDropped?.Invoke(files, index < 0 ? _entries.Count : index);
        }
        InvalidateVisual();
    }

    /// <summary>Retângulo do painel em DIPs (para calcular a reserva de espaço).</summary>
    public Size PanelSize() => Horizontal ? new Size(RequiredLength(), PanelThickness) : new Size(PanelThickness, RequiredLength());
}

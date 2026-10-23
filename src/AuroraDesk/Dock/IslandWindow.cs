using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;
using AuroraDesk.Theming;

namespace AuroraDesk.Dock;

/// <summary>
/// Ilha lateral do dock (estatísticas à esquerda, bandeja e relógio à direita). Janela própria,
/// do tamanho do conteúdo: atualizar a CPU a cada segundo redesenha só esta janela pequena,
/// não o dock inteiro com a ampliação.
/// </summary>
internal sealed class IslandWindow : Window
{
    private readonly IslandSurface _surface;
    private nint _hwnd;

    public IslandWindow(ThemeService themes, bool alignRight)
    {
        _surface = new IslandSurface(themes, alignRight);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Title = alignRight ? "Aurora Desk - bandeja" : "Aurora Desk - estatísticas";
        Width = 200;
        Height = 70;
        Content = _surface;
    }

    public IslandSurface Surface => _surface;
    public nint Handle => _hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        nint ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(_hwnd)!.AddHook((nint h, int msg, nint w, nint l, ref bool handled) =>
        {
            if (msg != Win32.WM_MOUSEACTIVATE) return 0;
            handled = true;
            return Win32.MA_NOACTIVATE;
        });
        RenderModes.Apply(this);
    }

    /// <summary>Posiciona em pixels físicos (o dock calcula pelo monitor e pela reserva de espaço).</summary>
    public void SetBounds(int x, int y, int width, int height)
    {
        if (_hwnd == 0) return;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, width, height, Win32.SWP_NOACTIVATE);
    }

    public unsafe void ApplyBackdrop(bool enabled, Core.Theming.ArgbColor tint, string kind)
    {
        if (_hwnd == 0) return;
        _surface.SystemBackdrop = enabled;
        if (!enabled) { Win32.SetAccent(_hwnd, 0, 0); return; }
        int round = 2;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, &round, sizeof(int));
        uint argb = (uint)(Math.Min(tint.A, (byte)0xB0) << 24 | tint.R << 16 | tint.G << 8 | tint.B);
        Win32.SetAccent(_hwnd, kind == "desfoque" ? 3 : 4, argb);
    }
}

/// <summary>Desenho e interação de uma ilha: itens lado a lado, destaque ao passar o mouse.</summary>
internal sealed class IslandSurface : FrameworkElement
{
    public const double Padding = 14;
    private readonly ThemeService _themes;
    private readonly bool _alignRight;
    private readonly List<(IslandItem Item, Rect Area)> _layout = [];
    private IReadOnlyList<IslandItem> _items = [];
    private int _hover = -1;
    private Rect _panel;

    public IslandSurface(ThemeService themes, bool alignRight)
    {
        _themes = themes;
        _alignRight = alignRight;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 500);
    }

    /// <summary>Altura do painel (igual à do dock) e distância da borda da tela.</summary>
    public double PanelThickness { get; set; } = 65;
    public double EdgeGap { get; set; } = 8;
    public bool Bottom { get; set; } = true;
    public bool SystemBackdrop { get; set; }
    /// <summary>Skin de imagens (null = desenho do tema).</summary>
    public DockSkinRenderer? Skin { get; set; }
    /// <summary>
    /// Solta: sem painel por trás e do tamanho dos itens (ex.: o quadro do jogador da skin, ao lado
    /// das ilhas, como no jogo).
    /// </summary>
    public bool Bare { get; set; }

    /// <summary>Distância dos itens até a borda: com skin, por dentro da moldura e com um respiro.</summary>
    private double InnerPadding => Bare ? 0 : Skin is { } s && s.HasPanel(_alignRight ? 'd' : 'e')
        ? Math.Max(Padding - 2, s.PanelInset(_alignRight ? 'd' : 'e', PanelThickness) + 9)
        : Padding;

    /// <summary>Espaço entre itens.</summary>
    public double Gap => _alignRight ? 12 : 18;

    public event Action? RightClicked;

    public void SetItems(IReadOnlyList<IslandItem> items)
    {
        _items = items;
        _hover = -1;
        InvalidateVisual();
    }

    private IslandContext Context() => new(_themes, VisualTreeHelper.GetDpi(this).PixelsPerDip, Skin, PanelThickness - 12);

    /// <summary>Largura do painel em DIPs com o conteúdo atual.</summary>
    public double RequiredWidth()
    {
        var ctx = Context();
        var visible = _items.Where(i => i.Visible).ToList();
        if (visible.Count == 0) return 0;
        return Math.Ceiling(2 * InnerPadding + visible.Sum(i => i.Width(ctx)) + Gap * (visible.Count - 1));
    }

    /// <summary>Altura da janela: o painel, ou mais se algum item for maior (ex.: o quadro do jogador da skin).</summary>
    public double RequiredHeight
    {
        get
        {
            var ctx = Context();
            double tallest = _items.Where(i => i.Visible).Select(i => i.Height(ctx)).DefaultIfEmpty(0).Max();
            return Math.Max(PanelThickness, tallest + (Bare ? 0 : 6)) + EdgeGap;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        _layout.Clear();
        var ctx = Context();
        var theme = ctx.Theme;
        double width = RequiredWidth();
        if (width <= 0) { _panel = Rect.Empty; return; }

        double x0 = _alignRight ? ActualWidth - width : 0;
        if (Bare)
        {
            // Sem painel: cada item ocupa a altura toda, rente ao pé (ou ao topo) da janela.
            double tall = Math.Max(PanelThickness, _items.Where(i => i.Visible).Select(i => i.Height(ctx)).DefaultIfEmpty(0).Max());
            _panel = new Rect(x0, Bottom ? ActualHeight - EdgeGap - tall : EdgeGap, width, tall);
            double bx = x0;
            foreach (var item in _items.Where(i => i.Visible))
            {
                double w = item.Width(ctx);
                var area = new Rect(bx, _panel.Y, w, _panel.Height);
                _layout.Add((item, area));
                try { item.Draw(dc, ctx, area); }
                catch (Exception ex) { Core.Logging.Log.Error($"Item '{item.Key}' da ilha falhou", ex); }
                bx += w + Gap;
            }
            return;
        }
        double y0 = Bottom ? ActualHeight - EdgeGap - PanelThickness : EdgeGap;
        if (SystemBackdrop) { x0 = 0; y0 = 0; width = ActualWidth; }
        _panel = new Rect(x0, y0, width, SystemBackdrop ? ActualHeight : PanelThickness);
        double radius = Math.Min(theme.CornerRadius + 4, PanelThickness / 2);
        char island = _alignRight ? 'd' : 'e';
        if (!SystemBackdrop && Skin?.HasPanel(island) == true) Skin.DrawPanel(dc, island, _panel);
        else if (!SystemBackdrop)
            dc.DrawRoundedRectangle(_themes.Brush(theme.Panel), _themes.Pen(theme.Border.WithOpacity(0.6), 1), _panel, radius, radius);

        double x = x0 + InnerPadding;
        foreach (var item in _items.Where(i => i.Visible))
        {
            double w = item.Width(ctx);
            var area = new Rect(x, _panel.Y + 6, w, _panel.Height - 12);
            int index = _layout.Count;
            _layout.Add((item, area));
            if (index == _hover && item.Clickable)
            {
                var hover = new Rect(area.X - 6, area.Y - 2, area.Width + 12, area.Height + 4);
                dc.DrawRoundedRectangle(_themes.Brush(theme.Surface.WithOpacity(0.9)), null, hover, 8, 8);
            }
            try { item.Draw(dc, ctx, area); }
            catch (Exception ex) { Core.Logging.Log.Error($"Item '{item.Key}' da ilha falhou", ex); }
            x += w + Gap;
        }
    }

    private int HitIndex(Point p)
    {
        for (int i = 0; i < _layout.Count; i++)
        {
            var a = _layout[i].Area;
            if (p.X >= a.X - Gap / 2 && p.X < a.Right + Gap / 2 && _panel.Contains(p)) return i;
        }
        return -1;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters p) =>
        _panel.Contains(p.HitPoint) ? new PointHitTestResult(this, p.HitPoint) : null;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = HitIndex(e.GetPosition(this));
        if (i == _hover) return;
        _hover = i;
        ToolTip = i >= 0 ? _layout[i].Item.Tooltip : null;
        Cursor = i >= 0 && _layout[i].Item.Clickable ? Cursors.Hand : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        int i = HitIndex(e.GetPosition(this));
        if (e.ChangedButton == MouseButton.Right) { e.Handled = true; RightClicked?.Invoke(); return; }
        if (e.ChangedButton != MouseButton.Left || i < 0) return;
        try { _layout[i].Item.Click(); }
        catch (Exception ex) { Core.Logging.Log.Error($"Clique em '{_layout[i].Item.Key}' falhou", ex); }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        int i = HitIndex(e.GetPosition(this));
        if (i < 0) return;
        _layout[i].Item.Wheel(e.Delta);
        e.Handled = true;
    }
}

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AuroraDesk.Core.Logging;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;
using AuroraDesk.Theming;

namespace AuroraDesk.Dock;

/// <summary>
/// Janela do dock: sem borda, sempre no topo, sem roubar o foco. Flutuante na borda da área de
/// trabalho, ou reservando espaço (AppBar, como a barra de tarefas). Com Mica/Acrílico, a janela
/// tem exatamente o tamanho do painel e o fundo é desenhado pelo DWM.
/// </summary>
internal sealed unsafe class DockWindow : Window
{
    private static readonly int AppBarMessage = Win32.RegisterWindowMessage("AuroraDesk.AppBar");

    private readonly string _backdrop;
    private nint _hwnd;
    private bool _appBarRegistered;
    private uint _appBarEdge;

    public DockWindow(ThemeService themes, string backdrop)
    {
        _backdrop = backdrop;
        Surface = new DockSurface(themes) { SystemBackdrop = UsesBackdrop };
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Title = "Aurora Desk - dock";
        Width = 400;
        Height = 100;
        Content = Surface;
    }

    public DockSurface Surface { get; }
    public nint Handle => _hwnd;
    public bool UsesBackdrop => _backdrop is "acrilico" or "desfoque";

    /// <summary>Aplicativo em tela cheia avisado pelo sistema de AppBar.</summary>
    public event Action<bool>? FullscreenChanged;
    public event Action? AppBarPositionChanged;

    /// <summary>Permite ocultar a janela explicitamente pelo app (ex.: em tela cheia).</summary>
    public bool AllowHide { get; set; }

    /// <summary>Restaura e mantém a janela no topo (visível mesmo após Win+D).</summary>
    public void BringToTop()
    {
        if (_hwnd == 0) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            BringToTop();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        nint ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        RenderModes.Apply(this);
    }

    /// <summary>
    /// Acrílico/desfoque atrás do painel. A janela tem o tamanho exato do painel (sem área de
    /// ampliação) e cantos arredondados do Windows 11; a cor do tema vira a tinta da camada.
    /// </summary>
    public void ApplyBackdrop(Core.Theming.ArgbColor tint)
    {
        if (_hwnd == 0 || !UsesBackdrop) return;
        int round = 2; // DWMWCP_ROUND
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, &round, sizeof(int));
        uint argb = (uint)(Math.Min(tint.A, (byte)0xB0) << 24 | tint.R << 16 | tint.G << 8 | tint.B);
        if (!Win32.SetAccent(_hwnd, _backdrop == "desfoque" ? 3 : 4, argb))
            Log.Warn("Acrílico/desfoque indisponível neste Windows");
    }

    /// <summary>
    /// Posiciona o dock na borda do monitor (tamanhos em DIPs vindos da superfície). Devolve a
    /// área usada (área de trabalho ou faixa reservada) e a escala, para as ilhas laterais.
    /// </summary>
    public (Win32.RECT Area, double Scale) Place(nint monitor, DockEdge edge, bool reserve)
    {
        if (_hwnd == 0) return (default, 1);
        Surface.Edge = edge;
        var info = Win32.MonitorInfo(monitor);
        double scale = Win32.MonitorScale(monitor);

        double lengthDip = Surface.RequiredLength() + (UsesBackdrop ? 0 : 48 + 2 * Surface.CapsExtent);
        double thicknessDip = UsesBackdrop ? Surface.PanelThickness : Surface.RequiredThickness;
        int length = (int)Math.Ceiling(lengthDip * scale);
        int thickness = (int)Math.Ceiling(thicknessDip * scale);
        int gap = (int)Math.Round(Surface.EdgeGap * scale);

        Win32.RECT area = info.rcWork;
        if (reserve) area = ReserveSpace(info.rcMonitor, edge, (int)Math.Ceiling((Surface.PanelThickness + Surface.EdgeGap * 2) * scale));
        else RemoveAppBar();

        bool horizontal = edge is DockEdge.Bottom or DockEdge.Top;
        int mainStart = horizontal ? area.Left + (area.Width - length) / 2 : area.Top + (area.Height - length) / 2;
        int backdropGap = UsesBackdrop ? gap : 0;
        int x, y, w, h;
        switch (edge)
        {
            case DockEdge.Top: (x, y, w, h) = (mainStart, area.Top + backdropGap, length, thickness); break;
            case DockEdge.Left: (x, y, w, h) = (area.Left + backdropGap, mainStart, thickness, length); break;
            case DockEdge.Right: (x, y, w, h) = (area.Right - thickness - backdropGap, mainStart, thickness, length); break;
            default: (x, y, w, h) = (mainStart, area.Bottom - thickness - backdropGap, length, thickness); break;
        }
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
        return (area, scale);
    }

    // ---------- AppBar ----------

    private Win32.RECT ReserveSpace(Win32.RECT monitor, DockEdge edge, int thickness)
    {
        var data = new Win32.APPBARDATA { cbSize = sizeof(Win32.APPBARDATA), hWnd = _hwnd, uCallbackMessage = AppBarMessage };
        if (!_appBarRegistered)
        {
            _appBarRegistered = Win32.SHAppBarMessage(Win32.ABM_NEW, ref data) != 0;
            if (!_appBarRegistered) { Log.Warn("AppBar não registrada; usando dock flutuante"); return monitor; }
            Log.Info("Dock reservando espaço na tela (AppBar)");
        }
        _appBarEdge = edge switch { DockEdge.Top => Win32.ABE_TOP, DockEdge.Left => Win32.ABE_LEFT, DockEdge.Right => Win32.ABE_RIGHT, _ => Win32.ABE_BOTTOM };
        data.uEdge = _appBarEdge;
        data.rc = monitor;
        // Pede a borda inteira; o sistema desconta a barra de tarefas e outras AppBars.
        Win32.SHAppBarMessage(Win32.ABM_QUERYPOS, ref data);
        switch (_appBarEdge)
        {
            case Win32.ABE_TOP: data.rc.Bottom = data.rc.Top + thickness; break;
            case Win32.ABE_LEFT: data.rc.Right = data.rc.Left + thickness; break;
            case Win32.ABE_RIGHT: data.rc.Left = data.rc.Right - thickness; break;
            default: data.rc.Top = data.rc.Bottom - thickness; break;
        }
        Win32.SHAppBarMessage(Win32.ABM_SETPOS, ref data);
        return data.rc;
    }

    /// <summary>O Explorer reiniciou e esqueceu as AppBars: a próxima reserva registra de novo.</summary>
    public void OnShellRestarted() => _appBarRegistered = false;

    /// <summary>Devolve o espaço reservado (sempre chamado ao sair ou ao voltar para flutuante).</summary>
    public void RemoveAppBar()
    {
        if (!_appBarRegistered || _hwnd == 0) return;
        var data = new Win32.APPBARDATA { cbSize = sizeof(Win32.APPBARDATA), hWnd = _hwnd };
        Win32.SHAppBarMessage(Win32.ABM_REMOVE, ref data);
        _appBarRegistered = false;
        Log.Info("Espaço reservado do dock devolvido");
    }

    private unsafe nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WM_MOUSEACTIVATE)
        {
            handled = true;
            return Win32.MA_NOACTIVATE;
        }
        if (msg == Win32.WM_SYSCOMMAND && ((int)wParam & 0xFFF0) == Win32.SC_MINIMIZE)
        {
            handled = true;
            return 0;
        }
        if (msg == Win32.WM_WINDOWPOSCHANGING && lParam != 0)
        {
            var pos = (Win32.WINDOWPOS*)lParam;
            if ((pos->flags & Win32.SWP_NOZORDER) == 0 && pos->hwndInsertAfter == Win32.HWND_BOTTOM)
                pos->hwndInsertAfter = Win32.HWND_TOPMOST;
            if (!AllowHide && (pos->flags & Win32.SWP_HIDEWINDOW) != 0)
            {
                pos->flags &= ~Win32.SWP_HIDEWINDOW;
                pos->flags |= Win32.SWP_SHOWWINDOW;
            }
        }
        if (msg == AppBarMessage)
        {
            if ((int)wParam == Win32.ABN_POSCHANGED) AppBarPositionChanged?.Invoke();
            else if ((int)wParam == Win32.ABN_FULLSCREENAPP) FullscreenChanged?.Invoke(lParam != 0);
            handled = true;
        }
        return 0;
    }

    protected override void OnClosed(EventArgs e)
    {
        RemoveAppBar();
        base.OnClosed(e);
    }
}

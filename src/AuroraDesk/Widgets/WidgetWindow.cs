using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Logging;
using AuroraDesk.Interop;

namespace AuroraDesk.Widgets;

/// <summary>
/// Janela de um widget: sem borda, transparente, fora do Alt+Tab e da barra de tarefas, e que
/// não rouba o foco ao ser clicada. Camadas: "desktop" (logo acima da área de trabalho, abaixo
/// de todas as janelas), "normal" ou "topo" (sempre visível).
/// </summary>
internal sealed class WidgetWindow : Window
{
    private readonly WidgetManager _manager;
    private nint _hwnd;
    private bool _placingZOrder;

    public WidgetWindow(WidgetManager manager, WidgetInstance instance)
    {
        _manager = manager;
        Instance = instance;
        Canvas = new SkinCanvas { FontOverride = instance.Font };

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Aurora Desk - widget";
        Content = Canvas;
        ApplyScaleAndOpacity();
    }

    public WidgetInstance Instance { get; }
    public SkinCanvas Canvas { get; }
    public nint Handle => _hwnd;

    public void ApplyScaleAndOpacity()
    {
        double s = Math.Clamp(Instance.Scale, 0.25, 4);
        var t = new ScaleTransform(s, s);
        t.Freeze();
        Canvas.LayoutTransform = t;
        Opacity = Math.Clamp(Instance.Opacity, 0.1, 1);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Infrastructure.RenderModes.Apply(this);
        ApplyStyles();
        RestorePosition();
        ApplyLayer();
    }

    /// <summary>WS_EX_TOOLWINDOW (fora do Alt+Tab), NOACTIVATE (não rouba foco), TRANSPARENT (clique através).</summary>
    public void ApplyStyles()
    {
        if (_hwnd == 0) return;
        nint ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        ex |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        ex &= ~(nint)Win32.WS_EX_APPWINDOW;
        ex = Instance.ClickThrough ? ex | Win32.WS_EX_TRANSPARENT : ex & ~(nint)Win32.WS_EX_TRANSPARENT;
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, ex);
    }

    public void ApplyLayer()
    {
        if (_hwnd == 0) return;
        switch (Instance.Layer)
        {
            case "topo":
                Topmost = true;
                break;
            case "normal":
                Topmost = false;
                Win32.SetWindowPos(_hwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                break;
            default:
                Topmost = false;
                Win32.SetWindowPos(_hwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                SendToDesktopLayer();
                break;
        }
    }

    private bool IsDesktopLayer => Instance.Layer is not ("topo" or "normal");

    /// <summary>
    /// Coloca a janela logo acima da janela da área de trabalho (Progman): fica sobre o papel de
    /// parede e os ícones, mas abaixo de qualquer outra janela. Não depende do WorkerW, então
    /// continua funcionando quando o Explorer reinicia ou muda entre builds do Windows.
    /// </summary>
    public void SendToDesktopLayer()
    {
        if (_hwnd == 0 || !IsDesktopLayer) return;
        _placingZOrder = true;
        try
        {
            Win32.SetWindowPos(_hwnd, DesktopInsertAfter(), 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
        }
        finally
        {
            _placingZOrder = false;
        }
    }

    /// <summary>
    /// Janela da área de trabalho que serve de âncora. Normalmente o Progman; quando o
    /// "Mostrar área de trabalho" (Win+D) traz um WorkerW para a frente, o gerenciador troca
    /// a âncora por ele, e os widgets continuam visíveis logo acima.
    /// </summary>
    public static nint DesktopAnchor { get; set; }

    private nint DesktopInsertAfter()
    {
        nint anchor = DesktopAnchor != 0 && Win32.IsWindow(DesktopAnchor) ? DesktopAnchor : Win32.FindWindow("Progman", null);
        if (anchor == 0) return Win32.HWND_BOTTOM;
        // Inserir "depois" (abaixo) de quem está acima da âncora = ficar logo acima da âncora.
        nint above = Win32.GetWindow(anchor, Win32.GW_HWNDPREV);
        if (above == _hwnd) above = Win32.GetWindow(_hwnd, Win32.GW_HWNDPREV); // já estamos no lugar
        return above == 0 ? Win32.HWND_TOP : above;
    }

    private unsafe nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEACTIVATE:
                handled = true;
                return Win32.MA_NOACTIVATE;
            case Win32.WM_WINDOWPOSCHANGING when IsDesktopLayer && !_placingZOrder:
                var pos = (Win32.WINDOWPOS*)lParam;
                if ((pos->flags & Win32.SWP_NOZORDER) == 0)
                    pos->hwndInsertAfter = DesktopInsertAfter(); // ninguém nos tira da camada da área de trabalho
                break;
        }
        return 0;
    }

    // ---------- posição (pixels físicos, para funcionar entre monitores com DPI diferentes) ----------

    private void RestorePosition()
    {
        if (double.IsNaN(Instance.X) || double.IsNaN(Instance.Y)) return; // o gerenciador posiciona
        var rect = new Win32.RECT { Left = (int)Instance.X, Top = (int)Instance.Y, Right = (int)Instance.X + 50, Bottom = (int)Instance.Y + 50 };
        if (Win32.MonitorFromRect(rect, Win32.MONITOR_DEFAULTTONULL) == 0)
        {
            Log.Info($"Widget '{Instance.Skin}' estava fora das telas; reposicionando");
            Instance.X = Instance.Y = double.NaN;
            return;
        }
        Win32.SetWindowPos(_hwnd, 0, (int)Instance.X, (int)Instance.Y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    public void MoveTo(int x, int y)
    {
        Win32.SetWindowPos(_hwnd, 0, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        SavePosition();
    }

    public Win32.RECT PhysicalRect()
    {
        Win32.GetWindowRect(_hwnd, out var r);
        return r;
    }

    private void SavePosition()
    {
        var r = PhysicalRect();
        Instance.X = r.Left;
        Instance.Y = r.Top;
        var info = Win32.MonitorInfo(Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST));
        Instance.Monitor = Win32.DeviceName(info);
        _manager.RequestSave();
    }

    /// <summary>Encaixa nas bordas da área de trabalho do monitor quando chega a menos de 12 px.</summary>
    private void SnapToEdges()
    {
        if (!Instance.SnapToEdges) return;
        var r = PhysicalRect();
        var work = Win32.MonitorInfo(Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST)).rcWork;
        const int Gap = 12, Margin = 8;
        int x = r.Left, y = r.Top;
        if (Math.Abs(r.Left - work.Left - Margin) < Gap) x = work.Left + Margin;
        if (Math.Abs(work.Right - r.Right - Margin) < Gap) x = work.Right - r.Width - Margin;
        if (Math.Abs(r.Top - work.Top - Margin) < Gap) y = work.Top + Margin;
        if (Math.Abs(work.Bottom - r.Bottom - Margin) < Gap) y = work.Bottom - r.Height - Margin;
        if (x != r.Left || y != r.Top)
            Win32.SetWindowPos(_hwnd, 0, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    // ---------- mouse ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var start = PhysicalRect();
        var point = e.GetPosition(Canvas);

        if (!Instance.Locked)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
            var end = PhysicalRect();
            if (Math.Abs(end.Left - start.Left) > 2 || Math.Abs(end.Top - start.Top) > 2)
            {
                SnapToEdges();
                SavePosition();
                SendToDesktopLayer();
                return;
            }
        }

        if (Canvas.ActionAt(point) is { } action) RunAction(action);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        e.Handled = true;
        _manager.ShowWidgetMenu(this);
    }

    private void RunAction(string action)
    {
        try
        {
            Process.Start(new ProcessStartInfo(action) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Ação '{action}' falhou: {ex.Message}");
        }
    }
}

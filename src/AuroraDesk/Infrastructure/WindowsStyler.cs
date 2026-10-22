using System.Runtime.InteropServices;
using System.Windows.Threading;
using AuroraDesk.Core.Backup;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Theming;
using AuroraDesk.Interop;
using AuroraDesk.Theming;

namespace AuroraDesk.Infrastructure;

/// <summary>
/// "Windows com o tema do Aurora", sem entrar em outros programas:
/// <list type="number">
/// <item>Cores oficiais (registro do usuário): modo escuro/claro, cor de destaque e destaque nas
/// barras de título. Os valores originais vão para o backup e voltam ao fechar (opcional) ou em
/// "Restaurar original".</item>
/// <item>Molduras: o DWM pinta a barra de título (as abas, no Explorer), o texto e a borda de cada
/// janela com as cores do tema (DwmSetWindowAttribute de fora, como o Mica For Everyone).</item>
/// </list>
/// </summary>
internal sealed unsafe class WindowsStyler : IDisposable
{
    private const int DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;
    private const int WS_CAPTION = 0x00C00000;

    private readonly AppConfig _config;
    private readonly ThemeService _themes;
    private readonly OriginalValues _backup;
    private readonly HashSet<nint> _styled = [];
    private readonly DispatcherTimer _debounce;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private bool _hooked;

    public WindowsStyler(AppConfig config, ThemeService themes, OriginalValues backup, Dispatcher dispatcher)
    {
        _config = config;
        _themes = themes;
        _backup = backup;
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => { _debounce!.Stop(); ApplyAll(); }, dispatcher) { IsEnabled = false };
        _themes.Changed += Schedule;
    }

    private WindowsStyleConfig Options => _config.WindowsStyle;

    /// <summary>Reaplica tudo (ao iniciar e quando o tema ou as opções mudam).</summary>
    public void Schedule()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void ApplyAll()
    {
        try
        {
            if (Options.SyncColors) ApplyColors();
            if (Options.FrameColors) StyleAllWindows();
            else ClearFrames();
            Hook(Options.FrameColors);
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível aplicar o tema ao Windows", ex);
        }
    }

    // ---------- 1. cores oficiais ----------

    private void ApplyColors()
    {
        var plan = WindowsColorPlan.Build(_themes.Current, new WindowsColorOptions(Options.DarkMode, Options.Accent, Options.AccentOnTitleBars));
        int changed = 0;
        var registry = new UserRegistry();
        foreach (var w in plan)
        {
            if (registry.Get(w.Key, w.Name) == w.Value) continue; // já está assim: nem toca
            _backup.Set(w.Key, w.Name, w.Value); // guarda o original na primeira vez
            changed++;
        }
        if (changed == 0) return;
        Broadcast();
        Log.Info($"Cores do Windows sincronizadas com o tema {_themes.Current.Name} ({changed} valor(es))");
    }

    /// <summary>Devolve as cores originais do Windows (só as do plano; o resto do backup fica).</summary>
    public int RestoreColors()
    {
        int n = _backup.RestoreWhere(v => WindowsColorPlan.Keys.Contains(v.Key, StringComparer.OrdinalIgnoreCase));
        if (n > 0)
        {
            Broadcast();
            Log.Info($"Cores originais do Windows restauradas ({n} valor(es))");
        }
        return n;
    }

    /// <summary>Avisa o Windows e os apps que as cores mudaram (o mesmo que o app Configurações faz).</summary>
    private static void Broadcast()
    {
        const nint HWND_BROADCAST = 0xFFFF;
        const uint SMTO_ABORTIFHUNG = 0x2;
        foreach (var area in new[] { "ImmersiveColorSet", "WindowsThemeElement" })
        {
            nint text = Marshal.StringToHGlobalUni(area);
            try { Win32.SendMessageTimeout(HWND_BROADCAST, Win32.WM_SETTINGCHANGE, 0, text, SMTO_ABORTIFHUNG, 1000, out _); }
            finally { Marshal.FreeHGlobal(text); }
        }
        Win32.SendMessageTimeout(HWND_BROADCAST, Win32.WM_DWMCOLORIZATIONCOLORCHANGED, 0, 0, SMTO_ABORTIFHUNG, 1000, out _);
    }

    // ---------- 2. molduras das janelas ----------

    private void Hook(bool on)
    {
        if (on == _hooked) return;
        if (on)
        {
            WindowEvents.StartLifecycle();
            WindowEvents.WindowChanged += OnWindowChanged;
        }
        else
        {
            WindowEvents.WindowChanged -= OnWindowChanged;
        }
        _hooked = on;
    }

    private void OnWindowChanged(uint ev, nint hwnd)
    {
        if (ev == Win32.EVENT_OBJECT_DESTROY) { _styled.Remove(hwnd); return; }
        if (ev == Win32.EVENT_SYSTEM_FOREGROUND)
        {
            StyleWindow(hwnd); // barato; cobre apps que redefinem a própria barra depois
            return;
        }
        if (ev != Win32.EVENT_OBJECT_SHOW) return;
        // O Explorer (e outros apps) monta a barra/abas alguns instantes depois de mostrar a
        // janela e redefine a cor nesse momento: aplica agora e de novo um pouco depois.
        StyleWindow(hwnd);
        foreach (int ms in new[] { 400, 1500 })
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, _debounce.Dispatcher) { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) => { timer.Stop(); if (Options.FrameColors && Win32.IsWindow(hwnd)) StyleWindow(hwnd); };
            timer.Start();
        }
    }

    private void StyleAllWindows()
    {
        foreach (var hwnd in Win32.TopLevelWindows()) StyleWindow(hwnd);
    }

    /// <summary>Janelas de apps com barra de título (no escopo escolhido), fora do próprio Aurora.</summary>
    private bool Eligible(nint hwnd)
    {
        if (!Win32.IsWindowVisible(hwnd)) return false;
        nint style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
        if ((style & WS_CAPTION) != WS_CAPTION || (style & Win32.WS_CHILD) != 0) return false;
        if ((Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0) return false;
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid) return false;
        return Options.FrameScope != "explorer" || Win32.ClassName(hwnd) == "CabinetWClass";
    }

    private void StyleWindow(nint hwnd)
    {
        if (!Eligible(hwnd)) return;
        var t = _themes.Current;
        uint caption = ColorRef(t.Surface), text = ColorRef(t.Text), border = ColorRef(t.Accent);
        Win32.DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, &caption, sizeof(uint));
        Win32.DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, &text, sizeof(uint));
        Win32.DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, &border, sizeof(uint));
        _styled.Add(hwnd);
    }

    /// <summary>Volta todas as janelas que pintamos para as cores padrão do Windows.</summary>
    private void ClearFrames()
    {
        uint def = DWMWA_COLOR_DEFAULT;
        foreach (var hwnd in _styled)
        {
            if (!Win32.IsWindow(hwnd)) continue;
            Win32.DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, &def, sizeof(uint));
            Win32.DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, &def, sizeof(uint));
            Win32.DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, &def, sizeof(uint));
        }
        _styled.Clear();
    }

    /// <summary>COLORREF = 0x00BBGGRR.</summary>
    private static uint ColorRef(ArgbColor c) => (uint)(c.B << 16 | c.G << 8 | c.R);

    /// <summary>
    /// Ao fechar o app, só com "devolver ao fechar" ligado: molduras ao padrão e cores originais.
    /// Desligado (padrão), tudo continua como está; janelas abertas depois ficam sem moldura.
    /// </summary>
    public void Dispose()
    {
        _debounce.Stop();
        _themes.Changed -= Schedule;
        Hook(false);
        try
        {
            if (!Options.RestoreOnExit) return;
            ClearFrames();
            RestoreColors();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao devolver as cores do Windows", ex);
        }
    }
}

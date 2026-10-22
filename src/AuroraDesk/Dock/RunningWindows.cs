using System.Windows.Threading;
using AuroraDesk.Core.Dock;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;

namespace AuroraDesk.Dock;

/// <summary>Uma janela que apareceria na barra de tarefas.</summary>
internal sealed record WindowRecord(nint Handle, AppIdentity Identity, string Title);

/// <summary>
/// Janelas de aplicativos abertas, mantidas por eventos do sistema (WinEventHook): uma
/// varredura completa só ao iniciar; depois, cada evento reavalia apenas a janela afetada.
/// </summary>
internal sealed class RunningWindows : IDisposable
{
    private readonly Dictionary<nint, WindowRecord> _windows = [];
    private readonly DispatcherTimer _debounce;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public RunningWindows(Dispatcher dispatcher)
    {
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => { _debounce!.Stop(); Changed?.Invoke(); }, dispatcher) { IsEnabled = false };
    }

    public IReadOnlyCollection<WindowRecord> All => _windows.Values;
    public nint Foreground { get; private set; }

    /// <summary>A lista mudou (agrupado: no máximo um aviso a cada 120 ms).</summary>
    public event Action? Changed;

    public void Start()
    {
        foreach (var hwnd in Win32.TopLevelWindows())
            if (Evaluate(hwnd) is { } record) _windows[hwnd] = record;
        Foreground = Win32.GetForegroundWindow();
        WindowEvents.StartLifecycle();
        WindowEvents.WindowChanged += OnWindowChanged;
        Changed?.Invoke();
    }

    private void OnWindowChanged(uint ev, nint hwnd)
    {
        bool changed = false;
        if (ev == Win32.EVENT_SYSTEM_FOREGROUND && Foreground != hwnd)
        {
            Foreground = hwnd;
            changed = true;
        }

        if (ev == Win32.EVENT_OBJECT_DESTROY)
        {
            changed |= _windows.Remove(hwnd);
        }
        else
        {
            var record = Evaluate(hwnd);
            if (record is null) changed |= _windows.Remove(hwnd);
            else if (!_windows.TryGetValue(hwnd, out var old) || old != record) { _windows[hwnd] = record; changed = true; }
        }

        if (changed) { _debounce.Stop(); _debounce.Start(); }
    }

    /// <summary>
    /// Reavalia todas as janelas conhecidas e tira as que não valem mais. O Explorer cria e
    /// esconde janelas sem que todo evento chegue; sem isso, o dock mostraria janelas
    /// "fantasma" e o clique tentaria ativar uma janela que não aparece.
    /// </summary>
    public bool Prune()
    {
        bool changed = false;
        foreach (var hwnd in _windows.Keys.ToList())
        {
            if (Win32.IsWindow(hwnd) && Evaluate(hwnd) is { } record) _windows[hwnd] = record;
            else changed |= _windows.Remove(hwnd);
        }
        if (changed) { _debounce.Stop(); _debounce.Start(); }
        return changed;
    }

    public bool Contains(nint hwnd) => _windows.ContainsKey(hwnd);

    /// <summary>Mesmas regras da barra de tarefas: visível, não "cloaked", sem dono, não é janela de ferramenta.</summary>
    private WindowRecord? Evaluate(nint hwnd)
    {
        if (!Win32.IsWindowVisible(hwnd)) return null;
        nint ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        bool appWindow = (ex & Win32.WS_EX_APPWINDOW) != 0;
        if (!appWindow && ((ex & Win32.WS_EX_TOOLWINDOW) != 0 || (ex & Win32.WS_EX_NOACTIVATE) != 0)) return null;
        if (!appWindow && Win32.GetWindow(hwnd, Win32.GW_OWNER) != 0) return null;
        if (Win32.IsCloaked(hwnd)) return null;
        // A área de trabalho e a barra de tarefas são janelas visíveis do explorer.exe, mas não "apps".
        if (Win32.ClassName(hwnd) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;

        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid) return null;

        string title = Win32.WindowTitle(hwnd);
        if (title.Length == 0 && !appWindow) return null;

        // Reaproveita a identidade já conhecida (evita abrir o processo a cada evento).
        if (_windows.TryGetValue(hwnd, out var known))
            return known.Title == title ? known : known with { Title = title };

        string? appId = ShellInterop.WindowAppId(hwnd);
        string? exe = Win32.ProcessPath(pid);
        if (exe is null && appId is null) return null;
        return new WindowRecord(hwnd, new AppIdentity(appId, exe), title);
    }

    public void Dispose()
    {
        _debounce.Stop();
        WindowEvents.WindowChanged -= OnWindowChanged;
    }
}

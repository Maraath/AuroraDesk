using AuroraDesk.Interop;

namespace AuroraDesk.Infrastructure;

/// <summary>
/// Eventos globais de janela (WinEventHook fora de contexto): chegam na thread de interface,
/// sem varredura periódica. Um único hook por faixa de eventos, compartilhado por todo o app.
/// </summary>
internal static unsafe class WindowEvents
{
    private static nint _foregroundHook;
    private static nint _lifecycleHook;
    private static nint _minimizeHook;

    /// <summary>A janela em primeiro plano mudou.</summary>
    public static event Action<nint>? ForegroundChanged;

    /// <summary>Janela mostrada, oculta, destruída, renomeada ou (des)minimizada. Usado pelo dock.</summary>
    public static event Action<uint, nint>? WindowChanged;

    public static void Start()
    {
        if (_foregroundHook != 0) return;
        _foregroundHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, 0, &Callback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        _minimizeHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND, 0, &Callback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>Liga também os eventos de criação/destruição (só o dock precisa).</summary>
    public static void StartLifecycle()
    {
        Start();
        if (_lifecycleHook != 0) return;
        _lifecycleHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_DESTROY, Win32.EVENT_OBJECT_HIDE, 0, &Callback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
    }

    public static void Stop()
    {
        foreach (var h in new[] { _foregroundHook, _lifecycleHook, _minimizeHook })
            if (h != 0) Win32.UnhookWinEvent(h);
        _foregroundHook = _lifecycleHook = _minimizeHook = 0;
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void Callback(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == 0) return; // só a janela em si (OBJID_WINDOW), não seus filhos
        try
        {
            if (ev == Win32.EVENT_SYSTEM_FOREGROUND) ForegroundChanged?.Invoke(hwnd);
            WindowChanged?.Invoke(ev, hwnd);
        }
        catch (Exception ex)
        {
            Core.Logging.Log.Error("Erro em evento de janela", ex);
        }
    }
}

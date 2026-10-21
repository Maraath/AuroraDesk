using System.Diagnostics;
using System.Windows.Threading;
using AuroraDesk.Core.Backup;
using AuroraDesk.Core.Logging;
using AuroraDesk.Interop;

namespace AuroraDesk.Infrastructure;

/// <summary>
/// Oculta a barra de tarefas do Windows enquanto o app está aberto e a devolve ao fechar.
/// <list type="bullet">
/// <item>Antes de ocultar, guarda o estado original em backup\barra-de-tarefas.json.</item>
/// <item>Liga "ocultar automaticamente" para que janelas maximizadas usem a tela toda, e esconde a janela da barra.</item>
/// <item>Se o Explorer tentar mostrá-la de novo (evento de janela) ou reiniciar (TaskbarCreated), oculta outra vez.</item>
/// <item>Um processo vigia (o próprio exe com --vigia, sem WPF) restaura tudo se o app cair ou for encerrado à força.</item>
/// </list>
/// </summary>
internal sealed class TaskbarHider : IDisposable
{
    private readonly TaskbarBackup _backup;
    private readonly ControlWindow _control;
    private readonly Dispatcher _dispatcher;
    private bool _active;
    private bool _watchdogStarted;

    public TaskbarHider(TaskbarBackup backup, ControlWindow control, Dispatcher dispatcher)
    {
        _backup = backup;
        _control = control;
        _dispatcher = dispatcher;
    }

    public bool IsActive => _active;

    public void Hide()
    {
        if (TaskbarControl.GetState() < 0) { Log.Warn("Barra de tarefas não encontrada (Explorer fechado?)"); return; }
        int state = TaskbarControl.GetState();
        var saved = _backup.Remember(state, Environment.ProcessId);
        StartWatchdog(); // antes de mexer em qualquer coisa
        TaskbarControl.SetState(TaskbarControl.ABS_AUTOHIDE | (state & TaskbarControl.ABS_ALWAYSONTOP));
        TaskbarControl.HideAll();
        if (!_active)
        {
            WindowEvents.StartLifecycle();
            WindowEvents.WindowChanged += OnWindowChanged;
            _control.Message += OnControlMessage;
        }
        _active = true;
        Log.Info($"Barra de tarefas oculta (estado original {saved.OriginalAppBarState})");
    }

    public void Restore()
    {
        if (_active)
        {
            WindowEvents.WindowChanged -= OnWindowChanged;
            _control.Message -= OnControlMessage;
            _active = false;
        }
        if (TaskbarControl.Restore(_backup)) Log.Info("Barra de tarefas restaurada");
    }

    private void OnWindowChanged(uint ev, nint hwnd)
    {
        // O Explorer às vezes mostra a barra de novo (tecla Win, notificações): oculta na hora.
        if (_active && ev == Win32.EVENT_OBJECT_SHOW && TaskbarControl.IsTaskbar(hwnd))
            Win32.ShowWindow(hwnd, 0);
    }

    private bool OnControlMessage(int msg, nint wParam, nint lParam)
    {
        if (_active && msg == ControlWindow.TaskbarCreatedMessage)
        {
            // Explorer reiniciou com uma barra nova: oculta já e de novo quando ela terminar de montar
            // (o Explorer reaplica o próprio estado alguns instantes depois de criar a barra).
            Hide();
            int remaining = 2;
            var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) =>
            {
                if (_active) Hide();
                if (--remaining <= 0) timer.Stop();
            };
            timer.Start();
        }
        return false;
    }

    private void StartWatchdog()
    {
        if (_watchdogStarted) return;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--vigia {Environment.ProcessId}") { UseShellExecute = false, CreateNoWindow = true });
            _watchdogStarted = true;
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível iniciar o processo vigia da barra de tarefas", ex);
        }
    }

    public void Dispose() => Restore();
}

/// <summary>Processo vigia (--vigia): classe separada para não carregar tipos do WPF.</summary>
internal static class TaskbarWatchdog
{
    /// <summary>
    /// Corpo do processo vigia: espera o app <paramref name="pid"/> terminar (de qualquer jeito)
    /// e, se a barra ainda estiver oculta por ele, restaura. Não carrega WPF.
    /// </summary>
    public static int RunWatchdog(int pid, string backupFile)
    {
        const uint SYNCHRONIZE = 0x00100000;
        nint process = Win32.OpenProcess(SYNCHRONIZE, false, (uint)pid);
        if (process != 0)
        {
            WaitForSingleObject(process, uint.MaxValue);
            Win32.CloseHandle(process);
        }
        Thread.Sleep(300); // deixa uma reinicialização do app regravar o arquivo, se for o caso
        var backup = new TaskbarBackup(backupFile);
        if (backup.Load() is { } state && state.OwnerProcessId == pid)
            TaskbarControl.Restore(backup);
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
}

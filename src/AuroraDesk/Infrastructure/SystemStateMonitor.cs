using System.Windows.Threading;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Scheduling;
using AuroraDesk.Interop;

namespace AuroraDesk.Infrastructure;

/// <summary>
/// Decide quando pausar ou desacelerar as medidas: tela bloqueada, monitor desligado,
/// aplicativo em tela cheia/jogo, widgets ocultos e (opcional) notebook na bateria.
/// Tudo por notificações do sistema; só a tela cheia é verificada a cada 5 s.
/// </summary>
internal sealed unsafe class SystemStateMonitor : IDisposable
{
    [Flags]
    public enum PauseReason
    {
        None = 0,
        Locked = 1,
        DisplayOff = 2,
        Fullscreen = 4,
        Hidden = 8,
    }

    private readonly ControlWindow _window;
    private readonly MeasureScheduler _scheduler;
    private readonly PerformanceConfig _config;
    private readonly DispatcherTimer _fullscreenTimer;
    private nint _displayNotification;
    private PauseReason _reasons;

    public SystemStateMonitor(ControlWindow window, MeasureScheduler scheduler, PerformanceConfig config, Dispatcher dispatcher)
    {
        _window = window;
        _scheduler = scheduler;
        _config = config;
        _window.Message += OnMessage;
        Win32.WTSRegisterSessionNotification(window.Handle, Win32.NOTIFY_FOR_THIS_SESSION);
        _displayNotification = Win32.RegisterPowerSettingNotification(window.Handle, Win32.GUID_CONSOLE_DISPLAY_STATE, 0);
        _fullscreenTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => CheckFullscreen(), dispatcher);
        _fullscreenTimer.Start();
        WindowEvents.ForegroundChanged += _ => CheckFullscreen();
        ApplyBattery();
    }

    public PauseReason Reasons => _reasons;

    public event Action? Changed;

    public void SetHidden(bool hidden) => Set(PauseReason.Hidden, hidden);

    private void Set(PauseReason reason, bool on)
    {
        var next = on ? _reasons | reason : _reasons & ~reason;
        if (next == _reasons) return;
        _reasons = next;
        _scheduler.Paused = _reasons != PauseReason.None;
        Log.Info(_reasons == PauseReason.None ? "Medidas retomadas" : $"Medidas pausadas: {_reasons}");
        Changed?.Invoke();
    }

    private void CheckFullscreen()
    {
        if (!_config.PauseOnFullscreen) { Set(PauseReason.Fullscreen, false); return; }
        bool full = Win32.SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4;
        Set(PauseReason.Fullscreen, full);
    }

    public void ApplyBattery()
    {
        bool onBattery = false;
        if (_config.BatterySaver)
        {
            Core.Native.PowerStatus.Read(out bool hasBattery, out bool pluggedIn);
            onBattery = hasBattery && !pluggedIn;
        }
        _scheduler.IntervalMultiplier = onBattery ? 3 : 1;
    }

    private bool OnMessage(int msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win32.WM_WTSSESSION_CHANGE:
                if (_config.PauseOnLock)
                {
                    if ((int)wParam == Win32.WTS_SESSION_LOCK) Set(PauseReason.Locked, true);
                    else if ((int)wParam == Win32.WTS_SESSION_UNLOCK) Set(PauseReason.Locked, false);
                }
                return false;
            case Win32.WM_POWERBROADCAST:
                if ((int)wParam == Win32.PBT_APMPOWERSTATUSCHANGE) ApplyBattery();
                else if ((int)wParam == Win32.PBT_POWERSETTINGCHANGE && lParam != 0)
                {
                    var setting = (Win32.POWERBROADCAST_SETTING*)lParam;
                    if (setting->PowerSetting == Win32.GUID_CONSOLE_DISPLAY_STATE)
                        Set(PauseReason.DisplayOff, setting->Data == 0);
                }
                return false;
        }
        return false;
    }

    public void Dispose()
    {
        _fullscreenTimer.Stop();
        _window.Message -= OnMessage;
        Win32.WTSUnRegisterSessionNotification(_window.Handle);
        if (_displayNotification != 0) Win32.UnregisterPowerSettingNotification(_displayNotification);
    }
}

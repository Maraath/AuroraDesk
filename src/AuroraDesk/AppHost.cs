using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Scheduling;
using AuroraDesk.Interop;
using AuroraDesk.Tray;

namespace AuroraDesk;

/// <summary>
/// Hospeda tudo em um único processo: bandeja, agendador de medidas, widgets e dock.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private readonly Application _app;
    private readonly Dispatcher _dispatcher;

    public AppHost(Application app)
    {
        _app = app;
        _dispatcher = app.Dispatcher;
        Paths = new AppPaths();
        Paths.EnsureCreated();
        Log.Initialize(Paths.Logs);
        ConfigStore = new ConfigStore(Paths.ConfigFile);
        Config = ConfigStore.Load();
        Strings.Language = Strings.Parse(Config.Language);

        // Precisa vir antes de criar qualquer janela. Ver PerformanceConfig.HardwareRendering.
        if (!Config.Performance.HardwareRendering)
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        Scheduler = new MeasureScheduler(MeasureRegistry.CreateDefault(),
            action => _dispatcher.BeginInvoke(DispatcherPriority.Background, action));
        Control = new ControlWindow();
        Tray = new TrayIcon(Control, "Aurora Desk");
    }

    public AppPaths Paths { get; }
    public ConfigStore ConfigStore { get; }
    public AppConfig Config { get; }
    public MeasureScheduler Scheduler { get; }
    public ControlWindow Control { get; }
    public TrayIcon Tray { get; }

    public void Start(bool openSettings)
    {
        Control.Message += OnControlMessage;
        Tray.ContextMenuRequested += ShowTrayMenu;
        Tray.Activated += OpenSettings;
        Tray.Show();

        var startedAt = Process.GetCurrentProcess().StartTime;
        // Medido quando a fila de interface esvazia, isto é, com tudo já visível.
        _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            Log.Info($"Inicialização concluída em {(DateTime.Now - startedAt).TotalMilliseconds:0} ms"));

        if (openSettings) OpenSettings();
    }

    private bool OnControlMessage(int msg, nint wParam, nint lParam)
    {
        if (msg != ControlWindow.CommandMessage) return false;
        switch ((int)wParam)
        {
            case ControlWindow.CommandExit: Exit(); break;
            case ControlWindow.CommandOpenSettings: OpenSettings(); break;
        }
        return true;
    }

    private void ShowTrayMenu()
    {
        var menu = new NativeMenu()
            .Item("Configurações", OpenSettings)
            .Item("Abrir pasta de dados", () => OpenFolder(Paths.Roaming))
            .Separator()
            .Item("Sair", Exit);
        menu.Show(Control.Handle, dark: true);
    }

    public void OpenSettings()
    {
        // Implementada no M5.
        Log.Info("Configurações solicitadas");
    }

    public static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Não foi possível abrir a pasta", ex); }
    }

    public void SaveConfig()
    {
        try { ConfigStore.Save(Config); }
        catch (Exception ex) { Log.Error("Falha ao salvar config.json", ex); }
    }

    public void ReportUiFailure(Exception ex)
    {
        // Ponto único para o motor de widgets isolar o widget que falhou (M3).
    }

    public void Exit() => _app.Shutdown();

    public void Dispose()
    {
        Scheduler.Dispose();
        Tray.Dispose();
        Control.Dispose();
    }
}

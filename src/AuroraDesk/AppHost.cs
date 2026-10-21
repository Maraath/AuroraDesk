using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Scheduling;
using AuroraDesk.Core.Skins;
using AuroraDesk.Core.Theming;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;
using AuroraDesk.Theming;
using AuroraDesk.Tray;
using AuroraDesk.Widgets;

namespace AuroraDesk;

/// <summary>
/// Hospeda tudo em um único processo: bandeja, agendador de medidas, widgets e dock.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private readonly Application _app;
    private DebouncedWatcher? _themeWatcher, _fontWatcher;

    public AppHost(Application app)
    {
        _app = app;
        Dispatcher = app.Dispatcher;
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
            action => Dispatcher.BeginInvoke(DispatcherPriority.Background, action));
        Control = new ControlWindow();
        Tray = new TrayIcon(Control, "Aurora Desk");
        Themes = new ThemeService(Config, new ThemeStore(Paths.Themes), new FontService(Paths.Fonts));
        Widgets = new WidgetManager(this);
    }

    public Dispatcher Dispatcher { get; }
    public AppPaths Paths { get; }
    public ConfigStore ConfigStore { get; }
    public AppConfig Config { get; }
    public MeasureScheduler Scheduler { get; }
    public ControlWindow Control { get; }
    public TrayIcon Tray { get; }
    public ThemeService Themes { get; }
    public WidgetManager Widgets { get; }
    public SystemStateMonitor? State { get; private set; }

    public void Start(bool openSettings)
    {
        Control.Message += OnControlMessage;
        Tray.ContextMenuRequested += ShowTrayMenu;
        Tray.Activated += OpenSettings;
        Tray.Show();

        WindowEvents.Start();
        State = new SystemStateMonitor(Control, Scheduler, Config.Performance, Dispatcher);

        // Temas e fontes recarregam sozinhos ao salvar arquivos nas pastas do usuário.
        _themeWatcher = new DebouncedWatcher(Paths.Themes, "*.json", false, Dispatcher);
        _themeWatcher.Changed += _ => Themes.ReloadFiles();
        _fontWatcher = new DebouncedWatcher(Paths.Fonts, "*.*", false, Dispatcher, delayMs: 800);
        _fontWatcher.Changed += _ => Themes.ReloadFonts();

        Widgets.Start();

        var startedAt = Process.GetCurrentProcess().StartTime;
        // Medido quando a fila de interface esvazia, isto é, com tudo já visível.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            Log.Info($"Inicialização concluída em {(DateTime.Now - startedAt).TotalMilliseconds:0} ms"));

        if (openSettings) OpenSettings();
    }

    private bool OnControlMessage(int msg, nint wParam, nint lParam)
    {
        if (msg == Win32.WM_DWMCOLORIZATIONCOLORCHANGED ||
            (msg == Win32.WM_SETTINGCHANGE && lParam != 0 && System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet"))
        {
            if (Config.FollowWindowsTheme || Config.FollowWindowsAccent) Themes.Refresh();
            return false;
        }
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
        var themeMenu = new NativeMenu();
        foreach (var theme in Themes.Store.All)
        {
            string name = theme.Name;
            themeMenu.Item(name, () => { Themes.Select(name); SaveConfig(); },
                isChecked: !Config.FollowWindowsTheme && Themes.Current.Name == name);
        }
        themeMenu.Separator()
            .Item("Seguir o modo claro/escuro do Windows", () =>
            {
                Config.FollowWindowsTheme = !Config.FollowWindowsTheme;
                Themes.Refresh(force: true);
                SaveConfig();
            }, isChecked: Config.FollowWindowsTheme)
            .Item("Usar a cor de destaque do Windows", () =>
            {
                Config.FollowWindowsAccent = !Config.FollowWindowsAccent;
                Themes.Refresh(force: true);
                SaveConfig();
            }, isChecked: Config.FollowWindowsAccent)
            .Item("Abrir pasta de temas", () => OpenFolder(Paths.Themes));

        var widgetMenu = new NativeMenu();
        foreach (var skin in SkinLibrary.Discover(Widgets.Folder))
        {
            string name = skin;
            widgetMenu.Item(name, () => Widgets.SetEnabled(name, !Widgets.IsEnabled(name)), isChecked: Widgets.IsEnabled(name));
        }
        widgetMenu.Separator()
            .Item("Recarregar todos", Widgets.ReloadAll)
            .Item("Desativar o clique através", Widgets.DisableClickThrough, enabled: Config.Widgets.Any(w => w.ClickThrough))
            .Item("Abrir pasta de widgets", () => OpenFolder(Widgets.Folder));

        var menu = new NativeMenu()
            .Submenu("Tema", themeMenu)
            .Submenu("Widgets", widgetMenu)
            .Item(Config.WidgetsVisible ? "Ocultar widgets" : "Mostrar widgets", () => Widgets.SetVisible(!Config.WidgetsVisible))
            .Separator()
            .Item("Configurações", OpenSettings)
            .Item("Abrir pasta de dados", () => OpenFolder(Paths.Roaming))
            .Separator()
            .Item("Sair", Exit);
        menu.Show(Control.Handle, dark: Themes.Current.Dark);
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

    public void Exit() => _app.Shutdown();

    public void Dispose()
    {
        SaveConfig();
        Widgets.Dispose();
        _themeWatcher?.Dispose();
        _fontWatcher?.Dispose();
        State?.Dispose();
        WindowEvents.Stop();
        Scheduler.Dispose();
        Tray.Dispose();
        Control.Dispose();
    }
}

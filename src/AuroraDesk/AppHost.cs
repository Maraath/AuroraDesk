using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AuroraDesk.Core.Backup;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Scheduling;
using AuroraDesk.Core.Skins;
using AuroraDesk.Core.Theming;
using AuroraDesk.Dock;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;
using AuroraDesk.Settings;
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
        foreach (var copy in Paths.FindPackageCopies())
            Log.Warn($"Há uma cópia dos dados do Aurora redirecionada para um app empacotado: {copy}. " +
                "Abrir o Aurora por esse app faz as mudanças irem para lá, e não valem ao abrir pelo logon.");
        ConfigStore = new ConfigStore(Paths.ConfigFile);
        Config = ConfigStore.Load();
        Strings.Language = Strings.Parse(Config.Language);

        // Precisa vir antes de criar qualquer janela. Ver PerformanceConfig.HardwareRendering e RenderModes.
        RenderModes.HardwareEverywhere = Config.Performance.HardwareRendering;
        if (!Config.Performance.HardwareRendering)
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        ApplyWeather(save: false);
        Scheduler = new MeasureScheduler(MeasureRegistry.CreateDefault(Weather),
            action => Dispatcher.BeginInvoke(DispatcherPriority.Background, action));
        Control = new ControlWindow();
        Tray = new TrayIcon(Control, "Aurora Desk");
        Themes = new ThemeService(Config, new ThemeStore(Paths.Themes), new FontService(Paths.Fonts));
        Widgets = new WidgetManager(this);
        Dock = new DockManager(this);
        var registry = new UserRegistry();
        Backup = new OriginalValues(Path.Combine(Paths.Backup, "windows-original.json"), registry);
        Startup = new StartupRegistration(Backup, registry);
        Trimmer = new MemoryTrimmer(Config.Performance, Dispatcher);
        Taskbar = new TaskbarHider(new TaskbarBackup(Program.BackupFile()), Control, Dispatcher);
        Styler = new WindowsStyler(Config, Themes, Backup, Dispatcher);
    }

    /// <summary>Cores do Windows e molduras das janelas com o tema do Aurora.</summary>
    public WindowsStyler Styler { get; }

    public TaskbarHider Taskbar { get; }

    /// <summary>Liga/desliga a regra "ocultar a barra do Windows enquanto o app estiver aberto".</summary>
    public void SetHideTaskbar(bool hide)
    {
        Config.HideTaskbar = hide;
        if (hide) Taskbar.Hide(); else Taskbar.Restore();
        Dock.ApplyConfig(); // a reserva de espaço do dock depende da barra estar oculta
        SaveConfig();
    }

    public DockManager Dock { get; }
    public WeatherSource Weather { get; } = new();
    public OriginalValues Backup { get; }
    public StartupRegistration Startup { get; }
    public MemoryTrimmer Trimmer { get; }
    private SettingsWindow? _settings;

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

        // Com a regra ligada, a barra do Windows some enquanto o app estiver aberto. Se uma
        // execução anterior caiu com a barra oculta e a regra foi desligada, devolve a barra.
        // Vem antes do dock: a reserva de espaço dele depende da barra estar oculta.
        if (Config.HideTaskbar) Taskbar.Hide();
        else Taskbar.Restore();
        Widgets.Start();
        Dock.Start();
        if (Config.WindowsStyle.SyncColors || Config.WindowsStyle.FrameColors) Styler.Schedule();

        var startedAt = Process.GetCurrentProcess().StartTime;
        // Medido quando a fila de interface esvazia, isto é, com tudo já visível.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            Log.Info($"Inicialização concluída em {(DateTime.Now - startedAt).TotalMilliseconds:0} ms"));
        // A inicialização deixa páginas que não serão mais usadas (JIT, XAML do sistema...).
        Trimmer.Schedule(TimeSpan.FromSeconds(20));

        if (openSettings) OpenSettings();

        if (ConfigStore.ReadOnly)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => MessageBox.Show(
                "Não foi possível ler a sua configuração (o arquivo estava em uso por outro programa).\n\n" +
                "O Aurora abriu com o padrão e NÃO vai salvar nada, para não apagar seus grupos e ajustes. " +
                "Feche o Aurora e abra de novo.",
                "Aurora Desk", MessageBoxButton.OK, MessageBoxImage.Warning));
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

    /// <summary>
    /// Pausa as medidas por "oculto" só quando ninguém as usa: nem widgets visíveis, nem as
    /// ilhas do dock. Chamado quando widgets ou o dock mudam.
    /// </summary>
    public void UpdateMeasureDemand() => State?.SetHidden(!Widgets.NeedsMeasures && !Dock.NeedsMeasures);

    /// <summary>Menu principal (o da bandeja). Também aberto pelo ícone do Aurora Desk no dock.</summary>
    public void ShowMainMenu() => ShowTrayMenu();

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
            .Submenu("Dock", Dock.SettingsMenu())
            .Item(Config.Dock.Enabled ? "Ocultar dock" : "Mostrar dock", () => Dock.SetEnabled(!Config.Dock.Enabled))
            .Item("Ocultar a barra de tarefas do Windows", () => SetHideTaskbar(!Config.HideTaskbar), isChecked: Config.HideTaskbar)
            .Separator()
            .Item("Configurações", OpenSettings)
            .Item("Abrir pasta de dados", () => OpenFolder(Paths.Roaming))
            .Separator()
            .Submenu("Energia", PowerActions.Menu())
            .Item("Sair", Exit);
        menu.Show(Control.Handle, dark: Themes.Current.Dark);
    }

    /// <summary>Abre (ou traz à frente) as configurações. A janela é descartada ao fechar.</summary>
    public void OpenSettings()
    {
        if (_settings is not null)
        {
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(this);
        _settings.Closed += (_, _) =>
        {
            _settings = null;
            Trimmer.Schedule(TimeSpan.FromSeconds(2), force: Config.Performance.TrimWorkingSet);
        };
        _settings.Show();
        _settings.Activate();
    }

    /// <summary>Aplica a configuração do clima à fonte compartilhada.</summary>
    public void ApplyWeather(bool save = true)
    {
        // Outra cidade (ou o clima acabou de ser ligado): descarta o tempo guardado e consulta já.
        bool changed = Weather.Latitude != Config.Weather.Latitude || Weather.Longitude != Config.Weather.Longitude || Weather.Enabled != Config.Weather.Enabled;
        if (changed) Weather.Invalidate();
        Weather.Enabled = Config.Weather.Enabled;
        Weather.Latitude = Config.Weather.Latitude;
        Weather.Longitude = Config.Weather.Longitude;
        if (changed) Scheduler?.RefreshNow("clima");
        if (save) SaveConfig();
    }

    /// <summary>Desfaz tudo que o app mudou no Windows. Devolve quantos itens foram restaurados.</summary>
    public int RestoreOriginal()
    {
        int count = 0;
        // Cores e molduras primeiro: elas avisam o Windows para redesenhar.
        Config.WindowsStyle.SyncColors = false;
        Config.WindowsStyle.FrameColors = false;
        count += Styler.RestoreColors();
        Styler.ApplyAll();
        try { count += Backup.RestoreAll(); }
        catch (Exception ex) { Log.Error("Falha ao restaurar valores originais", ex); }
        if (Config.HideTaskbar || Taskbar.IsActive)
        {
            Config.HideTaskbar = false;
            Taskbar.Restore();
            SaveConfig();
            count++;
        }
        if (Config.Dock.ReserveSpace)
        {
            Config.Dock.ReserveSpace = false;
            count++;
        }
        Dock.ApplyConfig(); // devolve o espaço reservado (AppBar), se houver
        Log.Info($"Restaurar original: {count} item(ns)");
        return count;
    }

    /// <summary>Faz backup do perfil atual, importa o novo e reinicia.</summary>
    public void ImportProfile(string zip)
    {
        string backup = Path.Combine(Paths.Backup, $"antes-de-importar-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        Core.Profiles.ProfileArchive.Export(backup, Config, Paths);
        var result = Core.Profiles.ProfileArchive.Import(zip, Paths, Config);
        Log.Info($"Perfil importado: {result.Files} arquivo(s), {result.Skipped.Count} ignorado(s){(result.Skipped.Count > 0 ? ": " + string.Join(", ", result.Skipped.Take(10)) : "")}");
        if (result.Config is { } merged)
        {
            // Reinicia lendo a configuração importada.
            ConfigStore.Save(merged);
            _skipSaveOnExit = true;
        }
        RequestRestartFor(() => { });
    }

    private bool _skipSaveOnExit;

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

    /// <summary>
    /// Aplica uma mudança que exige reiniciar (ex.: Mica/Acrílico troca o modo de renderização),
    /// grava a configuração e reabre o app.
    /// </summary>
    public void RequestRestartFor(Action change)
    {
        change();
        if (!_skipSaveOnExit) SaveConfig();
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--reiniciar {Environment.ProcessId}") { UseShellExecute = false });
            Log.Info("Reiniciando para aplicar a mudança");
            Exit();
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível reiniciar; a mudança vale na próxima abertura", ex);
        }
    }

    public void Dispose()
    {
        _settings?.Close();
        if (!_skipSaveOnExit) SaveConfig();
        Taskbar.Restore(); // primeiro: a barra volta mesmo se algo abaixo falhar
        Styler.Dispose();  // molduras ao padrão e, se configurado, as cores originais do Windows
        Dock.Dispose();
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

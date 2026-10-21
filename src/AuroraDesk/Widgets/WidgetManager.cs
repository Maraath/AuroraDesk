using System.Diagnostics;
using System.Windows.Threading;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Skins;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;
using AuroraDesk.Tray;

namespace AuroraDesk.Widgets;

/// <summary>
/// Ciclo de vida dos widgets: carrega skins, assina medidas no agendador central, cria as
/// janelas, recarrega ao salvar skin.json (hot reload) e isola falhas por widget.
/// </summary>
internal sealed class WidgetManager : IDisposable
{
    private sealed class Running(WidgetInstance instance, WidgetWindow window)
    {
        public WidgetInstance Instance { get; } = instance;
        public WidgetWindow Window { get; } = window;
        public List<MeasureSpec> Subscriptions { get; } = [];
        public SkinDefinition? Skin { get; set; }
    }

    private readonly AppHost _host;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, Running> _running = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    // Só para validar tipos ao interpretar skins; as instâncias vêm do agendador.
    private readonly MeasureRegistry _registry = MeasureRegistry.CreateDefault();
    private DebouncedWatcher? _watcher;

    public WidgetManager(AppHost host)
    {
        _host = host;
        _dispatcher = host.Dispatcher;
        _saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _saveTimer!.Stop(); _host.SaveConfig(); }, _dispatcher) { IsEnabled = false };
    }

    private AppConfig Config => _host.Config;
    public string Folder => _host.Paths.Widgets;
    public bool Visible => Config.WidgetsVisible;
    public int Count => _running.Count;

    public void Start()
    {
        // Copia os exemplos que faltam (inclusive os que chegaram numa versão nova, como "clima").
        // Nunca sobrescreve: um skin editado pelo usuário fica como está.
        int installed = SkinLibrary.InstallSamples(Folder);
        if (installed > 0 && Config.FirstRunDone) Log.Info($"{installed} arquivo(s) de exemplo novo(s) instalado(s)");
        if (!Config.FirstRunDone)
        {
            foreach (var skin in SkinLibrary.DefaultEnabled)
                Config.Widgets.Add(new WidgetInstance { Skin = skin });
            Config.FirstRunDone = true;
            _host.SaveConfig();
        }

        _host.Scheduler.Changed += OnMeasuresChanged;
        _host.Themes.Changed += OnThemeChanged;
        WindowEvents.ForegroundChanged += OnForegroundChanged;
        _host.Control.Message += OnControlMessage;

        _watcher = new DebouncedWatcher(Folder, "*.*", recursive: true, _dispatcher);
        _watcher.Changed += OnFilesChanged;

        if (Config.WidgetsVisible)
            foreach (var instance in Config.Widgets.Where(w => w.Enabled))
                Open(instance);
        _host.State?.SetHidden(!Config.WidgetsVisible || _running.Count == 0);
        WatchDesktopIfActive();
    }

    // ---------- abrir / fechar ----------

    private void Open(WidgetInstance instance)
    {
        if (_running.ContainsKey(instance.Id)) return;
        var window = new WidgetWindow(this, instance);
        var running = new Running(instance, window);
        _running[instance.Id] = running;

        LoadSkin(running, initial: true);
        try
        {
            window.Show();
            if (double.IsNaN(instance.X) || double.IsNaN(instance.Y)) AutoPlace(window);
        }
        catch (Exception ex)
        {
            Log.Error($"Widget '{instance.Skin}' não pôde ser exibido", ex);
            Close(instance.Id);
        }
    }

    private void Close(string id)
    {
        if (!_running.Remove(id, out var running)) return;
        Unsubscribe(running);
        running.Window.Close();
    }

    /// <summary>Liga/desliga um widget (cria a instância se o skin ainda não estiver na configuração).</summary>
    public void SetEnabled(string skin, bool enabled)
    {
        var instance = Config.Widgets.FirstOrDefault(w => w.Skin.Equals(skin, StringComparison.OrdinalIgnoreCase));
        if (instance is null)
        {
            if (!enabled) return;
            instance = new WidgetInstance { Skin = skin };
            Config.Widgets.Add(instance);
        }
        instance.Enabled = enabled;
        if (enabled && Config.WidgetsVisible) Open(instance);
        else if (!enabled) Close(instance.Id);
        _host.State?.SetHidden(!Config.WidgetsVisible || _running.Count == 0);
        RequestSave();
    }

    public bool IsEnabled(string skin) =>
        Config.Widgets.Any(w => w.Enabled && w.Skin.Equals(skin, StringComparison.OrdinalIgnoreCase));

    public WidgetInstance? InstanceFor(string skin) =>
        Config.Widgets.FirstOrDefault(w => w.Skin.Equals(skin, StringComparison.OrdinalIgnoreCase));

    public void SetVisible(bool visible)
    {
        Config.WidgetsVisible = visible;
        if (visible)
            foreach (var instance in Config.Widgets.Where(w => w.Enabled)) Open(instance);
        else
            foreach (var id in _running.Keys.ToList()) Close(id);
        _host.State?.SetHidden(!visible || _running.Count == 0);
        RequestSave();
    }

    /// <summary>Aplica mudanças feitas na configuração de uma instância (camada, escala...).</summary>
    public void ApplyInstance(WidgetInstance instance)
    {
        if (!_running.TryGetValue(instance.Id, out var r)) return;
        r.Window.ApplyScaleAndOpacity();
        r.Window.ApplyStyles();
        r.Window.ApplyLayer();
        r.Window.Canvas.OnThemeChanged(); // fonte do widget pode ter mudado
        RequestSave();
    }

    // ---------- skins e medidas ----------

    private void LoadSkin(Running running, bool initial)
    {
        string file = SkinLibrary.SkinFile(Folder, running.Instance.Skin);
        SkinDefinition skin;
        try
        {
            skin = SkinParser.ParseFile(file, _registry);
        }
        catch (Exception ex) when (ex is SkinException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Skin '{running.Instance.Skin}': {ex.Message}");
            if (running.Skin is null)
            {
                // Sem versão anterior: mostra um cartão de erro no lugar do widget.
                var placeholder = new SkinDefinition { Name = running.Instance.Skin, Width = 260, Height = 70, Folder = Path.GetDirectoryName(file) ?? "" };
                running.Window.Canvas.Load(placeholder, new Dictionary<string, Measure>(), _host.Themes);
            }
            running.Window.Canvas.SetError($"{running.Instance.Skin}: {ex.Message}");
            return;
        }

        foreach (var w in skin.Warnings) Log.Warn($"Skin '{running.Instance.Skin}': {w}");

        // Assina as novas medidas antes de soltar as antigas: as compartilhadas não são recriadas.
        var measures = new Dictionary<string, Measure>(StringComparer.OrdinalIgnoreCase);
        var subscriptions = new List<MeasureSpec>();
        foreach (var (name, spec) in skin.Measures)
        {
            try
            {
                measures[name] = _host.Scheduler.Subscribe(spec);
                subscriptions.Add(spec);
            }
            catch (Exception ex)
            {
                Log.Warn($"Skin '{running.Instance.Skin}', medida '{name}': {ex.Message}");
            }
        }
        Unsubscribe(running);
        running.Subscriptions.AddRange(subscriptions);
        running.Skin = skin;

        running.Window.Canvas.SetError(null);
        running.Window.Canvas.Load(skin, measures, _host.Themes);
        if (!initial) Log.Info($"Skin '{running.Instance.Skin}' recarregado");
    }

    private void Unsubscribe(Running running)
    {
        foreach (var spec in running.Subscriptions) _host.Scheduler.Unsubscribe(spec);
        running.Subscriptions.Clear();
    }

    private void OnMeasuresChanged(IReadOnlyList<Measure> changed)
    {
        foreach (var r in _running.Values)
        {
            try
            {
                r.Window.Canvas.OnMeasuresChanged(changed);
            }
            catch (Exception ex)
            {
                Log.Error($"Widget '{r.Instance.Skin}' falhou ao atualizar", ex);
            }
        }
    }

    private void OnThemeChanged()
    {
        foreach (var r in _running.Values)
        {
            try { r.Window.Canvas.OnThemeChanged(); }
            catch (Exception ex) { Log.Error($"Widget '{r.Instance.Skin}' falhou ao trocar o tema", ex); }
        }
    }

    private void OnFilesChanged(IReadOnlyCollection<string> paths)
    {
        // Pasta do skin = primeiro nível abaixo de widgets\.
        var skins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string root = Path.TrimEndingDirectorySeparator(Folder) + Path.DirectorySeparatorChar;
        foreach (var p in paths)
        {
            if (!p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = p[root.Length..];
            int sep = rest.IndexOf(Path.DirectorySeparatorChar);
            skins.Add(sep < 0 ? rest : rest[..sep]);
        }

        foreach (var r in _running.Values.Where(r => skins.Contains(r.Instance.Skin)).ToList())
            LoadSkin(r, initial: false);
        SkinsChanged?.Invoke();
    }

    /// <summary>Lista de skins na pasta mudou (para a tela de configurações).</summary>
    public event Action? SkinsChanged;

    public void ReloadAll()
    {
        foreach (var r in _running.Values.ToList()) LoadSkin(r, initial: false);
    }

    // ---------- camada da área de trabalho ----------

    private static bool IsDesktopWindow(nint hwnd) => hwnd != 0 && Win32.ClassName(hwnd) is "WorkerW" or "Progman";

    /// <summary>
    /// Win+D traz o desktop para a frente; ao voltar para outra janela, ele desce de novo. Os
    /// widgets acompanham, sempre logo acima dele. Se o desktop já estava em primeiro plano
    /// (ex.: depois de um clique nele), o Win+D não gera evento de foco; por isso, enquanto o
    /// desktop estiver em primeiro plano, e só nesse período, a ordem Z é conferida a cada
    /// 400 ms (poucas chamadas GetWindow; nada é redesenhado).
    /// </summary>
    private void OnForegroundChanged(nint hwnd)
    {
        bool desktop = IsDesktopWindow(hwnd);
        if (!desktop && !_desktopWatch.IsEnabled) return; // foco entre janelas comuns: nada muda
        WidgetWindow.DesktopAnchor = desktop ? hwnd : 0;
        PlaceOnDesktop();
        if (desktop) _desktopWatch.Start();
        else
        {
            _desktopWatch.Stop();
            _settle = 3; // o desktop ainda está descendo: confere mais algumas vezes
            _settleTimer.Start();
        }
    }

    private DispatcherTimer? _desktopWatchTimer, _settleTimerField;
    private int _settle;

    private DispatcherTimer _desktopWatch => _desktopWatchTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
    {
        if (!IsDesktopWindow(Win32.GetForegroundWindow())) { OnForegroundChanged(Win32.GetForegroundWindow()); return; }
        PlaceOnDesktop(onlyIfNeeded: true);
    }, _dispatcher) { IsEnabled = false };

    private DispatcherTimer _settleTimer => _settleTimerField ??= new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
    {
        PlaceOnDesktop(onlyIfNeeded: true);
        if (--_settle <= 0) _settleTimer.Stop();
    }, _dispatcher) { IsEnabled = false };

    private void PlaceOnDesktop(bool onlyIfNeeded = false)
    {
        foreach (var r in _running.Values)
            if (!onlyIfNeeded || !r.Window.IsJustAboveDesktop()) r.Window.SendToDesktopLayer();
    }

    /// <summary>Se o desktop estiver em primeiro plano agora (início, Explorer reiniciado), passa a vigiá-lo.</summary>
    private void WatchDesktopIfActive()
    {
        nint fg = Win32.GetForegroundWindow();
        if (IsDesktopWindow(fg)) OnForegroundChanged(fg);
    }

    private bool OnControlMessage(int msg, nint wParam, nint lParam)
    {
        if (msg == ControlWindow.TaskbarCreatedMessage)
        {
            // Explorer reiniciou: nova janela da área de trabalho, reposiciona as camadas.
            WidgetWindow.DesktopAnchor = 0;
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                foreach (var r in _running.Values) r.Window.ApplyLayer();
                WatchDesktopIfActive();
            });
        }
        else if (msg == Win32.WM_DISPLAYCHANGE)
        {
            foreach (var r in _running.Values) r.Window.ApplyLayer();
        }
        return false;
    }

    // ---------- posicionamento automático ----------

    private void AutoPlace(WidgetWindow window)
    {
        var primary = Win32.MonitorInfo(Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTOPRIMARY)).rcWork;
        var size = window.PhysicalRect();
        int margin = (int)(24 * Win32.GetDpiForWindow(window.Handle) / 96.0);
        int y = primary.Top + margin;
        // Empilha abaixo dos widgets já posicionados automaticamente na coluna da direita.
        foreach (var other in _running.Values)
        {
            if (other.Window == window || other.Window.Handle == 0) continue;
            var r = other.Window.PhysicalRect();
            if (r.Right >= primary.Right - margin - 2 && r.Bottom + margin / 2 > y) y = r.Bottom + margin / 2;
        }
        window.MoveTo(primary.Right - size.Width - margin, y);
    }

    public void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---------- menu do widget ----------

    public void ShowWidgetMenu(WidgetWindow window)
    {
        var i = window.Instance;
        void Apply(Action change) { change(); ApplyInstance(i); }

        var layer = new NativeMenu()
            .Item("Na área de trabalho", () => Apply(() => i.Layer = "desktop"), isChecked: i.Layer is not ("normal" or "topo"))
            .Item("Normal", () => Apply(() => i.Layer = "normal"), isChecked: i.Layer == "normal")
            .Item("Sempre no topo", () => Apply(() => i.Layer = "topo"), isChecked: i.Layer == "topo");

        var opacity = new NativeMenu();
        foreach (var v in new[] { 1.0, 0.9, 0.75, 0.6, 0.4 })
            opacity.Item($"{v * 100:0}%", () => Apply(() => i.Opacity = v), isChecked: Math.Abs(i.Opacity - v) < 0.01);

        var scale = new NativeMenu();
        foreach (var v in new[] { 0.75, 1.0, 1.25, 1.5, 2.0 })
            scale.Item($"{v * 100:0}%", () => Apply(() => i.Scale = v), isChecked: Math.Abs(i.Scale - v) < 0.01);

        new NativeMenu()
            .Submenu("Camada", layer)
            .Submenu("Opacidade", opacity)
            .Submenu("Escala", scale)
            .Item("Travar posição", () => Apply(() => i.Locked = !i.Locked), isChecked: i.Locked)
            .Item("Encaixar nas bordas", () => Apply(() => i.SnapToEdges = !i.SnapToEdges), isChecked: i.SnapToEdges)
            .Item("Clique através (desfazer pela bandeja/configurações)", () => Apply(() => i.ClickThrough = true))
            .Separator()
            .Item("Editar skin", () => OpenEditor(i.Skin))
            .Item("Abrir pasta do skin", () => AppHost.OpenFolder(Path.Combine(Folder, i.Skin)))
            .Item("Recarregar", () => { if (_running.TryGetValue(i.Id, out var r)) LoadSkin(r, initial: false); })
            .Separator()
            .Item("Fechar widget", () => SetEnabled(i.Skin, false))
            .Show(window.Handle, _host.Themes.Current.Dark);
    }

    public void OpenEditor(string skin)
    {
        string file = SkinLibrary.SkinFile(Folder, skin);
        try
        {
            // "edit" abre no editor associado a .json; se não houver, usa o Bloco de Notas.
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Verb = "edit" });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Não foi possível abrir o editor", ex); }
        }
    }

    /// <summary>
    /// Converte um skin .ini do Rainmeter (subconjunto) para uma nova pasta de widget, copia as
    /// imagens referenciadas (só caminhos relativos e formatos de imagem) e liga o widget.
    /// </summary>
    public (string Skin, IReadOnlyList<string> Warnings) ImportRainmeter(string iniPath)
    {
        string baseName = string.Concat(Path.GetFileNameWithoutExtension(iniPath).Split(Path.GetInvalidFileNameChars())).Trim();
        if (baseName.Length == 0) baseName = "rainmeter";
        string skin = "rm-" + baseName.ToLowerInvariant().Replace(' ', '-');
        for (int n = 2; Directory.Exists(Path.Combine(Folder, skin)); n++) skin = $"rm-{baseName.ToLowerInvariant().Replace(' ', '-')}-{n}";

        var result = RainmeterImporter.Convert(File.ReadAllText(iniPath), baseName);
        string target = Path.Combine(Folder, skin);
        Directory.CreateDirectory(target);

        var warnings = result.Warnings.ToList();
        string source = Path.GetDirectoryName(iniPath)!;
        foreach (var image in result.Images.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string ext = Path.GetExtension(image).ToLowerInvariant();
            string from = Core.Profiles.ProfileArchive.SafeCombine(source, image);
            string to = Core.Profiles.ProfileArchive.SafeCombine(target, image);
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif") || from.Length == 0 || to.Length == 0 || !File.Exists(from))
            {
                warnings.Add($"Imagem '{image}' não copiada");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
        }

        // Grava por último: o hot reload só vê o skin já completo.
        File.WriteAllText(SkinLibrary.SkinFile(Folder, skin), result.Json);
        foreach (var w in warnings) Log.Warn($"Importação Rainmeter ({skin}): {w}");
        Log.Info($"Skin do Rainmeter importado como '{skin}' ({warnings.Count} aviso(s))");
        SetEnabled(skin, true);
        return (skin, warnings);
    }

    /// <summary>Remove o clique através de todos os widgets (atalho da bandeja).</summary>
    public void DisableClickThrough()
    {
        foreach (var w in Config.Widgets.Where(w => w.ClickThrough))
        {
            w.ClickThrough = false;
            ApplyInstance(w);
        }
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _desktopWatchTimer?.Stop();
        _settleTimerField?.Stop();
        _watcher?.Dispose();
        WindowEvents.ForegroundChanged -= OnForegroundChanged;
        _host.Control.Message -= OnControlMessage;
        foreach (var id in _running.Keys.ToList()) Close(id);
    }
}

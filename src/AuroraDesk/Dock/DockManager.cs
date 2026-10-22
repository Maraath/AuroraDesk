using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Dock;
using AuroraDesk.Core.Logging;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;
using AuroraDesk.Tray;

namespace AuroraDesk.Dock;

/// <summary>
/// Dock: itens fixados + aplicativos em execução, cliques, menus, arrastar e soltar,
/// ocultar automaticamente e posição. A barra de tarefas do Windows nunca é alterada.
/// </summary>
internal sealed partial class DockManager : IDisposable
{
    private readonly AppHost _host;
    private readonly IconCache _icons;
    private readonly ShortcutKeeper _shortcuts;
    private readonly DispatcherTimer _hideTimer;
    private readonly Dictionary<DockItem, (AppIdentity Identity, string Name, string? Icon)> _resolved = [];
    private readonly Dictionary<string, string> _exeNames = new(StringComparer.OrdinalIgnoreCase);
    private RunningWindows? _running;
    private DockWindow? _window;
    private List<DockEntry> _entries = [];
    private bool _fullscreen;
    private string _layoutKey = "";

    public DockManager(AppHost host)
    {
        _host = host;
        _icons = new IconCache(host.Paths.IconCache);
        _shortcuts = new ShortcutKeeper(host.Paths.Shortcuts);
        _hideTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => { _hideTimer!.Stop(); HideNow(); }, host.Dispatcher) { IsEnabled = false };
    }

    private DockConfig Config => _host.Config.Dock;
    public bool IsVisible => _window is not null;
    public int IconCount => _icons.Count;

    public void Start()
    {
        if (!Config.Initialized)
        {
            Config.Items.AddRange(DefaultItems());
            Config.Initialized = true;
            _host.SaveConfig();
        }
        // Atalhos fixados antes desta versão também ganham uma cópia guardada.
        if (_shortcuts.KeepAll(Config.Items)) _host.SaveConfig();
        MigratePackagedApps();
        _host.Themes.Changed += OnThemeChanged;
        _host.Control.Message += OnControlMessage;
        if (_host.State is { } state) state.Changed += OnStateChanged;
        if (Config.Enabled) Show();
    }

    // ---------- mostrar / ocultar ----------

    public void SetEnabled(bool enabled)
    {
        Config.Enabled = enabled;
        if (enabled) Show(); else Close();
        _host.SaveConfig();
    }

    private void Show()
    {
        if (_window is not null) return;
        _running ??= new RunningWindows(_host.Dispatcher);
        _running.Changed += Rebuild;

        _window = new DockWindow(_host.Themes, Config.Backdrop);
        var s = _window.Surface;
        s.ItemClicked += OnItemClicked;
        s.ContextRequested += OnContextRequested;
        s.ItemMoved += OnItemMoved;
        s.ItemDraggedOut += OnItemDraggedOut;
        s.FilesDropped += OnFilesDropped;
        s.FilesDroppedOnGroup += OnFilesDroppedOnGroup;
        s.ItemDroppedOnGroup += OnItemDroppedOnGroup;
        s.MouseEntered += () => { _hideTimer.Stop(); if (s.IsHidden) { s.SetHidden(false); SetIslandsVisible(true); } };
        s.MouseLeft += () => { if (AutoHideActive) { _hideTimer.Stop(); _hideTimer.Start(); } };
        _window.FullscreenChanged += full => { _fullscreen = full; UpdateVisibility(); };
        _window.AppBarPositionChanged += Place;
        ApplySurfaceSettings();
        _window.Show();
        _window.ApplyBackdrop(_host.Themes.Current.Panel);
        Win32.InputAnchor = _window.Handle; // Win+A/N/B continuam funcionando com um app elevado na frente

        if (_running.All.Count == 0) _running.Start();
        Rebuild();
        ShowIslands();
        if (AutoHideActive) { _hideTimer.Stop(); _hideTimer.Start(); }
    }

    private void Close()
    {
        if (_window is null) return;
        _hideTimer.Stop();
        if (_running is not null) _running.Changed -= Rebuild;
        CloseIslands();
        Win32.InputAnchor = 0;
        _window.Close(); // devolve o espaço da AppBar
        _window = null;
        _layoutKey = "";
        _host.UpdateMeasureDemand();
    }

    private bool AutoHideActive => Config.AutoHide && !Reserve && _window is { UsesBackdrop: false };

    /// <summary>Reserva de espaço valendo agora (depende de a barra do Windows estar oculta).</summary>
    private bool Reserve => Config.EffectiveReserve(_host.Taskbar.IsActive);

    private void HideNow()
    {
        if (_window is null || !AutoHideActive || _window.IsMouseOver) return;
        _window.Surface.SetHidden(true);
        SetIslandsVisible(false);
    }

    private void OnStateChanged()
    {
        _fullscreen = (_host.State!.Reasons & SystemStateMonitor.PauseReason.Fullscreen) != 0;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_window is null) return;
        // Em jogos e vídeos em tela cheia o dock sai do caminho.
        if (_fullscreen && _window.IsVisible) { _window.Hide(); SetIslandsVisible(false); }
        else if (!_fullscreen && !_window.IsVisible) { _window.Show(); Place(); SetIslandsVisible(true); }
    }

    // ---------- itens ----------

    private void Rebuild()
    {
        if (_window is null) return;
        var entries = new List<DockEntry>();
        if (Config.SearchButton)
            entries.Add(new DockEntry { Name = "Pesquisar", Glyph = "", IsSearch = true });
        foreach (var item in Config.Items)
        {
            var (identity, name, icon) = Resolve(item);
            entries.Add(new DockEntry { Pinned = item, Name = name, Identity = identity, IconSource = icon });
        }

        var extras = new List<DockEntry>();
        nint foreground = _running?.Foreground ?? 0;
        foreach (var w in _running?.All ?? [])
        {
            var target = entries.FirstOrDefault(e => !e.IsSeparator && !e.Identity.IsEmpty && e.Identity.Matches(w.Identity))
                ?? extras.FirstOrDefault(e => e.Identity.Matches(w.Identity));
            if (target is null)
            {
                if (!Config.ShowRunning) continue;
                target = new DockEntry { Name = RunningName(w), Identity = w.Identity, IconSource = IconSourceFor(w.Identity) };
                extras.Add(target);
            }
            target.Windows.Add(w);
            if (w.Handle == foreground) target.IsActive = true;
        }
        if (extras.Count > 0)
        {
            if (entries.Count > 0) entries.Add(new DockEntry { Pinned = new DockItem { Kind = "separador" }, Name = "" });
            entries.AddRange(extras);
        }

        int iconPixels = IconPixels();
        foreach (var e in entries)
        {
            if (!e.IsSeparator && e.IconSource is not null)
                e.Icon = _icons.Get(e.IconSource, iconPixels);
            // Grupo: miniaturas dos 4 primeiros atalhos (ícones pequenos, metade do tamanho).
            if (e.Pinned is { IsGroup: true } g)
                e.GroupIcons = (g.Items ?? []).Take(4).Select(c => Resolve(c).Item3 is { } src ? _icons.Get(src, Math.Max(32, iconPixels / 2)) : null).ToList();
        }

        _entries = entries;
        _window.Surface.SetEntries(entries);

        // Só reposiciona a janela quando o comprimento muda.
        string key = $"{entries.Count}|{entries.Count(e => e.IsSeparator)}|{Config.Edge}|{Config.IconSize}|{Config.Monitor}|{Reserve}";
        if (key != _layoutKey) { _layoutKey = key; Place(); }
    }

    private int IconPixels()
    {
        double scale = Win32.MonitorScale(TargetMonitor());
        double px = Config.IconSize * (Config.Zoom ? Config.ZoomFactor : 1) * scale;
        foreach (int size in new[] { 32, 48, 64, 96, 128, 256 })
            if (size >= px) return size;
        return 256;
    }

    private (AppIdentity, string, string?) Resolve(DockItem item)
    {
        if (_resolved.TryGetValue(item, out var r)) return r;
        string path = Environment.ExpandEnvironmentVariables(item.Path);
        AppIdentity identity = default;
        string name = item.Name ?? "";
        string? icon = path;

        switch (item.Kind)
        {
            case "separador":
                icon = null;
                break;
            case "grupo":
                icon = null;
                if (name.Length == 0) name = "Grupo";
                break;
            case "app":
                identity = new AppIdentity(item.Path, null);
                icon = @"shell:AppsFolder\" + item.Path;
                if (name.Length == 0) name = item.Path.Split('!')[0].Split('_')[0].Split('.').Last();
                break;
            case "pasta":
                if (name.Length == 0) name = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;
                break;
            default:
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    var (target, appId, _) = ShellInterop.ResolveShortcut(path);
                    identity = new AppIdentity(appId, target);
                }
                else
                {
                    identity = new AppIdentity(null, path);
                }
                if (name.Length == 0) name = ExeName(path);
                break;
        }
        r = (identity, name, icon);
        _resolved[item] = r;
        return r;
    }

    private static string? IconSourceFor(AppIdentity id)
    {
        bool packaged = id.ExePath is null
            || id.ExePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || id.ExePath.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
        return id.AppId is not null && packaged ? @"shell:AppsFolder\" + id.AppId : id.ExePath;
    }

    private string RunningName(WindowRecord w)
    {
        if (w.Identity.ExePath is { } exe && !exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            return ExeName(exe);
        return w.Title.Length > 0 ? w.Title : "Aplicativo";
    }

    /// <summary>Descrição do executável (ex.: "Google Chrome"), com cache.</summary>
    private string ExeName(string path)
    {
        if (_exeNames.TryGetValue(path, out var name)) return name;
        name = Path.GetFileNameWithoutExtension(path);
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) name = info.FileDescription.Trim();
            }
        }
        catch { }
        if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name[1..];
        _exeNames[path] = name;
        return name;
    }

    // ---------- posição ----------

    private nint TargetMonitor()
    {
        if (!string.IsNullOrEmpty(Config.Monitor))
            foreach (var m in Win32.Monitors())
                if (string.Equals(Win32.DeviceName(Win32.MonitorInfo(m)), Config.Monitor, StringComparison.OrdinalIgnoreCase))
                    return m;
        return Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTOPRIMARY);
    }

    private static DockEdge ParseEdge(string edge) => edge switch
    {
        "superior" => DockEdge.Top,
        "esquerda" => DockEdge.Left,
        "direita" => DockEdge.Right,
        _ => DockEdge.Bottom,
    };

    private void ApplySurfaceSettings()
    {
        if (_window is null) return;
        var s = _window.Surface;
        s.Edge = ParseEdge(Config.Edge);
        s.IconSize = Math.Clamp(Config.IconSize, 24, 96);
        s.Spacing = Math.Clamp(Config.Spacing, 0, 24);
        bool backdrop = _window.UsesBackdrop;
        s.Zoom = Config.Zoom && !backdrop ? Math.Clamp(Config.ZoomFactor, 1, 2) : 1;
        s.ShowLabels = Config.ShowLabels && !backdrop;
        s.EdgeGap = backdrop ? 0 : (Reserve ? 4 : 8);
        if (!AutoHideActive) s.SetHidden(false, animate: false);
    }

    private void Place()
    {
        if (_window is null) return;
        _lastPlacement = _window.Place(TargetMonitor(), ParseEdge(Config.Edge), Reserve);
        PlaceIslands();
    }

    /// <summary>Reaplica a configuração (chamado pelo menu e pelas configurações).</summary>
    public void ApplyConfig()
    {
        SaveChanges(); // antes de redesenhar: um erro ao desenhar não pode perder a mudança
        if (!Config.Enabled) { Close(); return; }
        if (_window is not null && _window.UsesBackdrop != (Config.Backdrop is "acrilico" or "desfoque"))
        {
            Close(); // Mica/Acrílico muda o tipo de janela: recria
        }
        if (_window is null) { Show(); }
        ApplySurfaceSettings();
        _layoutKey = "";
        _icons.Clear();
        Rebuild();
        ShowIslands(); // itens, borda ou estilo podem ter mudado
        if (AutoHideActive) { _hideTimer.Stop(); _hideTimer.Start(); }
    }

    private void OnThemeChanged()
    {
        _window?.ApplyBackdrop(_host.Themes.Current.Panel);
        _window?.Surface.InvalidateVisual();
        foreach (var w in new[] { _leftIsland, _rightIsland })
        {
            if (w is null || _window is null) continue;
            w.ApplyBackdrop(_window.UsesBackdrop, _host.Themes.Current.Panel, Config.Backdrop);
        }
        PlaceIslands(); // a fonte do tema muda a largura dos textos
    }

    private bool OnControlMessage(int msg, nint wParam, nint lParam)
    {
        const int SPI_SETWORKAREA = 0x2F; // a área de trabalho mudou (ex.: barra do Windows oculta/mostrada)
        if (msg == ControlWindow.TaskbarCreatedMessage) _window?.OnShellRestarted();
        if (msg == Win32.WM_DISPLAYCHANGE || msg == ControlWindow.TaskbarCreatedMessage || (msg == Win32.WM_SETTINGCHANGE && wParam == SPI_SETWORKAREA))
            _host.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { _layoutKey = ""; Rebuild(); });
        return false;
    }

    // ---------- ações ----------

    private void OnItemClicked(int index, MouseButton button)
    {
        if (index < 0 || index >= _entries.Count) return;
        var e = _entries[index];
        if (e.IsSearch) { ToggleSearch(); return; } // pesquisa do Aurora, acima do dock
        var windows = LiveWindows(e);
        if (button == MouseButton.Middle || windows.Count == 0) { Launch(e); return; }

        nint fg = Win32.GetForegroundWindow();
        int current = windows.FindIndex(w => w.Handle == fg);
        if (windows.Count == 1 && current == 0 && !Win32.IsIconic(fg))
            Win32.ShowWindow(fg, Win32.SW_MINIMIZE); // clicar no app ativo minimiza
        else
            Activate(windows[(current + 1) % windows.Count].Handle); // senão, traz (ou alterna entre janelas)
    }

    /// <summary>Janelas da entrada que ainda existem de verdade (tira as "fantasma" antes de agir).</summary>
    private List<WindowRecord> LiveWindows(DockEntry e)
    {
        if (_running is null || e.Windows.Count == 0) return [];
        _running.Prune(); // se algo mudou, o dock é redesenhado logo depois
        return e.Windows.Where(w => _running.Contains(w.Handle)).ToList();
    }

    private static void Activate(nint hwnd)
    {
        if (Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        Win32.SetForegroundWindow(hwnd);
    }

    private void Launch(DockEntry e)
    {
        if (e.Pinned is { IsGroup: true }) { ToggleGroup(_entries.IndexOf(e)); return; }
        LaunchItem(e.Pinned, e.Identity.ExePath, e.Name);
    }

    /// <summary>Abre um atalho fixado (ou, sem item, o executável de um app em execução).</summary>
    internal void LaunchItem(DockItem? item, string? exePath, string name)
    {
        // App da Store cujo exe sumiu numa atualização: resolve o AppUserModelID na hora.
        if (item is { Kind: "arquivo" } && PackagedApps.FamilyName(item.Path) is not null && !File.Exists(item.Path))
        {
            try
            {
                var apps = Task.Run(() => ShellInterop.ListStartApps().Select(a => a.ParsingName).ToList()).GetAwaiter().GetResult();
                if (PackagedApps.Migrate([item], apps)) { _resolved.Clear(); Changed(); }
            }
            catch (Exception ex) { Log.Error("Não foi possível listar os apps do menu Iniciar", ex); }
        }
        try
        {
            ProcessStartInfo psi = item?.Kind switch
            {
                "app" => new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + item.Path),
                "pasta" => new ProcessStartInfo("explorer.exe", $"\"{Environment.ExpandEnvironmentVariables(item.Path)}\""),
                null => new ProcessStartInfo(exePath ?? throw new InvalidOperationException("sem executável")),
                _ => new ProcessStartInfo(Environment.ExpandEnvironmentVariables(item.Path), item.Arguments ?? ""),
            };
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível abrir '{name}': {ex.Message}");
        }
    }

    private void OnItemMoved(int from, int to)
    {
        if (from < 0 || from >= _entries.Count || _entries[from].Pinned is not { } item) return;
        int target = Math.Clamp(PinnedIndexAt(to), 0, Config.Items.Count - 1);
        Config.Items.Remove(item);
        Config.Items.Insert(Math.Min(target, Config.Items.Count), item);
        Changed();
    }

    private void OnItemDraggedOut(int index)
    {
        if (index >= 0 && index < _entries.Count && _entries[index].Pinned is { } item) Unpin(item);
    }

    private void OnFilesDropped(string[] files, int index)
    {
        int at = PinnedIndexAt(index);
        foreach (var f in files)
        {
            bool folder = Directory.Exists(f);
            if (!folder && !File.Exists(f)) continue;
            Config.Items.Insert(Math.Min(at++, Config.Items.Count), new DockItem { Kind = folder ? "pasta" : "arquivo", Path = f });
        }
        Changed();
    }

    /// <summary>Converte um índice de entrada exibida em índice na lista de fixados.</summary>
    private int PinnedIndexAt(int entryIndex) => _entries.Take(Math.Max(0, entryIndex)).Count(e => e.Pinned is not null && Config.Items.Contains(e.Pinned));

    private void Pin(DockEntry e)
    {
        var id = e.Identity;
        var item = IconSourceFor(id) is { } src && src.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)
            ? new DockItem { Kind = "app", Path = id.AppId!, Name = e.Name }
            : new DockItem { Kind = "arquivo", Path = id.ExePath ?? "", Name = e.Name };
        if (item.Path.Length == 0) return;
        Config.Items.Add(item);
        Changed(); // app da Store fixado pelo exe (ex.: Claude) vira AppUserModelID ao gravar
    }

    private bool _migratingPackaged;
    /// <summary>Caminhos já procurados no menu Iniciar sem sucesso: não refaz a busca a cada gravação.</summary>
    private readonly HashSet<string> _packagedTried = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Apps da Store fixados pelo caminho do exe (com a versão no nome da pasta) passam a ser
    /// abertos pelo AppUserModelID, que sobrevive às atualizações. A lista do menu Iniciar é lida
    /// fora da interface (~0,5 s) e só quando há algo para converter.
    /// </summary>
    private void MigratePackagedApps()
    {
        if (_migratingPackaged) return;
        var pending = PackagedApps.Candidates(Config.Items).Where(p => !_packagedTried.Contains(p)).ToList();
        if (pending.Count == 0) return;
        _migratingPackaged = true;
        Task.Run(() => ShellInterop.ListStartApps().Select(a => a.ParsingName).ToList()).ContinueWith(t =>
        {
            _migratingPackaged = false;
            if (t.IsFaulted) { Log.Error("Não foi possível listar os apps do menu Iniciar", t.Exception); return; }
            bool changed = PackagedApps.Migrate(Config.Items, t.Result);
            _packagedTried.UnionWith(PackagedApps.Candidates(Config.Items)); // os que sobraram não estão no menu Iniciar
            if (!changed) return;
            Log.Info("Apps da Store fixados pelo caminho passaram a abrir pelo AppUserModelID");
            _resolved.Clear();
            Changed();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Unpin(DockItem item)
    {
        Config.Items.Remove(item);
        _resolved.Remove(item);
        Changed();
    }

    private void Changed()
    {
        SaveChanges();
        _layoutKey = "";
        Rebuild();
    }

    /// <summary>Guarda uma cópia dos atalhos novos (.lnk/.url) e grava a configuração.</summary>
    private void SaveChanges()
    {
        if (_shortcuts.KeepAll(Config.Items)) _resolved.Clear(); // caminhos mudaram: resolve de novo
        _host.SaveConfig();
        MigratePackagedApps(); // qualquer atalho novo de app da Store (dock, grupo, configurações)
    }

    // ---------- menus ----------

    private void OnContextRequested(int index)
    {
        if (_window is null) return;
        _hideTimer.Stop();
        var menu = new NativeMenu();
        if (index >= 0 && index < _entries.Count && _entries[index].IsSearch)
        {
            menu.Item("Pesquisar", ToggleSearch)
                .Item("Abrir a pesquisa do Windows", () => Win32.SendWinShortcut('S'))
                .Item("Ocultar o botão de pesquisa", () => { Config.SearchButton = false; Changed(); })
                .Separator();
        }
        else if (index >= 0 && index < _entries.Count && _entries[index].Pinned is { IsGroup: true } group)
        {
            int gi = index;
            menu.Item(group.Name ?? "Grupo", () => { }, enabled: false).Separator()
                .Item("Abrir grupo", () => ToggleGroup(gi))
                .Item("Renomear grupo", () => ToggleGroup(gi, rename: true))
                .Item("Adicionar atalhos…", () => AddToGroupWithDialog(group))
                .Item("Desagrupar (atalhos voltam ao dock)", () => { DockGroups.Ungroup(Config.Items, group); Changed(); })
                .Item("Remover grupo e atalhos", () => Unpin(group))
                .Separator();
        }
        else if (index >= 0 && index < _entries.Count && !_entries[index].IsSeparator)
        {
            var e = _entries[index];
            menu.Item(e.Name, () => { }, enabled: false).Separator();
            var windows = LiveWindows(e);
            menu.Item(windows.Count > 0 ? "Abrir nova janela" : "Abrir", () => Launch(e));
            if (windows.Count > 0)
            {
                var list = new NativeMenu();
                foreach (var w in windows)
                {
                    nint h = w.Handle;
                    string title = Win32.WindowTitle(h);
                    list.Item(title.Length > 60 ? title[..60] + "…" : title.Length == 0 ? e.Name : title, () => Activate(h));
                }
                menu.Submenu($"Janelas ({windows.Count})", list);
                menu.Item(windows.Count > 1 ? $"Fechar {windows.Count} janelas" : "Fechar janela", () =>
                {
                    foreach (var w in windows) Win32.PostMessage(w.Handle, Win32.WM_CLOSE, 0, 0);
                });
            }
            if (e.Pinned is { } pinned)
            {
                menu.Item("Desafixar do dock", () => Unpin(pinned));
                var move = new NativeMenu();
                foreach (var g in DockGroups.Groups(Config.Items))
                {
                    var target = g;
                    move.Item(g.Name ?? "Grupo", () => { DockGroups.Add(Config.Items, target, pinned); Changed(); });
                }
                move.Item("Novo grupo com este atalho", () => { DockGroups.Create(Config.Items, "Grupo", pinned); Changed(); });
                menu.Submenu("Mover para o grupo", move);
            }
            else menu.Item("Fixar no dock", () => Pin(e));
            string? location = e.Pinned?.Kind is "arquivo" or "pasta" ? Environment.ExpandEnvironmentVariables(e.Pinned.Path) : e.Pinned is null ? e.Identity.ExePath : null;
            if (location is not null && (File.Exists(location) || Directory.Exists(location)))
                menu.Item("Abrir local do arquivo", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{location}\"") { UseShellExecute = true }));
            menu.Separator();
        }
        else if (index >= 0 && _entries[index].Pinned is { } sep)
        {
            menu.Item("Remover separador", () => Unpin(sep)).Separator();
        }
        menu.Item("Criar grupo de atalhos", () =>
        {
            var g = DockGroups.Create(Config.Items, "Grupo");
            Changed();
            ToggleGroup(_entries.FindIndex(x => ReferenceEquals(x.Pinned, g)), rename: true);
        });
        menu.Item("Adicionar separador", () => { Config.Items.Add(new DockItem { Kind = "separador" }); Changed(); });
        menu.Submenu("Configurações do dock", SettingsMenu());
        // Com a barra do Windows oculta, o ícone da bandeja some junto: o dock dá acesso ao app.
        menu.Separator()
            .Item("Ocultar a barra de tarefas do Windows", () => _host.SetHideTaskbar(!_host.Config.HideTaskbar), isChecked: _host.Config.HideTaskbar)
            .Item("Configurações do Aurora Desk", _host.OpenSettings)
            .Item("Sair do Aurora Desk", _host.Exit);
        menu.Show(_window.Handle, _host.Themes.Current.Dark);
        if (AutoHideActive && !_window.IsMouseOver) _hideTimer.Start();
    }

    /// <summary>Opções do dock (usado aqui e no menu da bandeja).</summary>
    public NativeMenu SettingsMenu()
    {
        void Set(Action change) { change(); ApplyConfig(); }

        var edge = new NativeMenu();
        foreach (var (key, label) in new[] { ("inferior", "Inferior"), ("superior", "Superior"), ("esquerda", "Esquerda"), ("direita", "Direita") })
            edge.Item(label, () => Set(() => Config.Edge = key), isChecked: Config.Edge == key);

        var size = new NativeMenu();
        foreach (int px in new[] { 32, 40, 44, 52, 64 })
            size.Item($"{px} px", () => Set(() => Config.IconSize = px), isChecked: Config.IconSize == px);

        var backdrop = new NativeMenu()
            .Item("Cor do tema", () => Set(() => Config.Backdrop = "nenhum"), isChecked: Config.Backdrop is not ("acrilico" or "desfoque"))
            .Item("Acrílico (sem ampliação)", () => Set(() => Config.Backdrop = "acrilico"), isChecked: Config.Backdrop == "acrilico")
            .Item("Desfoque (sem ampliação)", () => Set(() => Config.Backdrop = "desfoque"), isChecked: Config.Backdrop == "desfoque");

        var monitors = new NativeMenu().Item("Principal", () => Set(() => Config.Monitor = null), isChecked: string.IsNullOrEmpty(Config.Monitor));
        foreach (var m in Win32.Monitors())
        {
            string device = Win32.DeviceName(Win32.MonitorInfo(m));
            monitors.Item(device.Replace(@"\\.\", ""), () => Set(() => Config.Monitor = device), isChecked: device == Config.Monitor);
        }

        return new NativeMenu()
            .Submenu("Posição", edge)
            .Submenu("Monitor", monitors)
            .Submenu("Tamanho dos ícones", size)
            .Submenu("Fundo", backdrop)
            .Item(_host.Taskbar.IsActive ? "Reservar espaço na tela (barra do Windows oculta)" : "Reservar espaço na tela", () => Set(() => Config.SetReserve(_host.Taskbar.IsActive, !Reserve)), isChecked: Reserve)
            .Item("Ocultar automaticamente", () => Set(() => Config.AutoHide = !Config.AutoHide), isChecked: Config.AutoHide, enabled: !Reserve)
            .Item("Ampliar ao passar o mouse", () => Set(() => Config.Zoom = !Config.Zoom), isChecked: Config.Zoom)
            .Item("Mostrar nomes ao passar o mouse", () => Set(() => Config.ShowLabels = !Config.ShowLabels), isChecked: Config.ShowLabels)
            .Item("Mostrar apps abertos não fixados", () => Set(() => Config.ShowRunning = !Config.ShowRunning), isChecked: Config.ShowRunning)
            .Item("Botão de pesquisa", () => Set(() => Config.SearchButton = !Config.SearchButton), isChecked: Config.SearchButton);
    }

    // ---------- itens padrão ----------

    internal static IEnumerable<DockItem> DefaultItems()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");

        yield return new DockItem { Kind = "arquivo", Path = Path.Combine(win, "explorer.exe"), Name = "Explorador de Arquivos" };
        foreach (var (path, name) in new[]
        {
            (Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), "Microsoft Edge"),
            (Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), "Google Chrome"),
        })
            if (File.Exists(path)) yield return new DockItem { Kind = "arquivo", Path = path, Name = name };

        foreach (var (family, aumid, name) in new[]
        {
            ("Microsoft.WindowsTerminal_8wekyb3d8bbwe", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Terminal"),
            ("Microsoft.WindowsNotepad_8wekyb3d8bbwe", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "Bloco de Notas"),
        })
            if (Directory.Exists(Path.Combine(apps, family))) yield return new DockItem { Kind = "app", Path = aumid, Name = name };

        yield return new DockItem { Kind = "separador" };
        yield return new DockItem { Kind = "app", Path = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", Name = "Configurações" };
    }

    public void Dispose()
    {
        _hideTimer.Stop();
        _host.Themes.Changed -= OnThemeChanged;
        _host.Control.Message -= OnControlMessage;
        Close();
        _running?.Dispose();
    }
}

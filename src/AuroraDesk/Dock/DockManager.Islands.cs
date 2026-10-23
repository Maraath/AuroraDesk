using System.Diagnostics;
using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Measures;
using AuroraDesk.Interop;

namespace AuroraDesk.Dock;

/// <summary>
/// Estilo "três ilhas": estatísticas à esquerda, apps no centro (o dock de sempre) e bandeja +
/// relógio à direita. As ilhas usam as medidas do agendador central e só existem nas bordas
/// inferior e superior.
/// </summary>
internal sealed partial class DockManager
{
    private IslandWindow? _leftIsland, _rightIsland;
    /// <summary>Quadro do jogador da skin, solto ao lado da ilha esquerda.</summary>
    private IslandWindow? _frameIsland;
    private readonly List<IslandItem> _leftItems = [], _rightItems = [], _frameItems = [];
    private readonly List<MeasureSpec> _islandSpecs = [];
    private readonly HashSet<Measure> _islandMeasures = [];
    private (Win32.RECT Area, double Scale) _lastPlacement;

    /// <summary>As ilhas valem agora? (opção ligada, dock visível e borda horizontal)</summary>
    private bool IslandsActive => Config.Islands && _window is not null && Config.Edge is "inferior" or "superior";

    /// <summary>As ilhas precisam de medidas (para o agendador não pausar).</summary>
    public bool NeedsMeasures => IslandsActive && (_leftItems.Count > 0 || _rightItems.Count > 0 || _frameItems.Count > 0);

    private void ShowIslands()
    {
        CloseIslands();
        if (!IslandsActive) { _host.UpdateMeasureDemand(); return; }

        BuildItems();
        foreach (var item in _leftItems.Concat(_rightItems).Concat(_frameItems))
        {
            item.Measures.Clear();
            foreach (var spec in item.Specs)
            {
                var m = _host.Scheduler.Subscribe(spec);
                _islandSpecs.Add(spec);
                _islandMeasures.Add(m);
                item.Measures.Add(m);
            }
        }
        _host.Scheduler.Changed += OnIslandMeasures;

        _leftIsland = CreateIsland(alignRight: false, _leftItems);
        _rightIsland = CreateIsland(alignRight: true, _rightItems);
        _frameIsland = CreateIsland(alignRight: false, _frameItems, bare: true);
        _host.UpdateMeasureDemand();
        PlaceIslands();
    }

    private IslandWindow? CreateIsland(bool alignRight, List<IslandItem> items, bool bare = false)
    {
        if (items.Count == 0) return null;
        var w = new IslandWindow(_host.Themes, alignRight);
        w.Surface.Skin = _window!.UsesBackdrop ? null : _skin;
        w.Surface.Bare = bare;
        w.Surface.SetItems(items);
        w.Surface.RightClicked += () => OnContextRequested(-1);
        w.Surface.MouseEnter += (_, _) => _hideTimer.Stop();
        w.Surface.MouseLeave += (_, _) => { if (AutoHideActive) { _hideTimer.Stop(); _hideTimer.Start(); } };
        w.Show();
        w.ApplyBackdrop(_window!.UsesBackdrop, _host.Themes.Current.Panel, Config.Backdrop);
        return w;
    }

    private void CloseIslands()
    {
        if (_islandSpecs.Count > 0)
        {
            _host.Scheduler.Changed -= OnIslandMeasures;
            foreach (var spec in _islandSpecs) _host.Scheduler.Unsubscribe(spec);
            _islandSpecs.Clear();
            _islandMeasures.Clear();
        }
        _leftIsland?.Close();
        _rightIsland?.Close();
        _frameIsland?.Close();
        _leftIsland = _rightIsland = _frameIsland = null;
        _leftItems.Clear();
        _rightItems.Clear();
        _frameItems.Clear();
    }

    private void SetIslandsVisible(bool visible)
    {
        foreach (var w in new[] { _leftIsland, _rightIsland, _frameIsland })
        {
            if (w is null) continue;
            if (visible && !w.IsVisible) { w.Show(); PlaceIslands(); }
            else if (!visible && w.IsVisible)
            {
                w.AllowHide = true;
                w.Hide();
                w.AllowHide = false;
            }
        }
    }

    private void OnIslandMeasures(IReadOnlyList<Measure> changed)
    {
        if (!changed.Any(_islandMeasures.Contains)) return;
        double left = _leftIsland?.Surface.RequiredWidth() ?? 0, right = _rightIsland?.Surface.RequiredWidth() ?? 0;
        _leftIsland?.Surface.InvalidateVisual();
        _rightIsland?.Surface.InvalidateVisual();
        _frameIsland?.Surface.InvalidateVisual();
        // Um item pode aparecer/sumir ou crescer (ex.: texto do clima): só então a janela muda.
        if (Math.Abs(left - _leftIsland?.ActualWidth ?? 0) > 0.5 || Math.Abs(right - _rightIsland?.ActualWidth ?? 0) > 0.5) PlaceIslands();
    }

    /// <summary>Alinha as ilhas ao painel do dock: mesma altura, mesma distância da borda.</summary>
    private void PlaceIslands()
    {
        if (_window is null || _lastPlacement.Scale <= 0) return;
        var (area, scale) = _lastPlacement;
        var center = _window.Surface;
        bool bottom = Config.Edge != "superior";
        bool backdrop = _window.UsesBackdrop;
        int margin = (int)Math.Round(8 * scale);

        int leftEnd = area.Left;
        foreach (var (w, right) in new[] { (_leftIsland, false), (_rightIsland, true), (_frameIsland, false) })
        {
            if (w is null) continue;
            var s = w.Surface;
            s.PanelThickness = center.PanelThickness;
            s.EdgeGap = backdrop ? 0 : center.EdgeGap;
            s.Bottom = bottom;
            int width = (int)Math.Ceiling(s.RequiredWidth() * scale);
            int height = (int)Math.Ceiling((backdrop ? center.PanelThickness : s.RequiredHeight) * scale);
            if (width <= 0) { w.Hide(); continue; }
            // O quadro solto vem logo depois da ilha esquerda (ou na borda, sem ela).
            int x = right ? area.Right - margin - width : ReferenceEquals(w, _frameIsland) ? leftEnd + margin : area.Left + margin;
            int y = bottom ? area.Bottom - height : area.Top;
            w.SetBounds(x, y, width, height);
            if (ReferenceEquals(w, _leftIsland)) leftEnd = x + width;
            s.InvalidateVisual();
        }
    }

    // ---------- itens ----------

    private void BuildItems()
    {
        _leftItems.Clear();
        _rightItems.Clear();
        static MeasureSpec S(string type, params (string, string)[] o) => new(type, o.ToDictionary(p => p.Item1, p => p.Item2));
        var battery = S("bateria", ("modo", "nivel"));

        // Skin com quadro do jogador: CPU e RAM viram as barras de vida e mana ao lado do retrato.
        // Com o quadro inteiro (imagem), ele fica solto ao lado da ilha, maior, e leva também o disco
        // na placa do nome; a ilha esquerda fica com o resto (ex.: o clima).
        bool unitFrame = _skin?.Skin.UnitFrame is not null && !_window!.UsesBackdrop
            && (Config.LeftItems.Contains("cpu") || Config.LeftItems.Contains("ram"));
        bool separate = unitFrame && _skin!.Skin.UnitFrame!.HasFullFrame;
        var diskSpec = S("disco", ("unidade", "C:"), ("modo", "livre"));
        if (separate)
            _frameItems.Add(new UnitFrameItem(S("cpu"), S("memoria"), Config.LeftItems.Contains("disco") ? diskSpec : null, OpenTaskManager) { Large = true });
        bool unitFrameAdded = separate;
        foreach (var key in Core.Config.DockConfig.AllLeftItems.Where(k => Config.LeftItems.Contains(k)))
        {
            if (unitFrame && key is "cpu" or "ram" || separate && key == "disco")
            {
                if (!unitFrameAdded) _leftItems.Add(new UnitFrameItem(S("cpu"), S("memoria"), null, OpenTaskManager));
                unitFrameAdded = true;
                continue;
            }
            IslandItem? item = key switch
            {
                "cpu" => new StatItem("cpu", "CPU", S("cpu"), "100%", bar: true, m => $"{m.Value:0}%", OpenTaskManager, "Uso do processador · clique para abrir o Gerenciador de Tarefas"),
                "ram" => new StatItem("ram", "RAM", S("memoria"), "100%", bar: true, m => $"{m.Value:0}%", OpenTaskManager, "Memória em uso · clique para abrir o Gerenciador de Tarefas"),
                "disco" => new StatItem("disco", "Disco C:", S("disco", ("unidade", "C:"), ("modo", "livre")), "999 GB livres", bar: false, m => m.Text + " livres", () => Open("explorer.exe", "C:\\")),
                "rede" => new StatItem("rede", "Rede", S("rede", ("direcao", "download")), "↓ 999,9 KB/s", bar: false, m => "↓ " + m.Text, () => Open("ms-settings:network-status"), "Download atual"),
                "clima" => new WeatherItem(() => _host.Config.Weather.City, () => _host.Config.Weather.Enabled),
                "bateria" => new StatItem("bateria", "Bateria", battery, "100%", bar: true, m => $"{m.Value:0}%")
                { IsVisible = m => m is BatteryMeasure { HasBattery: true } },
                _ => null,
            };
            if (item is not null) _leftItems.Add(item);
        }

        if (Config.TrayOverflow)
            _rightItems.Add(new IconButtonItem("bandeja", "", "Ícones ocultos da bandeja (outros apps)", OpenHiddenTrayIcons));

        foreach (var key in Core.Config.DockConfig.AllRightItems.Where(k => Config.RightItems.Contains(k)))
        {
            IslandItem? item = key switch
            {
                "aurora" => new AuroraItem(_host.ShowMainMenu),
                "conexao" => new GlyphItem("conexao", S("conexao"),
                    m => (m as ConnectionMeasure)?.Kind switch { ConnectionKind.Wifi => "\uE701", ConnectionKind.Wired => "\uE839", _ => "\uF384" },
                    _ => null, m => m?.Text, QuickSettings),
                "volume" => new GlyphItem("volume", S("volume"),
                    m => m is VolumeMeasure { Muted: true } || m?.Value == 0 ? "\uE74F" : m?.Value < 34 ? "\uE993" : m?.Value < 67 ? "\uE994" : "\uE995",
                    _ => null, m => m is VolumeMeasure { Available: true } v ? (v.Muted ? "Volume: mudo" : $"Volume: {v.Value:0}%") + " · role o mouse para ajustar" : "Sem saída de áudio",
                    QuickSettings)
                { OnWheel = ChangeVolume },
                "bateria" => new GlyphItem("bateria", battery,
                    m => m is BatteryMeasure { Charging: true } ? "\uEBB5" : ((char)(0xEBA0 + (int)Math.Round((m?.Value ?? 100) / 10))).ToString(),
                    m => $"{m?.Value ?? 0:0}%", m => m is BatteryMeasure b ? $"Bateria: {b.Value:0}% · {Strings.BatteryState(b.HasBattery, b.PluggedIn, b.Charging)}" : null,
                    QuickSettings)
                { IsVisible = m => m is BatteryMeasure { HasBattery: true } },
                "relogio" => new ClockItem(() => Win32.SendWinShortcut('N')),
                _ => null,
            };
            if (item is not null) _rightItems.Add(item);
        }
        // Win+D: o próprio Windows minimiza tudo e, no clique seguinte, devolve as janelas.
        if (Config.ShowDesktopButton) _rightItems.Add(new ShowDesktopItem(() => Win32.SendWinShortcut('D')));
    }

    private static void QuickSettings() => Win32.SendWinShortcut('A');

    // ---------- grupos de atalhos ----------

    private GroupWindow? _groupWindow;
    private Core.Config.DockItem? _openGroup;

    /// <summary>Abre (ou fecha, se for o mesmo) o painel do grupo no índice <paramref name="index"/>.</summary>
    internal void ToggleGroup(int index, bool rename = false)
    {
        if (_window is null || index < 0 || index >= _entries.Count || _entries[index].Pinned is not { IsGroup: true } group) return;
        bool same = ReferenceEquals(_openGroup, group);
        _groupWindow?.Close();
        if (same && !rename) return;
        if (_window.Surface.ItemScreenRect(index) is not { } rect) return;

        int px = IconPixels();
        var w = new GroupWindow(_host.Themes, group, item =>
        {
            var (_, name, icon) = Resolve(item);
            return (name, icon is null ? null : _icons.Get(icon, px));
        });
        w.Launch += item => LaunchItem(item, null, Resolve(item).Item2);
        w.RemoveItem += item => { Core.Config.DockGroups.Remove(Config.Items, group, item, toDock: false); Changed(); };
        w.MoveToDock += item => { Core.Config.DockGroups.Remove(Config.Items, group, item, toDock: true); Changed(); };
        w.FilesAdded += files => { AddFilesToGroup(group, files); };
        w.Renamed += name => { group.Name = name; _resolved.Remove(group); Changed(); };
        w.AddRequested += () => AddToGroupWithDialog(group);
        w.Closed += (_, _) =>
        {
            if (ReferenceEquals(_groupWindow, w)) { _groupWindow = null; _openGroup = null; }
            _host.Trimmer.Schedule(TimeSpan.FromSeconds(3));
        };
        _groupWindow = w;
        _openGroup = group;
        w.ShowNear(rect, Config.Edge != "superior", Win32.MonitorScale(TargetMonitor()));
        if (rename) w.StartRename();
    }

    private void AddFilesToGroup(Core.Config.DockItem group, string[] files)
    {
        foreach (var f in files)
        {
            bool folder = Directory.Exists(f);
            if (!folder && !File.Exists(f)) continue;
            Core.Config.DockGroups.Add(Config.Items, group, new Core.Config.DockItem { Kind = folder ? "pasta" : "arquivo", Path = f });
        }
        Changed();
        _groupWindow?.Refresh();
    }

    private void AddToGroupWithDialog(Core.Config.DockItem group)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Adicionar atalhos ao grupo \"{group.Name}\"", Multiselect = true, DereferenceLinks = false,
            Filter = "Programas e atalhos|*.exe;*.lnk;*.url|Todos os arquivos|*.*",
        };
        if (dialog.ShowDialog() == true) AddFilesToGroup(group, dialog.FileNames);
    }

    private void OnFilesDroppedOnGroup(string[] files, int index)
    {
        if (index >= 0 && index < _entries.Count && _entries[index].Pinned is { IsGroup: true } group) AddFilesToGroup(group, files);
    }

    private void OnItemDroppedOnGroup(int from, int groupIndex)
    {
        if (from < 0 || groupIndex < 0 || from >= _entries.Count || groupIndex >= _entries.Count) return;
        if (_entries[groupIndex].Pinned is not { IsGroup: true } group) return;
        if (_entries[from].Pinned is { } item) { Core.Config.DockGroups.Add(Config.Items, group, item); Changed(); }
        else if (_entries[from].Identity.ExePath is { } exe)
        {
            // App aberto mas não fixado: entra no grupo direto.
            Core.Config.DockGroups.Add(Config.Items, group, new Core.Config.DockItem { Kind = "arquivo", Path = exe, Name = _entries[from].Name });
            Changed();
        }
    }

    private Search.SearchWindow? _search;

    /// <summary>Abre (ou fecha, se já aberta) a pesquisa do Aurora logo acima do painel do dock.</summary>
    public void ToggleSearch()
    {
        if (_search is not null) { _search.Close(); return; }
        if (_window is null) return;
        Win32.GetWindowRect(_window.Handle, out var r);
        double scale = Win32.MonitorScale(TargetMonitor());
        var s = _window.Surface;
        bool bottom = Config.Edge != "superior";
        int gap = (int)Math.Round(12 * scale);
        // Borda do painel do dock voltada para o meio da tela.
        int panelEdge = bottom
            ? r.Bottom - (int)Math.Round((s.EdgeGap + s.PanelThickness) * scale)
            : r.Top + (int)Math.Round((s.EdgeGap + s.PanelThickness) * scale);
        _search = new Search.SearchWindow(_host.Themes, (path, px) => _icons.Get(path, px), Config.SearchEngine);
        _search.Closed += (_, _) =>
        {
            _search = null;
            _host.Trimmer.Schedule(TimeSpan.FromSeconds(3));
        };
        _search.ShowNearDock((r.Left + r.Right) / 2, bottom ? panelEdge - gap : panelEdge + gap, bottom, scale);
    }

    /// <summary>
    /// Abre o painel nativo de ícones ocultos da bandeja: Win+B põe o foco na seta da bandeja do
    /// Windows e Enter a abre. Os ícones continuam sendo do Windows (clique direito, menus e
    /// arrastar funcionam), sem o app se passar pela barra de tarefas.
    /// </summary>
    private void OpenHiddenTrayIcons()
    {
        Win32.SendWinShortcut('B');
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, _host.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { timer.Stop(); Win32.SendKey(0x0D); };
        timer.Start();
    }

    private static void OpenTaskManager() => Open("taskmgr.exe");

    private static void Open(string target, string? args = null)
    {
        try { Process.Start(new ProcessStartInfo(target, args ?? "") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Não foi possível abrir '{target}': {ex.Message}"); }
    }

    private void ChangeVolume(int wheelDelta)
    {
        if (Core.Native.Audio.Change(wheelDelta > 0 ? 0.02f : -0.02f))
            _host.Scheduler.RefreshNow("volume");
    }
}

/// <summary>Clima na ilha esquerda: cidade como rótulo, "31° Nublado" como valor.</summary>
internal sealed class WeatherItem(Func<string> city, Func<bool> enabled) : IslandItem("clima")
{
    private static readonly MeasureSpec Temp = new("clima", new Dictionary<string, string> { ["campo"] = "temperatura" });
    private static readonly MeasureSpec Desc = new("clima", new Dictionary<string, string> { ["campo"] = "descricao" });

    public override IEnumerable<MeasureSpec> Specs => [Temp, Desc];
    public override bool Visible => enabled();
    public override string? Tooltip => $"Clima em {city()} (Open-Meteo)";

    private string Value => M(0) is { } t && t.Text != "--" ? $"{t.Text}° {M(1)?.Text}" : "--";

    public override double Width(IslandContext ctx) =>
        Math.Ceiling(Math.Min(170, Math.Max(ctx.Text(city(), 11, ctx.SecondaryColor).Width, ctx.Text(Value, 13, ctx.TextColor).Width)));

    public override void Draw(System.Windows.Media.DrawingContext dc, IslandContext ctx, System.Windows.Rect area)
    {
        var label = ctx.Text(city(), 11, ctx.SecondaryColor);
        var value = ctx.Text(Value, 13, ctx.TextColor);
        value.MaxTextWidth = Math.Max(1, area.Width);
        value.MaxLineCount = 1;
        value.Trimming = System.Windows.TextTrimming.CharacterEllipsis;
        double y = area.Y + (area.Height - label.Height - value.Height - 6) / 2; // alinhado aos itens com barra
        dc.DrawText(label, new System.Windows.Point(area.X, y));
        dc.DrawText(value, new System.Windows.Point(area.X, y + label.Height - 1));
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Skins;
using AuroraDesk.Core.Theming;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;
using Microsoft.Win32;

namespace AuroraDesk.Settings;

/// <summary>
/// Janela de configurações. Criada só quando aberta e descartada ao fechar (o host libera a
/// memória em seguida). Visual Fluent do WPF em modo escuro; páginas montadas sob demanda.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly ContentControl _content = new();
    private readonly ListBox _nav = new();
    private DispatcherTimer? _perfTimer;
    private IReadOnlyList<(string Name, bool IsPrivate)>? _fonts;

    private AppConfig Config => _host.Config;

    public SettingsWindow(AppHost host)
    {
        _host = host;
#pragma warning disable WPF0001 // ThemeMode (Fluent) ainda marcado como experimental
        ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
        Title = "Aurora Desk — Configurações";
        Width = 940;
        Height = 660;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.WindowBackground;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var side = new DockPanel { Margin = new Thickness(12, 16, 8, 16) };
        var brand = new TextBlock { Text = "Aurora Desk", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 16) };
        DockPanel.SetDock(brand, System.Windows.Controls.Dock.Top);
        side.Children.Add(brand);
        _nav.Background = Brushes.Transparent;
        _nav.BorderThickness = new Thickness(0);
        foreach (var (icon, name) in new[] { ("\uE7F4", "Widgets"), ("\uE8A9", "Dock"), ("\uE790", "Temas"), ("\uE8D2", "Fontes"), ("\uE9D9", "Desempenho"), ("\uE713", "Geral") })
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(new TextBlock { Text = icon, FontFamily = Ui.Icons, FontSize = 16, Width = 28, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            _nav.Items.Add(new ListBoxItem { Content = p, Tag = name, Padding = new Thickness(10, 8, 10, 8) });
        }
        _nav.SelectionChanged += (_, _) => ShowPage((_nav.SelectedItem as ListBoxItem)?.Tag as string ?? "Widgets");
        side.Children.Add(_nav);
        grid.Children.Add(side);

        var scroll = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24, 20, 28, 24) };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;

        _nav.SelectedIndex = 0;
        Closed += (_, _) => _perfTimer?.Stop();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        RenderModes.Apply(this);
        unsafe
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        }
    }

    public void ShowPage(string name)
    {
        _perfTimer?.Stop();
        try
        {
            _content.Content = name switch
            {
                "Dock" => DockPage(),
                "Temas" => ThemesPage(),
                "Fontes" => FontsPage(),
                "Desempenho" => PerformancePage(),
                "Geral" => GeneralPage(),
                _ => WidgetsPage(),
            };
        }
        catch (Exception ex)
        {
            Core.Logging.Log.Error($"Página '{name}' falhou", ex);
            _content.Content = Ui.Note("Não foi possível montar esta página: " + ex.Message, Ui.Warning);
        }
    }

    private void Refresh() => ShowPage((_nav.SelectedItem as ListBoxItem)?.Tag as string ?? "Widgets");

    // ---------- Widgets ----------

    private UIElement WidgetsPage()
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Header("Widgets"));
        page.Children.Add(Ui.Card(
            Ui.Row("Mostrar widgets", Ui.Toggle(Config.WidgetsVisible, v => _host.Widgets.SetVisible(v)), "Oculta todos sem desligá-los (as medidas também pausam)."),
            Ui.Buttons(
                Ui.Button("Abrir pasta de widgets", () => AppHost.OpenFolder(_host.Widgets.Folder)),
                Ui.Button("Recarregar todos", _host.Widgets.ReloadAll),
                Ui.Button("Reinstalar exemplos ausentes", () => { SkinLibrary.InstallSamples(_host.Widgets.Folder); Refresh(); }),
                Ui.Button("Importar skin do Rainmeter (.ini)…", ImportRainmeter))));

        page.Children.Add(Ui.Section("Skins instalados"));
        page.Children.Add(Ui.Note("Cada pasta em %APPDATA%\\AuroraDesk\\widgets com um skin.json vira um widget. Salvar o arquivo atualiza o widget na hora."));

        foreach (var skin in SkinLibrary.Discover(_host.Widgets.Folder))
        {
            string name = skin;
            var instance = _host.Widgets.InstanceFor(name);
            bool enabled = _host.Widgets.IsEnabled(name);
            string title = name, description = "";
            try
            {
                var def = SkinParser.ParseFile(SkinLibrary.SkinFile(_host.Widgets.Folder, name));
                title = def.Name;
                description = string.Join(" · ", new[] { def.Description, def.Author is null ? null : "por " + def.Author, $"{def.Width:0}×{def.Height:0}" }.Where(s => !string.IsNullOrEmpty(s)));
                if (def.Warnings.Count > 0) description += $" · {def.Warnings.Count} aviso(s) no log";
            }
            catch (Exception ex) { description = "Erro no skin: " + ex.Message; }

            var items = new List<UIElement>
            {
                Ui.Row(title, Ui.Toggle(enabled, v => { _host.Widgets.SetEnabled(name, v); Refresh(); }, "Ativo"), $"{name} — {description}"),
            };

            if (enabled && instance is not null)
            {
                void Apply(Action change) { change(); _host.Widgets.ApplyInstance(instance); }
                items.Add(Ui.Row("Camada", Ui.Combo([("Na área de trabalho", "desktop"), ("Normal", "normal"), ("Sempre no topo", "topo")], instance.Layer, v => Apply(() => instance.Layer = v))));
                items.Add(Ui.Row("Escala", Ui.Slider(0.5, 2.5, instance.Scale, 0.05, v => $"{v * 100:0}%", v => Apply(() => instance.Scale = v))));
                items.Add(Ui.Row("Opacidade", Ui.Slider(0.2, 1, instance.Opacity, 0.05, v => $"{v * 100:0}%", v => Apply(() => instance.Opacity = v))));
                items.Add(Ui.Row("Fonte do widget", FontCombo(instance.Font, f => Apply(() => instance.Font = f)), "Vazio = fonte do skin ou do tema."));
                var flags = new WrapPanel();
                flags.Children.Add(Ui.Toggle(instance.Locked, v => Apply(() => instance.Locked = v), "Travar posição"));
                flags.Children.Add(new Border { Width = 16 });
                flags.Children.Add(Ui.Toggle(instance.ClickThrough, v => Apply(() => instance.ClickThrough = v), "Clique através"));
                flags.Children.Add(new Border { Width = 16 });
                flags.Children.Add(Ui.Toggle(instance.SnapToEdges, v => Apply(() => instance.SnapToEdges = v), "Encaixar nas bordas"));
                items.Add(flags);
            }
            items.Add(Ui.Buttons(
                Ui.Button("Editar skin", () => _host.Widgets.OpenEditor(name)),
                Ui.Button("Abrir pasta", () => AppHost.OpenFolder(Path.Combine(_host.Widgets.Folder, name)))));
            page.Children.Add(Ui.Card([.. items]));
        }
        return page;
    }

    private void ImportRainmeter()
    {
        var dialog = new OpenFileDialog { Title = "Importar skin do Rainmeter", Filter = "Skin do Rainmeter|*.ini" };
        if (dialog.ShowDialog(this) != true) return;
        var (skin, warnings) = _host.Widgets.ImportRainmeter(dialog.FileName);
        string details = warnings.Count == 0
            ? "Tudo foi convertido."
            : $"{warnings.Count} item(ns) não suportado(s):\n• " + string.Join("\n• ", warnings.Take(12)) + (warnings.Count > 12 ? "\n…(veja o log)" : "");
        MessageBox.Show(this, $"Importado como \"{skin}\" e ligado.\n\n{details}\n\nO Aurora Desk importa um subconjunto do Rainmeter (sem scripts Lua, plugins nem ações).",
            "Aurora Desk", MessageBoxButton.OK, warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        Refresh();
    }

    private ComboBox FontCombo(string? current, Action<string?> changed)
    {
        var combo = new ComboBox { IsEditable = true, MinWidth = 220, Text = current ?? "" };
        combo.DropDownOpened += (_, _) =>
        {
            if (combo.Items.Count > 0) return;
            combo.Items.Add("");
            foreach (var (n, priv) in _fonts ??= _host.Themes.Fonts.ListAll()) combo.Items.Add(n);
        };
        combo.LostKeyboardFocus += (_, _) => changed(string.IsNullOrWhiteSpace(combo.Text) ? null : combo.Text.Trim());
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) changed(s.Length == 0 ? null : s); };
        return combo;
    }

    // ---------- Dock ----------

    private UIElement DockPage()
    {
        var d = Config.Dock;
        void Apply(Action change) { change(); _host.Dock.ApplyConfig(); }

        var page = new StackPanel();
        page.Children.Add(Ui.Header("Dock"));
        page.Children.Add(Ui.Card(
            Ui.Row("Mostrar o dock", Ui.Toggle(d.Enabled, v => _host.Dock.SetEnabled(v))),
            Ui.Row("Posição", Ui.Combo([("Inferior", "inferior"), ("Superior", "superior"), ("Esquerda", "esquerda"), ("Direita", "direita")], d.Edge, v => Apply(() => d.Edge = v))),
            Ui.Row("Monitor", Ui.Combo(MonitorItems(), d.Monitor ?? "", v => Apply(() => d.Monitor = v.Length == 0 ? null : v))),
            Ui.Row("Reservar espaço na tela", Ui.Toggle(d.ReserveSpace, v => Apply(() => d.ReserveSpace = v)), "Janelas maximizadas não ficam por baixo do dock (como a barra de tarefas). A reserva é desfeita ao sair."),
            Ui.Row("Ocultar automaticamente", Ui.Toggle(d.AutoHide, v => Apply(() => d.AutoHide = v)), "Some quando o mouse sai; volta ao encostar na borda. Não vale com espaço reservado."),
            Ui.Row("Mostrar apps abertos não fixados", Ui.Toggle(d.ShowRunning, v => Apply(() => d.ShowRunning = v)))));

        page.Children.Add(Ui.Section("Aparência"));
        page.Children.Add(Ui.Card(
            Ui.Row("Tamanho dos ícones", Ui.Slider(24, 96, d.IconSize, 2, v => $"{v:0} px", v => Apply(() => d.IconSize = (int)v))),
            Ui.Row("Espaçamento", Ui.Slider(0, 24, d.Spacing, 1, v => $"{v:0} px", v => Apply(() => d.Spacing = (int)v))),
            Ui.Row("Ampliar ao passar o mouse", Ui.Toggle(d.Zoom, v => Apply(() => d.Zoom = v)), "Desligar economiza CPU durante o uso do dock."),
            Ui.Row("Intensidade da ampliação", Ui.Slider(1.1, 2.0, d.ZoomFactor, 0.05, v => $"{v * 100:0}%", v => Apply(() => d.ZoomFactor = v))),
            Ui.Row("Mostrar nomes ao passar o mouse", Ui.Toggle(d.ShowLabels, v => Apply(() => d.ShowLabels = v))),
            Ui.Row("Fundo", Ui.Combo([("Cor do tema", "nenhum"), ("Acrílico", "acrilico"), ("Desfoque", "desfoque")], d.Backdrop is "acrilico" or "desfoque" ? d.Backdrop : "nenhum", v => Apply(() => d.Backdrop = v)),
                "Acrílico e desfoque usam o compositor do Windows sem GPU no app; neles a ampliação e os nomes ficam desligados.")));

        page.Children.Add(Ui.Section("Itens fixados"));
        page.Children.Add(Ui.Note("Também dá para arrastar arquivos, atalhos e pastas para o dock, reordenar arrastando e remover arrastando para fora."));
        var list = new StackPanel();
        for (int i = 0; i < d.Items.Count; i++)
        {
            var item = d.Items[i];
            int index = i;
            string label = item.Kind == "separador" ? "— separador —" : item.Name ?? Path.GetFileNameWithoutExtension(item.Path);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Ui.Button("↑", () => { Move(index, -1); }));
            buttons.Children.Add(Ui.Button("↓", () => { Move(index, 1); }));
            buttons.Children.Add(Ui.Button("Remover", () => { d.Items.RemoveAt(index); _host.Dock.ApplyConfig(); Refresh(); }));
            list.Children.Add(Ui.Row(label, buttons, item.Kind == "separador" ? null : $"{KindLabel(item.Kind)}: {item.Path}"));
        }
        page.Children.Add(Ui.Card(
            list,
            Ui.Buttons(
                Ui.Button("Adicionar arquivo ou atalho…", AddFile),
                Ui.Button("Adicionar pasta…", AddFolder),
                Ui.Button("Adicionar separador", () => { d.Items.Add(new DockItem { Kind = "separador" }); _host.Dock.ApplyConfig(); Refresh(); }))));
        return page;

        void Move(int index, int delta)
        {
            int to = index + delta;
            if (to < 0 || to >= d.Items.Count) return;
            (d.Items[index], d.Items[to]) = (d.Items[to], d.Items[index]);
            _host.Dock.ApplyConfig();
            Refresh();
        }
    }

    private static string KindLabel(string kind) => kind switch { "pasta" => "Pasta", "app" => "App", _ => "Arquivo" };

    private static IEnumerable<(string, string)> MonitorItems()
    {
        yield return ("Principal", "");
        foreach (var m in Win32.Monitors())
        {
            var info = Win32.MonitorInfo(m);
            string device = Win32.DeviceName(info);
            yield return ($"{device.Replace(@"\\.\", "")} ({info.rcMonitor.Width}×{info.rcMonitor.Height})", device);
        }
    }

    private void AddFile()
    {
        var dialog = new OpenFileDialog { Title = "Escolha um programa, atalho ou arquivo", Filter = "Programas e atalhos|*.exe;*.lnk|Todos os arquivos|*.*", DereferenceLinks = false };
        if (dialog.ShowDialog(this) != true) return;
        Config.Dock.Items.Add(new DockItem { Kind = "arquivo", Path = dialog.FileName });
        _host.Dock.ApplyConfig();
        Refresh();
    }

    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Escolha uma pasta" };
        if (dialog.ShowDialog(this) != true) return;
        Config.Dock.Items.Add(new DockItem { Kind = "pasta", Path = dialog.FolderName });
        _host.Dock.ApplyConfig();
        Refresh();
    }

    // ---------- Temas ----------

    private UIElement ThemesPage()
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Header("Temas"));
        page.Children.Add(Ui.Note("O tema define as cores, o raio dos cantos, a opacidade e a fonte do dock e dos widgets. A troca é imediata."));

        var cards = new WrapPanel();
        foreach (var theme in _host.Themes.Store.All)
        {
            var t = theme;
            bool selected = !Config.FollowWindowsTheme && _host.Themes.Current.Name == t.Name;
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var key in Theme.RequiredColors)
            {
                var c = t.Color(key);
                swatches.Children.Add(Ui.Swatch(Color.FromRgb(c.R, c.G, c.B)));
            }
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = t.Name + (selected ? "  ✓" : ""), FontWeight = FontWeights.SemiBold, FontSize = 15 });
            body.Children.Add(new TextBlock { Text = (t.Dark ? "Escuro" : "Claro") + (t.BuiltIn ? " · incluído" : " · seu"), Foreground = Ui.Secondary, FontSize = 12 });
            body.Children.Add(swatches);
            var button = new Button
            {
                Content = body, Width = 230, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 10, 14, 12),
                HorizontalContentAlignment = HorizontalAlignment.Left, BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(t.Accent.R, t.Accent.G, t.Accent.B)) : Ui.CardBorder,
                BorderThickness = new Thickness(selected ? 2 : 1),
            };
            button.Click += (_, _) => { _host.Themes.Select(t.Name); _host.SaveConfig(); Refresh(); };
            cards.Children.Add(button);
        }
        page.Children.Add(cards);

        var names = _host.Themes.Store.All.Select(t => (t.Name, t.Name)).ToList();
        page.Children.Add(Ui.Section("Seguir o Windows"));
        page.Children.Add(Ui.Card(
            Ui.Row("Seguir o modo claro/escuro do Windows", Ui.Toggle(Config.FollowWindowsTheme, v => { Config.FollowWindowsTheme = v; _host.Themes.Refresh(true); _host.SaveConfig(); Refresh(); })),
            Ui.Row("Tema no modo escuro", Ui.Combo(names, Config.DarkTheme, v => { Config.DarkTheme = v; _host.Themes.Refresh(true); _host.SaveConfig(); })),
            Ui.Row("Tema no modo claro", Ui.Combo(names, Config.LightTheme, v => { Config.LightTheme = v; _host.Themes.Refresh(true); _host.SaveConfig(); })),
            Ui.Row("Usar a cor de destaque do Windows", Ui.Toggle(Config.FollowWindowsAccent, v => { Config.FollowWindowsAccent = v; _host.Themes.Refresh(true); _host.SaveConfig(); }))));

        page.Children.Add(Ui.Section("Criar um tema"));
        page.Children.Add(Ui.Card(
            Ui.Note("Duplique o tema atual: um arquivo .json é criado em %APPDATA%\\AuroraDesk\\themes e aberto no editor. Ao salvar, o tema é recarregado."),
            Ui.Buttons(
                Ui.Button("Duplicar tema atual", DuplicateTheme),
                Ui.Button("Abrir pasta de temas", () => AppHost.OpenFolder(_host.Paths.Themes)))));
        return page;
    }

    private void DuplicateTheme()
    {
        var current = _host.Themes.Current;
        string name = current.Name + " (cópia)";
        int n = 2;
        while (_host.Themes.Store.Contains(name)) name = $"{current.Name} (cópia {n++})";
        var copy = ThemeStore.Parse(ThemeStore.Serialize(current), null);
        string json = ThemeStore.Serialize(new Theme
        {
            Name = name, Dark = copy.Dark, Colors = copy.Colors, CornerRadius = copy.CornerRadius,
            Opacity = copy.Opacity, FontFamily = copy.FontFamily, FontSize = copy.FontSize,
        });
        string file = Path.Combine(_host.Paths.Themes, string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".json");
        File.WriteAllText(file, json);
        _host.Themes.ReloadFiles();
        _host.Themes.Select(name);
        _host.SaveConfig();
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Verb = "edit" }); }
        catch { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true }); }
        Refresh();
    }

    // ---------- Fontes ----------

    private UIElement FontsPage()
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Header("Fontes"));
        page.Children.Add(Ui.Note("A fonte global vale para o dock e para os widgets que não definem a própria. Fontes .ttf/.otf colocadas em %APPDATA%\\AuroraDesk\\fonts são carregadas sem instalar no Windows."));

        string current = Config.FontOverride ?? _host.Themes.Current.FontFamily;
        var preview = new TextBlock { Text = "Aurora Desk 12:34 — Ágil Ção Ñ", FontSize = 30, Margin = new Thickness(0, 6, 0, 2), FontFamily = _host.Themes.Fonts.Get(current), TextTrimming = TextTrimming.CharacterEllipsis };
        var previewSmall = new TextBlock { Text = "quarta-feira, 21 de outubro · CPU 12% · ↓ 1,2 MB/s", FontSize = 14, Foreground = Ui.Secondary, FontFamily = preview.FontFamily };
        var selectedLabel = new TextBlock { Text = current, FontWeight = FontWeights.SemiBold };

        var search = new TextBox { MinWidth = 260 };
        var list = new ListBox { Height = 300, Background = Brushes.Transparent };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        var all = _fonts ??= _host.Themes.Fonts.ListAll();
        void Fill()
        {
            list.Items.Clear();
            string q = search.Text.Trim();
            foreach (var (name, priv) in all.Where(f => q.Length == 0 || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(400))
                list.Items.Add(new ListBoxItem { Content = priv ? $"{name}   (pasta fonts)" : name, Tag = name });
        }
        Fill();
        search.TextChanged += (_, _) => Fill();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: string name }) return;
            selectedLabel.Text = name;
            preview.FontFamily = previewSmall.FontFamily = _host.Themes.Fonts.Get(name);
        };

        page.Children.Add(Ui.Card(selectedLabel, preview, previewSmall));
        page.Children.Add(Ui.Card(
            Ui.Row("Procurar", search),
            list,
            Ui.Buttons(
                Ui.Button("Aplicar ao dock e aos widgets", () => { _host.Themes.SetFont(selectedLabel.Text); _host.SaveConfig(); }, accent: true),
                Ui.Button("Usar a fonte do tema", () => { _host.Themes.SetFont(null); _host.SaveConfig(); Refresh(); }),
                Ui.Button("Abrir pasta de fontes", () => AppHost.OpenFolder(_host.Paths.Fonts)))));
        return page;
    }

    // ---------- Desempenho ----------

    private UIElement PerformancePage()
    {
        var p = Config.Performance;
        var page = new StackPanel();
        page.Children.Add(Ui.Header("Desempenho"));

        var stats = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, LineHeight = 22 };
        var process = Process.GetCurrentProcess();
        TimeSpan lastCpu = process.TotalProcessorTime;
        DateTime lastTime = DateTime.UtcNow;
        void Update()
        {
            process.Refresh();
            var now = DateTime.UtcNow;
            double cpu = (process.TotalProcessorTime - lastCpu).TotalMilliseconds / Math.Max(1, (now - lastTime).TotalMilliseconds) / Environment.ProcessorCount * 100;
            lastCpu = process.TotalProcessorTime;
            lastTime = now;
            string paused = _host.State?.Reasons is { } r && r != SystemStateMonitor.PauseReason.None ? $"pausadas ({r})" : "ativas";
            stats.Text = string.Join("\n",
                $"Memória privada (Private Bytes):  {process.PrivateMemorySize64 / 1048576.0,7:0.0} MB",
                $"Working set:                      {process.WorkingSet64 / 1048576.0,7:0.0} MB",
                $"Heap gerenciado:                  {GC.GetTotalMemory(false) / 1048576.0,7:0.0} MB",
                $"CPU do app (média de 2 s):        {cpu,7:0.00} %",
                $"Threads / handles:                {process.Threads.Count,7} / {process.HandleCount}",
                $"Medidas ativas / leituras:        {_host.Scheduler.ActiveMeasures,7} / {_host.Scheduler.TickCount}",
                $"Medidas:                          {paused}",
                $"Widgets abertos / ícones em cache:{_host.Widgets.Count,7} / {_host.Dock.IconCount}");
        }
        Update();
        _perfTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Update(), Dispatcher);
        _perfTimer.Start();

        page.Children.Add(Ui.Card(stats, Ui.Note("A janela de configurações aberta usa memória extra; ela é liberada ao fechar."),
            Ui.Buttons(Ui.Button("Liberar memória agora", () => { _host.Trimmer.TrimNow(force: true); Update(); }))));

        page.Children.Add(Ui.Section("Economia"));
        page.Children.Add(Ui.Card(
            Ui.Row("Pausar em tela cheia e jogos", Ui.Toggle(p.PauseOnFullscreen, v => { p.PauseOnFullscreen = v; _host.SaveConfig(); })),
            Ui.Row("Pausar com a tela bloqueada", Ui.Toggle(p.PauseOnLock, v => { p.PauseOnLock = v; _host.SaveConfig(); })),
            Ui.Row("Atualizar menos na bateria", Ui.Toggle(p.BatterySaver, v => { p.BatterySaver = v; _host.State?.ApplyBattery(); _host.SaveConfig(); }), "Na bateria, os intervalos ficam 3× maiores."),
            Ui.Row("Devolver memória ociosa ao Windows", Ui.Toggle(p.TrimWorkingSet, v => { p.TrimWorkingSet = v; _host.SaveConfig(); }), "Depois de iniciar e ao fechar esta janela (no máximo a cada 5 min)."),
            Ui.Row("Renderização por GPU", Ui.Toggle(p.HardwareRendering, v => _host.RequestRestartFor(() => p.HardwareRendering = v)),
                "Desligada por padrão: com ela o WPF carrega os drivers de vídeo no processo (~+85 MB). Reinicia o app.")));

        page.Children.Add(Ui.Section("Intervalos de atualização"));
        page.Children.Add(Ui.Card(Ui.Note(
            "Padrões: CPU 1 s · memória 2 s · rede (nos exemplos) 2 s · disco e bateria 30 s · relógio na virada do minuto (ou do segundo, se o formato tiver segundos) · uptime e clima 1 min (clima consulta a internet a cada 30 min).\n" +
            "Cada skin pode mudar com \"intervalo\": 500, \"2s\" ou \"1min\" na medida. Medidas iguais em widgets diferentes são lidas uma vez só.")));
        return page;
    }

    // ---------- Geral ----------

    private UIElement GeneralPage()
    {
        var w = Config.Weather;
        var page = new StackPanel();
        page.Children.Add(Ui.Header("Geral"));

        bool startup = _host.Startup.IsEnabled;
        page.Children.Add(Ui.Card(
            Ui.Row("Ocultar a barra de tarefas do Windows enquanto o Aurora Desk estiver aberto",
                Ui.Toggle(Config.HideTaskbar, v => _host.SetHideTaskbar(v)),
                "O dock assume o lugar da barra. Ao fechar o app, ou se ele cair, a barra volta sozinha ao estado original. Com a barra oculta, o ícone da bandeja também some: abra as configurações pelo clique direito no dock ou executando o Aurora Desk de novo."),
            Ui.Note("Os atalhos do Windows continuam funcionando: Win (Iniciar), Win+A (configurações rápidas) e Win+N (notificações e calendário).")));
        page.Children.Add(Ui.Card(
            Ui.Row("Iniciar com o Windows", Ui.Toggle(startup, v => _host.Startup.Set(v)),
                _host.Startup.PointsElsewhere ? "Atenção: a entrada atual aponta para outro executável; ligar de novo corrige." : "Usa a chave Run do seu usuário; pode ser desligado a qualquer momento."),
            Ui.Row("Idioma de datas e textos dos widgets", Ui.Combo([("Português (Brasil)", "pt-BR"), ("English", "en")], Config.Language, v =>
            {
                Config.Language = v;
                Strings.Language = Strings.Parse(v);
                _host.Widgets.ReloadAll();
                _host.SaveConfig();
            }), "A interface do app é em português.")));

        page.Children.Add(Ui.Section("Clima (opcional)"));
        var city = new TextBox { Text = w.City, MinWidth = 220 };
        var lat = new TextBox { Text = w.Latitude.ToString(CultureInfo.InvariantCulture), Width = 110 };
        var lon = new TextBox { Text = w.Longitude.ToString(CultureInfo.InvariantCulture), Width = 110 };
        void SaveWeather()
        {
            w.City = city.Text.Trim();
            if (double.TryParse(lat.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var la)) w.Latitude = Math.Clamp(la, -90, 90);
            if (double.TryParse(lon.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var lo)) w.Longitude = Math.Clamp(lo, -180, 180);
            _host.ApplyWeather();
        }
        city.LostFocus += (_, _) => SaveWeather();
        lat.LostFocus += (_, _) => SaveWeather();
        lon.LostFocus += (_, _) => SaveWeather();
        var coords = new StackPanel { Orientation = Orientation.Horizontal };
        coords.Children.Add(lat);
        coords.Children.Add(new Border { Width = 8 });
        coords.Children.Add(lon);
        page.Children.Add(Ui.Card(
            Ui.Note("É o único recurso que usa a internet: consulta api.open-meteo.com (sem conta e sem chave) a cada 30 minutos, enviando só a latitude e a longitude abaixo. Desligado por padrão.", Ui.Warning),
            Ui.Row("Ativar o clima", Ui.Toggle(w.Enabled, v => { w.Enabled = v; _host.ApplyWeather(); if (v) _host.Widgets.SetEnabled("clima", true); })),
            Ui.Row("Cidade (só para exibição)", city),
            Ui.Row("Latitude, longitude", coords, "Ex.: São Paulo -23.55, -46.63 · Rio de Janeiro -22.91, -43.17")));

        page.Children.Add(Ui.Section("Perfis"));
        page.Children.Add(Ui.Card(
            Ui.Note("Um perfil (.zip) leva tema, widgets (com seus arquivos), fontes e a configuração do dock. Ao importar, o estado atual é salvo antes em backup\\."),
            Ui.Buttons(
                Ui.Button("Exportar perfil…", ExportProfile),
                Ui.Button("Importar perfil…", ImportProfile))));

        page.Children.Add(Ui.Section("Restaurar"));
        page.Children.Add(Ui.Card(
            Ui.Note("Desfaz tudo o que o Aurora Desk mudou no Windows: devolve o valor original de \"Iniciar com o Windows\" e libera o espaço reservado pelo dock. Seus widgets e temas não são apagados."),
            Ui.Buttons(Ui.Button("Restaurar original", () =>
            {
                if (MessageBox.Show(this, "Restaurar as configurações originais do Windows?", "Aurora Desk", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                int n = _host.RestoreOriginal();
                MessageBox.Show(this, n == 0 ? "Nada a restaurar: o Windows já está como antes." : $"{n} configuração(ões) do Windows restaurada(s).", "Aurora Desk");
                Refresh();
            }))));

        page.Children.Add(Ui.Section("Sobre"));
        page.Children.Add(Ui.Card(
            Ui.Note($"Aurora Desk {typeof(AppHost).Assembly.GetName().Version?.ToString(3)} · .NET {Environment.Version} · sem telemetria."),
            Ui.Buttons(
                Ui.Button("Pasta de dados", () => AppHost.OpenFolder(_host.Paths.Roaming)),
                Ui.Button("Pasta de logs", () => AppHost.OpenFolder(_host.Paths.Logs)))));
        return page;
    }

    private void ExportProfile()
    {
        var dialog = new SaveFileDialog { Title = "Exportar perfil", Filter = "Perfil do Aurora Desk|*.zip", FileName = $"aurora-perfil-{DateTime.Now:yyyy-MM-dd}.zip" };
        if (dialog.ShowDialog(this) != true) return;
        Core.Profiles.ProfileArchive.Export(dialog.FileName, Config, _host.Paths);
        MessageBox.Show(this, "Perfil exportado.", "Aurora Desk");
    }

    private void ImportProfile()
    {
        var dialog = new OpenFileDialog { Title = "Importar perfil", Filter = "Perfil do Aurora Desk|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "Importar substitui o tema, os widgets e o dock atuais (um backup é salvo antes). O app vai reiniciar. Continuar?",
                "Aurora Desk", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _host.ImportProfile(dialog.FileName);
    }
}

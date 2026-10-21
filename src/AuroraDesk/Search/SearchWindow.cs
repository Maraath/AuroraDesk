using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Search;
using AuroraDesk.Core.Theming;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;
using AuroraDesk.Theming;

namespace AuroraDesk.Search;

/// <summary>
/// Pesquisa do Aurora: painel com as cores do tema, logo acima do dock. Apps do menu Iniciar,
/// configurações do Windows, pesquisa na web e, por último, "arquivos na pesquisa do Windows".
/// Criado ao abrir e descartado ao fechar (clique fora, Esc ou ao abrir um resultado).
/// </summary>
internal sealed class SearchWindow : Window
{
    private const int MaxResults = 8;
    private static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly ThemeService _themes;
    private readonly Func<string, int, BitmapSource?> _icon;
    private readonly string _engine;
    private readonly TextBox _box = new();
    private readonly StackPanel _list = new();
    private readonly TextBlock _hint = new();
    private IReadOnlyList<SearchItem> _apps = [];
    private List<SearchItem> _results = [];
    private int _selected;
    private bool _closing;

    /// <param name="icon">Ícone do shell por caminho e tamanho (o cache do dock).</param>
    public SearchWindow(ThemeService themes, Func<string, int, BitmapSource?> icon, string engine)
    {
        _themes = themes;
        _icon = icon;
        _engine = engine;
        var t = themes.Current;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 620;
        Title = "Aurora Desk - pesquisa";
        FontFamily = themes.DefaultFont;

        var search = new DockPanel { Margin = new Thickness(18, 14, 18, 12) };
        var lupa = new TextBlock { Text = "", FontFamily = Glyphs, FontSize = 18, Foreground = B(t.TextSecondary), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(lupa, System.Windows.Controls.Dock.Left);
        search.Children.Add(lupa);
        _box.FontSize = 17;
        _box.Background = Brushes.Transparent;
        _box.Foreground = B(t.Text);
        _box.CaretBrush = B(t.Accent);
        _box.SelectionBrush = B(t.Accent.WithOpacity(0.4));
        _box.BorderThickness = new Thickness(0);
        _box.VerticalContentAlignment = VerticalAlignment.Center;
        _box.TextChanged += (_, _) => Update();
        search.Children.Add(_box);

        var divider = new Border { Height = 1, Background = B(t.Border), Margin = new Thickness(14, 0, 14, 6) };
        _hint.Text = "Digite para pesquisar apps, configurações e a web";
        _hint.Foreground = B(t.TextSecondary);
        _hint.FontSize = 13;
        _hint.Margin = new Thickness(18, 8, 18, 14);
        var footer = new TextBlock
        {
            Text = "↑ ↓ navegar   ·   Enter abrir   ·   Esc fechar",
            Foreground = B(t.TextSecondary.WithOpacity(0.8)), FontSize = 11, Margin = new Thickness(18, 4, 18, 12),
        };

        var content = new StackPanel();
        content.Children.Add(search);
        content.Children.Add(divider);
        content.Children.Add(_hint);
        content.Children.Add(_list);
        content.Children.Add(footer);

        double radius = Math.Min(t.CornerRadius + 4, 20);
        Content = new Border
        {
            Background = B(t.Background with { A = 255 }), // opaco: texto legível sobre qualquer janela
            BorderBrush = B(t.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            Child = content,
            Margin = new Thickness(0),
        };

        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => CloseSoon();
        Loaded += async (_, _) =>
        {
            _box.Focus();
            Keyboard.Focus(_box);
            _apps = await AppCatalog.GetAsync();
            Update();
        };
    }

    private SolidColorBrush B(ArgbColor c) => _themes.Brush(c);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        nint ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TOOLWINDOW);
        RenderModes.Apply(this);
    }

    /// <summary>
    /// Mostra centralizado em <paramref name="centerX"/>. Com o dock embaixo, a base fica em
    /// <paramref name="edgeY"/>; com o dock em cima, o topo fica em <paramref name="edgeY"/> (pixels físicos).
    /// </summary>
    public void ShowNearDock(int centerX, int edgeY, bool dockAtBottom, double scale)
    {
        Show();
        UpdateLayout();
        nint hwnd = new WindowInteropHelper(this).Handle;
        int w = (int)Math.Round(ActualWidth * scale), h = (int)Math.Round(ActualHeight * scale);
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, centerX - w / 2, dockAtBottom ? edgeY - h : edgeY, 0, 0, Win32.SWP_NOSIZE);
        _anchorBottom = dockAtBottom ? edgeY : 0;
        _scale = scale;
        Win32.SetForegroundWindow(hwnd);
        Activate();
    }

    private int _anchorBottom;
    private double _scale = 1;

    /// <summary>Mantém a base do painel no lugar quando a lista cresce ou diminui.</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_anchorBottom == 0 || !info.HeightChanged) return;
        nint hwnd = new WindowInteropHelper(this).Handle;
        Win32.GetWindowRect(hwnd, out var r);
        int h = (int)Math.Round(info.NewSize.Height * _scale);
        Win32.SetWindowPos(hwnd, 0, r.Left, _anchorBottom - h, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    // ---------- resultados ----------

    private void Update()
    {
        string q = _box.Text.Trim();
        _results = [];
        if (q.Length > 0)
        {
            _results.AddRange(SearchRanker.Rank(q, _apps.Concat(SettingsCatalog.All), MaxResults - 2));
            _results.Add(new SearchItem($"Pesquisar \"{q}\" na web", SearchRanker.WebUrl(q, _engine), ResultKind.Web));
            _results.Add(new SearchItem($"Procurar \"{q}\" em arquivos e mais", q, ResultKind.WindowsSearch));
        }
        _selected = 0;
        _hint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    private void Render()
    {
        var t = _themes.Current;
        _list.Children.Clear();
        for (int i = 0; i < _results.Count; i++)
        {
            var item = _results[i];
            int index = i;
            var row = new Grid { Margin = new Thickness(8, 0, 8, 0), Height = 46, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var bg = new Border { CornerRadius = new CornerRadius(10), Background = i == _selected ? B(t.Surface) : Brushes.Transparent };
            Grid.SetColumnSpan(bg, 2);
            row.Children.Add(bg);
            row.Children.Add(IconFor(item, t));
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = item.Name, Foreground = B(t.Text), FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
            texts.Children.Add(new TextBlock { Text = Subtitle(item), Foreground = B(t.TextSecondary), FontSize = 11.5 });
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            row.MouseEnter += (_, _) => { _selected = index; Highlight(); };
            row.MouseLeftButtonUp += (_, _) => Open(item);
            _list.Children.Add(row);
        }
        if (_results.Count > 0) _list.Margin = new Thickness(0, 0, 0, 2);
    }

    private void Highlight()
    {
        var t = _themes.Current;
        for (int i = 0; i < _list.Children.Count; i++)
            if (_list.Children[i] is Grid g && g.Children[0] is Border b)
                b.Background = i == _selected ? B(t.Surface) : Brushes.Transparent;
    }

    private static string Subtitle(SearchItem item) => item.Kind switch
    {
        ResultKind.App => "Aplicativo",
        ResultKind.Setting => item.Target.StartsWith("ms-settings:", StringComparison.Ordinal) ? "Configurações do Windows" : "Sistema",
        ResultKind.Web => "Navegador padrão",
        _ => "Arquivos, pastas e mais na pesquisa do Windows",
    };

    private FrameworkElement IconFor(SearchItem item, Theme t)
    {
        if (item.Kind == ResultKind.App)
        {
            var img = _icon(@"shell:AppsFolder\" + item.Target, 32);
            if (img is not null)
                return new Image { Source = img, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        string glyph = item.Kind switch { ResultKind.Setting => "", ResultKind.Web => "", ResultKind.WindowsSearch => "", _ => "" };
        return new TextBlock
        {
            Text = glyph, FontFamily = Glyphs, FontSize = 18, Foreground = B(t.Accent),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ---------- teclado e ações ----------

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseSoon();
                e.Handled = true;
                break;
            case Key.Down when _results.Count > 0:
                _selected = (_selected + 1) % _results.Count;
                Highlight();
                e.Handled = true;
                break;
            case Key.Up when _results.Count > 0:
                _selected = (_selected - 1 + _results.Count) % _results.Count;
                Highlight();
                e.Handled = true;
                break;
            case Key.Enter when _results.Count > 0:
                Open(_results[_selected]);
                e.Handled = true;
                break;
        }
    }

    private void Open(SearchItem item)
    {
        CloseSoon();
        try
        {
            switch (item.Kind)
            {
                case ResultKind.App:
                    Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + item.Target) { UseShellExecute = true });
                    break;
                case ResultKind.WindowsSearch:
                    // Abre a pesquisa do Windows e digita o termo (para arquivos e conteúdo indexado).
                    string text = item.Target;
                    Win32.SendWinShortcut('S');
                    var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(450) };
                    timer.Tick += (_, _) => { timer.Stop(); Win32.SendText(text); };
                    timer.Start();
                    break;
                default:
                    Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível abrir '{item.Name}': {ex.Message}");
        }
    }

    private void CloseSoon()
    {
        if (_closing) return;
        _closing = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Close);
    }
}

using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Theming;
using AuroraDesk.Infrastructure;
using AuroraDesk.Interop;
using AuroraDesk.Theming;

namespace AuroraDesk.Dock;

/// <summary>
/// Painel de um grupo de atalhos, aberto acima do ícone do grupo no dock: grade com os atalhos
/// (clique abre), soltar arquivos adiciona, clique direito remove, título editável.
/// Uma só janela serve a todos os grupos: ao fechar (clique fora, Esc ou ao abrir um atalho) ela
/// só se esconde, e a próxima abertura não paga a criação da janela nem refaz atalhos iguais.
/// O dock a descarta quando o tema, a escala ou o próprio dock mudam.
/// </summary>
internal sealed class GroupWindow : Window
{
    private const double Tile = 88;
    private const int Columns = 5;

    private readonly ThemeService _themes;
    private readonly Func<DockItem, (string Name, BitmapSource? Icon)> _describe;
    private readonly WrapPanel _tiles = new();
    private readonly TextBlock _title = new();
    private readonly TextBox _titleEdit = new();
    private readonly TextBlock _empty = new();
    private readonly Border _panel;
    private DockItem _group = new() { Kind = "grupo" };
    private string _shownKey = "";
    private bool _closing;
    private int _anchorEdge;
    private bool _anchorBottom;
    private double _scale = 1;

    public event Action<DockItem>? Launch;
    public event Action<DockItem>? RemoveItem;
    public event Action<DockItem>? MoveToDock;
    public event Action<string[]>? FilesAdded;
    public event Action<string>? Renamed;
    public event Action? AddRequested;
    /// <summary>O painel se escondeu (não foi destruído).</summary>
    public event Action? Hidden;

    /// <summary>Grupo mostrado agora (ou o último, com o painel escondido).</summary>
    public DockItem Group => _group;

    public bool IsOpen => IsVisible && !_closing;

    public GroupWindow(ThemeService themes, Func<DockItem, (string, BitmapSource?)> describe)
    {
        _themes = themes;
        _describe = describe;
        var t = themes.Current;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Aurora Desk - grupo";
        FontFamily = themes.DefaultFont;
        AllowDrop = true;

        _title.FontSize = 15;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = B(t.Text);
        _title.Cursor = Cursors.IBeam;
        _title.ToolTip = "Clique para renomear";
        _title.MouseLeftButtonUp += (_, _) => StartRename();
        _titleEdit.FontSize = 15;
        _titleEdit.Background = B(t.Surface);
        _titleEdit.Foreground = B(t.Text);
        _titleEdit.CaretBrush = B(t.Accent);
        _titleEdit.BorderBrush = B(t.Accent);
        _titleEdit.Visibility = Visibility.Collapsed;
        _titleEdit.MinWidth = 160;
        _titleEdit.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { FinishRename(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _titleEdit.Visibility = Visibility.Collapsed; _title.Visibility = Visibility.Visible; e.Handled = true; }
        };
        _titleEdit.LostKeyboardFocus += (_, _) => FinishRename();

        var add = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14,
            Foreground = B(t.TextSecondary), Cursor = Cursors.Hand, ToolTip = "Adicionar atalhos…", VerticalAlignment = VerticalAlignment.Center,
        };
        add.MouseLeftButtonUp += (_, _) => { CloseSoon(); AddRequested?.Invoke(); };

        var header = new DockPanel { Margin = new Thickness(16, 12, 14, 8), LastChildFill = true };
        DockPanel.SetDock(add, System.Windows.Controls.Dock.Right);
        header.Children.Add(add);
        var titles = new Grid();
        titles.Children.Add(_title);
        titles.Children.Add(_titleEdit);
        header.Children.Add(titles);

        _tiles.Margin = new Thickness(8, 0, 8, 4);
        _tiles.MaxWidth = Columns * Tile;
        _empty.Text = "Arraste atalhos, programas ou pastas para cá";
        _empty.Foreground = B(t.TextSecondary);
        _empty.FontSize = 12.5;
        _empty.Margin = new Thickness(16, 6, 16, 10);
        var footer = new TextBlock
        {
            Text = "Arraste arquivos para cá para adicionar · clique direito num atalho para remover",
            Foreground = B(t.TextSecondary.WithOpacity(0.8)), FontSize = 11, Margin = new Thickness(16, 2, 16, 12),
            TextWrapping = TextWrapping.Wrap, MaxWidth = Columns * Tile,
        };

        var content = new StackPanel { MinWidth = 260 };
        content.Children.Add(header);
        content.Children.Add(_empty);
        content.Children.Add(_tiles);
        content.Children.Add(footer);
        _panel = new Border
        {
            Background = B(t.Background with { A = 255 }),
            BorderBrush = B(t.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Min(t.CornerRadius + 4, 20)),
            Child = content,
        };
        Content = _panel;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _titleEdit.Visibility != Visibility.Visible) { CloseSoon(); e.Handled = true; } };
        Deactivated += (_, _) => CloseSoon();
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) FilesAdded?.Invoke(files);
            Refresh();
        };
    }

    private SolidColorBrush B(ArgbColor c) => _themes.Brush(c);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE) | Win32.WS_EX_TOOLWINDOW);
        RenderModes.Apply(this);
    }

    /// <summary>Abre o painel de <paramref name="group"/> sobre o ícone dele no dock.</summary>
    public void Open(DockItem group, Rect itemScreen, bool dockAtBottom, double scale)
    {
        _group = group;
        _closing = false;
        CancelRename();
        Refresh();
        ShowNear(itemScreen, dockAtBottom, scale);
    }

    /// <summary>Chave do que está desenhado: só refaz os atalhos quando algo mudou.</summary>
    private string ContentKey() =>
        string.Join('\n', (_group.Items ?? []).Select(i => $"{RuntimeHelpers.GetHashCode(i)}|{i.Kind}|{i.Path}|{i.Name}").Prepend($"{RuntimeHelpers.GetHashCode(_group)}"));

    /// <summary>Redesenha título e atalhos (depois de adicionar, remover ou renomear).</summary>
    public void Refresh()
    {
        var t = _themes.Current;
        _title.Text = _group.Name ?? "Grupo";
        string key = ContentKey();
        if (key == _shownKey) return;
        _shownKey = key;
        _tiles.Children.Clear();
        var items = _group.Items ?? [];
        _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in items)
        {
            var it = item;
            var (name, icon) = _describe(item);
            var tile = new StackPanel { Width = Tile - 8, Margin = new Thickness(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = name };
            var bg = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(4, 8, 4, 6), Background = Brushes.Transparent };
            var inner = new StackPanel();
            if (icon is not null) inner.Children.Add(new Image { Source = icon, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center });
            else inner.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), Background = B(t.Surface) });
            inner.Children.Add(new TextBlock
            {
                Text = name, Foreground = B(t.Text), FontSize = 11.5, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap, MaxHeight = 32, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            bg.Child = inner;
            tile.Children.Add(bg);
            tile.MouseEnter += (_, _) => bg.Background = B(t.Surface);
            tile.MouseLeave += (_, _) => bg.Background = Brushes.Transparent;
            tile.MouseLeftButtonUp += (_, _) => { CloseSoon(); Launch?.Invoke(it); };
            tile.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                nint hwnd = new WindowInteropHelper(this).Handle;
                new Tray.NativeMenu()
                    .Item(name, () => { }, enabled: false)
                    .Separator()
                    .Item("Abrir", () => { CloseSoon(); Launch?.Invoke(it); })
                    .Item("Mover de volta para o dock", () => { MoveToDock?.Invoke(it); Refresh(); })
                    .Item("Remover do grupo", () => { RemoveItem?.Invoke(it); Refresh(); })
                    .Show(hwnd, t.Dark);
            };
            _tiles.Children.Add(tile);
        }
    }

    public void StartRename()
    {
        _titleEdit.Text = _group.Name ?? "Grupo";
        _title.Visibility = Visibility.Collapsed;
        _titleEdit.Visibility = Visibility.Visible;
        _titleEdit.Focus();
        _titleEdit.SelectAll();
    }

    private void CancelRename()
    {
        _titleEdit.Visibility = Visibility.Collapsed;
        _title.Visibility = Visibility.Visible;
    }

    private void FinishRename()
    {
        if (_titleEdit.Visibility != Visibility.Visible) return;
        _titleEdit.Visibility = Visibility.Collapsed;
        _title.Visibility = Visibility.Visible;
        string name = _titleEdit.Text.Trim();
        if (name.Length > 0 && name != _group.Name) Renamed?.Invoke(name);
        Refresh();
    }

    /// <summary>Mostra centralizado sobre o ícone do grupo (pixels físicos), do lado de dentro da tela.</summary>
    private void ShowNear(Rect itemScreen, bool dockAtBottom, double scale)
    {
        _anchorEdge = 0; // o reposicionamento abaixo vale para esta abertura, não para a anterior
        Show();
        _panel.Opacity = 1;
        UpdateLayout();
        nint hwnd = new WindowInteropHelper(this).Handle;
        int w = (int)Math.Round(ActualWidth * scale), h = (int)Math.Round(ActualHeight * scale);
        int gap = (int)Math.Round(14 * scale);
        int x = (int)(itemScreen.X + itemScreen.Width / 2) - w / 2;
        var work = Win32.MonitorInfo(Win32.MonitorFromPoint(new Win32.POINT { X = (int)itemScreen.X, Y = (int)itemScreen.Y }, Win32.MONITOR_DEFAULTTONEAREST)).rcMonitor;
        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
        _anchorBottom = dockAtBottom;
        _anchorEdge = dockAtBottom ? (int)itemScreen.Top - gap : (int)itemScreen.Bottom + gap;
        _scale = scale;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, dockAtBottom ? _anchorEdge - h : _anchorEdge, 0, 0, Win32.SWP_NOSIZE);
        Win32.SetForegroundWindow(hwnd);
        Activate();
    }

    /// <summary>Mantém a borda do lado do dock parada quando o painel muda de tamanho.</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_anchorEdge == 0 || !_anchorBottom || !info.HeightChanged) return;
        nint hwnd = new WindowInteropHelper(this).Handle;
        Win32.GetWindowRect(hwnd, out var r);
        int h = (int)Math.Round(info.NewSize.Height * _scale);
        Win32.SetWindowPos(hwnd, 0, r.Left, _anchorEdge - h, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Esconde o painel. Antes, o conteúdo fica transparente: a janela em camadas guarda a última
    /// imagem, e sem isso a próxima abertura mostraria por um instante o grupo anterior.
    /// </summary>
    public void HideSoon() => CloseSoon();

    private void CloseSoon()
    {
        if (_closing || !IsVisible) return;
        _closing = true;
        _panel.Opacity = 0;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!_closing) return; // reaberto antes de esconder
            CancelRename();
            Hide();
            Hidden?.Invoke();
        });
    }
}

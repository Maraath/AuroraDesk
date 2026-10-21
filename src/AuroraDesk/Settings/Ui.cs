using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraDesk.Settings;

/// <summary>Blocos de interface da janela de configurações (código em vez de XAML: menos BAML carregado).</summary>
internal static class Ui
{
    public static readonly Brush CardBackground = Frozen(Color.FromRgb(0x2B, 0x2B, 0x2E));
    public static readonly Brush CardBorder = Frozen(Color.FromRgb(0x3A, 0x3A, 0x3F));
    public static readonly Brush WindowBackground = Frozen(Color.FromRgb(0x1F, 0x1F, 0x22));
    public static readonly Brush Secondary = Frozen(Color.FromRgb(0xA8, 0xAD, 0xB4));
    public static readonly Brush Warning = Frozen(Color.FromRgb(0xF5, 0xC2, 0x6B));
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static TextBlock Header(string text) => new() { Text = text, FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };

    public static TextBlock Section(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 8) };

    public static TextBlock Note(string text, Brush? brush = null) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Foreground = brush ?? Secondary, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 6),
    };

    public static Border Card(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var c in children) panel.Children.Add(c);
        return new Border
        {
            Background = CardBackground, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 0, 6), Child = panel,
        };
    }

    /// <summary>Linha com rótulo (e descrição) à esquerda e controle à direita.</summary>
    public static Grid Row(string label, UIElement control, string? description = null)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (description is not null) text.Children.Add(new TextBlock { Text = description, Foreground = Secondary, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        g.Children.Add(text);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    public static CheckBox Toggle(bool value, Action<bool> changed, string? content = null)
    {
        var cb = new CheckBox { IsChecked = value, Content = content, MinWidth = 0 };
        cb.Checked += (_, _) => changed(true);
        cb.Unchecked += (_, _) => changed(false);
        return cb;
    }

    public static ComboBox Combo(IEnumerable<(string Label, string Value)> items, string? current, Action<string> changed, double width = 200)
    {
        var combo = new ComboBox { MinWidth = width };
        foreach (var (label, value) in items)
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            combo.Items.Add(item);
            if (string.Equals(value, current, StringComparison.OrdinalIgnoreCase)) combo.SelectedItem = item;
        }
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string v }) changed(v); };
        return combo;
    }

    public static StackPanel Slider(double min, double max, double value, double step, Func<double, string> format, Action<double> changed, double width = 180)
    {
        var label = new TextBlock { Text = format(value), Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => { label.Text = format(e.NewValue); changed(e.NewValue); };
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(label);
        p.Children.Add(slider);
        return p;
    }

    public static Button Button(string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 5, 12, 6), MinWidth = 0 };
        if (accent && Application.Current.TryFindResource("AccentButtonStyle") is Style s) b.Style = s;
        b.Click += (_, _) =>
        {
            try { click(); }
            catch (Exception ex) { Core.Logging.Log.Error($"Ação '{text}' falhou", ex); MessageBox.Show(ex.Message, "Aurora Desk", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        return b;
    }

    public static WrapPanel Buttons(params Button[] buttons)
    {
        var p = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    public static Border Swatch(Color c, double size = 18) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Margin = new Thickness(0, 0, 4, 0),
        Background = Frozen(c), BorderBrush = CardBorder, BorderThickness = new Thickness(1),
    };
}

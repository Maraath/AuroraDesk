using System.Windows.Media;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Theming;

namespace AuroraDesk.Theming;

/// <summary>
/// Tema ativo + caches de pincéis e canetas congelados (Freeze: o WPF não precisa rastrear
/// mudanças e o objeto pode ser compartilhado por todos os widgets e pelo dock).
/// </summary>
internal sealed class ThemeService
{
    private readonly AppConfig _config;
    private readonly Dictionary<uint, SolidColorBrush> _brushes = [];
    private readonly Dictionary<(uint, double), Pen> _pens = [];

    public ThemeService(AppConfig config, ThemeStore store, FontService fonts)
    {
        _config = config;
        Store = store;
        Fonts = fonts;
        Current = Compute();
    }

    public ThemeStore Store { get; }
    public FontService Fonts { get; }
    public Theme Current { get; private set; }

    /// <summary>Disparado na thread de interface quando o tema ou a fonte mudam.</summary>
    public event Action? Changed;

    /// <summary>Fonte padrão efetiva (a global do usuário, ou a do tema).</summary>
    public FontFamily DefaultFont => Fonts.Get(string.IsNullOrWhiteSpace(_config.FontOverride) ? Current.FontFamily : _config.FontOverride);

    public FontFamily Font(string? name) => string.IsNullOrWhiteSpace(name) ? DefaultFont : Fonts.Get(name);

    public SolidColorBrush Brush(ArgbColor color)
    {
        if (_brushes.TryGetValue(color.Value, out var b)) return b;
        if (_brushes.Count > 512) _brushes.Clear();
        b = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        b.Freeze();
        _brushes[color.Value] = b;
        return b;
    }

    public Pen Pen(ArgbColor color, double thickness)
    {
        var key = (color.Value, thickness);
        if (_pens.TryGetValue(key, out var p)) return p;
        if (_pens.Count > 256) _pens.Clear();
        p = new Pen(Brush(color), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        _pens[key] = p;
        return p;
    }

    public SolidColorBrush Brush(string? spec, ArgbColor fallback) => Brush(Current.Resolve(spec, fallback));

    /// <summary>Escolhe um tema pelo nome (desliga "seguir o Windows").</summary>
    public void Select(string name)
    {
        _config.Theme = name;
        _config.FollowWindowsTheme = false;
        Refresh(force: true);
    }

    public void SetFont(string? name)
    {
        _config.FontOverride = string.IsNullOrWhiteSpace(name) ? null : name;
        Refresh(force: true);
    }

    /// <summary>Recalcula o tema (após mudar config, arquivos de tema ou o tema do Windows).</summary>
    public void Refresh(bool force = false)
    {
        var next = Compute();
        bool changed = force || !SameTheme(next, Current);
        Current = next;
        if (!changed) return;
        Log.Info($"Tema ativo: {Current.Name}");
        Changed?.Invoke();
    }

    public void ReloadFiles()
    {
        Store.Reload();
        Refresh(force: true);
    }

    public void ReloadFonts()
    {
        Fonts.Reload();
        Refresh(force: true);
    }

    private Theme Compute() => Store.Resolve(
        _config.Theme,
        _config.FollowWindowsTheme,
        _config.FollowWindowsTheme && WindowsTheme.AppsUseDarkMode(),
        _config.DarkTheme,
        _config.LightTheme,
        _config.FollowWindowsAccent ? WindowsTheme.Accent() : null);

    private static bool SameTheme(Theme a, Theme b) =>
        a.Name == b.Name && a.Accent == b.Accent && ReferenceEquals(a.SourcePath, b.SourcePath) && a.Colors.Count == b.Colors.Count;
}

namespace AuroraDesk.Core.Theming;

/// <summary>
/// Tema: paleta, raio dos cantos, opacidade e fonte. Dock e widgets herdam daqui;
/// skins referenciam cores com "@chave" (ex.: "@destaque", "@texto/60" = 60% de opacidade).
/// </summary>
public sealed class Theme
{
    /// <summary>Chaves que todo tema tem (as ausentes recebem o valor do Grafite).</summary>
    public static readonly string[] RequiredColors = ["fundo", "superficie", "texto", "textoSecundario", "destaque", "borda"];

    public required string Name { get; init; }
    public bool Dark { get; init; } = true;
    public IReadOnlyDictionary<string, ArgbColor> Colors { get; init; } = new Dictionary<string, ArgbColor>();
    public double CornerRadius { get; init; } = 12;
    /// <summary>Opacidade dos painéis (aplicada em "@painel").</summary>
    public double Opacity { get; init; } = 0.85;
    public string FontFamily { get; init; } = "Segoe UI Variable Text";
    public double FontSize { get; init; } = 13;
    /// <summary>Caminho do arquivo de origem; null para temas embutidos.</summary>
    public string? SourcePath { get; init; }
    public bool BuiltIn => SourcePath is null;

    public ArgbColor Background => Color("fundo");
    public ArgbColor Surface => Color("superficie");
    public ArgbColor Text => Color("texto");
    public ArgbColor TextSecondary => Color("textoSecundario");
    public ArgbColor Accent => Color("destaque");
    public ArgbColor Border => Color("borda");
    /// <summary>Fundo com a opacidade do tema: o fundo padrão de widgets e do dock.</summary>
    public ArgbColor Panel => Background.WithOpacity(Opacity);

    public ArgbColor Color(string key) =>
        key.Equals("painel", StringComparison.OrdinalIgnoreCase) ? Panel
        : Colors.TryGetValue(key, out var c) ? c
        : Colors.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    public bool HasColor(string key) =>
        key.Equals("painel", StringComparison.OrdinalIgnoreCase) || Colors.Keys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolve uma cor de skin: "#RRGGBB", "#AARRGGBB", "@chave" ou "@chave/NN" (NN% de opacidade).
    /// Devolve <paramref name="fallback"/> se a especificação for vazia ou inválida.
    /// </summary>
    public ArgbColor Resolve(string? spec, ArgbColor fallback)
    {
        if (string.IsNullOrWhiteSpace(spec)) return fallback;
        spec = spec.Trim();
        if (spec[0] != '@') return ArgbColor.TryParse(spec, out var c) ? c : fallback;

        string key = spec[1..];
        double opacity = 1;
        int slash = key.IndexOf('/');
        if (slash >= 0)
        {
            if (double.TryParse(key[(slash + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
                opacity = pct / 100.0;
            key = key[..slash];
        }
        return HasColor(key) ? Color(key).WithOpacity(opacity) : fallback;
    }

    /// <summary>Cópia com outra cor de destaque (usado por "seguir a cor de destaque do Windows").</summary>
    public Theme WithAccent(ArgbColor accent)
    {
        var colors = new Dictionary<string, ArgbColor>(Colors, StringComparer.OrdinalIgnoreCase) { ["destaque"] = accent with { A = 255 } };
        return new Theme
        {
            Name = Name, Dark = Dark, Colors = colors, CornerRadius = CornerRadius, Opacity = Opacity,
            FontFamily = FontFamily, FontSize = FontSize, SourcePath = SourcePath,
        };
    }
}

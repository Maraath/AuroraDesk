using System.Text.Json;
using AuroraDesk.Core.Logging;

namespace AuroraDesk.Core.Theming;

/// <summary>
/// Temas embutidos (Grafite, Neve, Mocha) + temas do usuário em %APPDATA%\AuroraDesk\themes\*.json.
/// Um tema do usuário com o mesmo nome de um embutido o substitui.
/// </summary>
public sealed class ThemeStore
{
    public const string DefaultDark = "Grafite";
    public const string DefaultLight = "Neve";

    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private readonly Dictionary<string, Theme> _themes = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _userFolder;

    public ThemeStore(string? userFolder)
    {
        _userFolder = userFolder;
        Reload();
    }

    public IReadOnlyCollection<Theme> All => _themes.Values.OrderBy(t => t.BuiltIn ? 0 : 1).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public Theme Get(string? name) =>
        name is not null && _themes.TryGetValue(name, out var t) ? t : _themes[DefaultDark];

    public bool Contains(string name) => _themes.ContainsKey(name);

    public void Reload()
    {
        _themes.Clear();
        var asm = typeof(ThemeStore).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.Contains("themes", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = asm.GetManifestResourceStream(res)!;
            using var reader = new StreamReader(stream);
            var theme = Parse(reader.ReadToEnd(), null);
            _themes[theme.Name] = theme;
        }

        if (_userFolder is null || !Directory.Exists(_userFolder)) return;
        foreach (var file in Directory.EnumerateFiles(_userFolder, "*.json"))
        {
            try
            {
                var theme = Parse(File.ReadAllText(file), file);
                _themes[theme.Name] = theme;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or IOException)
            {
                Log.Warn($"Tema ignorado ({Path.GetFileName(file)}): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Formato:
    /// { "nome": "...", "escuro": true, "raio": 12, "opacidade": 0.85, "fonte": "Segoe UI Variable Text",
    ///   "tamanhoFonte": 13, "cores": { "fundo": "#1B1D22", "destaque": "#7AA2F7", ... } }
    /// </summary>
    public static Theme Parse(string json, string? sourcePath, Theme? defaults = null)
    {
        using var doc = JsonDocument.Parse(json, JsonOptions);
        var root = doc.RootElement;
        string name = root.TryGetProperty("nome", out var n) && n.GetString() is { Length: > 0 } s
            ? s
            : Path.GetFileNameWithoutExtension(sourcePath) ?? throw new FormatException("Tema sem \"nome\"");

        var colors = new Dictionary<string, ArgbColor>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("cores", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in c.EnumerateObject())
                colors[p.Name] = ArgbColor.TryParse(p.Value.GetString(), out var color) ? color : throw new FormatException($"Cor inválida em \"{p.Name}\"");
        }

        bool dark = root.TryGetProperty("escuro", out var e) ? e.GetBoolean() : (!colors.TryGetValue("fundo", out var bg) || bg.Luminance < 0.5);
        if (defaults is not null)
            foreach (var key in Theme.RequiredColors)
                colors.TryAdd(key, defaults.Color(key));
        else
            FillDefaults(colors, dark);

        return new Theme
        {
            Name = name,
            Dark = dark,
            Colors = colors,
            CornerRadius = Number(root, "raio", 12),
            Opacity = Math.Clamp(Number(root, "opacidade", 0.85), 0, 1),
            FontFamily = root.TryGetProperty("fonte", out var f) && f.GetString() is { Length: > 0 } font ? font : "Segoe UI Variable Text",
            FontSize = Number(root, "tamanhoFonte", 13),
            SourcePath = sourcePath,
        };
    }

    private static void FillDefaults(Dictionary<string, ArgbColor> colors, bool dark)
    {
        string[] fallback = dark
            ? ["#1B1D22", "#262A31", "#E8EAED", "#9AA0A6", "#7AA2F7", "#3A3F47"]
            : ["#F7F8FA", "#FFFFFF", "#1F2328", "#5F6670", "#2F6FEB", "#D9DDE3"];
        for (int i = 0; i < Theme.RequiredColors.Length; i++)
            colors.TryAdd(Theme.RequiredColors[i], ArgbColor.Parse(fallback[i]));
    }

    private static double Number(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    /// <summary>Gera o JSON de um tema (usado ao exportar perfis ou duplicar um tema).</summary>
    public static string Serialize(Theme theme)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("nome", theme.Name);
            w.WriteBoolean("escuro", theme.Dark);
            w.WriteNumber("raio", theme.CornerRadius);
            w.WriteNumber("opacidade", theme.Opacity);
            w.WriteString("fonte", theme.FontFamily);
            w.WriteNumber("tamanhoFonte", theme.FontSize);
            w.WriteStartObject("cores");
            foreach (var (k, v) in theme.Colors) w.WriteString(k, v.ToString());
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Escolhe o tema efetivo: o selecionado, ou (seguindo o Windows) o escuro/claro preferido,
    /// opcionalmente com a cor de destaque do Windows.
    /// </summary>
    public Theme Resolve(string selected, bool followWindows, bool windowsDark, string darkTheme, string lightTheme, ArgbColor? windowsAccent)
    {
        var theme = followWindows ? Get(windowsDark ? darkTheme : lightTheme) : Get(selected);
        return windowsAccent is { } accent ? theme.WithAccent(accent) : theme;
    }
}

using System.Windows.Media;
using AuroraDesk.Core.Logging;

namespace AuroraDesk.Theming;

/// <summary>
/// Resolve nomes de fonte para <see cref="FontFamily"/>. Fontes .ttf/.otf da pasta fonts\ do
/// usuário são carregadas direto do arquivo, sem instalar no sistema. Toda família tem
/// fallback (Segoe UI Variable → Segoe UI), então uma fonte ausente nunca quebra o desenho.
/// </summary>
internal sealed class FontService
{
    public const string Fallback = "Segoe UI Variable Text, Segoe UI";

    private readonly string _folder;
    private readonly Dictionary<string, FontFamily> _cache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, FontFamily>? _private;

    public FontService(string folder) => _folder = folder;

    /// <summary>Famílias das fontes privadas (carregadas sob demanda).</summary>
    public IReadOnlyDictionary<string, FontFamily> PrivateFamilies => _private ??= LoadPrivate();

    public FontFamily Get(string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Segoe UI Variable Text" : name.Trim();
        if (_cache.TryGetValue(name, out var cached)) return cached;

        FontFamily family;
        if (PrivateFamilies.TryGetValue(name, out var priv))
            family = new FontFamily(priv.BaseUri, $"./#{name}, {Fallback}");
        else
            family = new FontFamily($"{name}, {Fallback}");

        if (_cache.Count > 64) _cache.Clear();
        _cache[name] = family;
        return family;
    }

    /// <summary>Chamado quando a pasta de fontes muda.</summary>
    public void Reload()
    {
        _private = null;
        _cache.Clear();
    }

    private Dictionary<string, FontFamily> LoadPrivate()
    {
        var result = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(_folder)) return result;
            if (!Directory.EnumerateFiles(_folder).Any(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                return result;

            var baseUri = new Uri(Path.TrimEndingDirectorySeparator(_folder) + Path.DirectorySeparatorChar);
            foreach (var family in Fonts.GetFontFamilies(baseUri))
            {
                foreach (var familyName in family.FamilyNames.Values)
                    result.TryAdd(familyName, family);
            }
            Log.Info($"Fontes privadas: {string.Join(", ", result.Keys)}");
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao carregar fontes da pasta fonts\\", ex);
        }
        return result;
    }

    /// <summary>Lista para o seletor de fontes: privadas primeiro, depois as instaladas.</summary>
    public IReadOnlyList<(string Name, bool IsPrivate)> ListAll()
    {
        var list = PrivateFamilies.Keys.Select(n => (n, true)).ToList();
        var system = Fonts.SystemFontFamilies
            .Select(f => f.FamilyNames.Values.FirstOrDefault() ?? f.Source)
            .Where(n => !PrivateFamilies.ContainsKey(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => (n, false));
        list.AddRange(system);
        return list;
    }
}

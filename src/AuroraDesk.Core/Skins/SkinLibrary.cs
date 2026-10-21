namespace AuroraDesk.Core.Skins;

/// <summary>Skins na pasta do usuário (uma subpasta por skin, com skin.json) e exemplos embutidos.</summary>
public static class SkinLibrary
{
    private const string ResourcePrefix = "AuroraDesk.widgets";

    /// <summary>Exemplos ligados no primeiro uso, na ordem em que são empilhados na tela.</summary>
    public static readonly string[] DefaultEnabled = ["relogio", "sistema", "rede", "bateria"];

    /// <summary>Nomes das pastas de skins disponíveis.</summary>
    public static IReadOnlyList<string> Discover(string widgetsFolder)
    {
        if (!Directory.Exists(widgetsFolder)) return [];
        return Directory.EnumerateDirectories(widgetsFolder)
            .Where(d => File.Exists(Path.Combine(d, SkinParser.FileName)))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string SkinFile(string widgetsFolder, string skin) => Path.Combine(widgetsFolder, skin, SkinParser.FileName);

    /// <summary>Nomes dos exemplos embutidos.</summary>
    public static IReadOnlyList<string> SampleNames() =>
        Samples().Select(s => s.Skin).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Copia os exemplos que ainda não existem (nunca sobrescreve edições do usuário).</summary>
    public static int InstallSamples(string widgetsFolder, bool overwrite = false)
    {
        int count = 0;
        var asm = typeof(SkinLibrary).Assembly;
        foreach (var (resource, skin, file) in Samples())
        {
            string target = Path.Combine(widgetsFolder, skin, file);
            if (File.Exists(target) && !overwrite) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = asm.GetManifestResourceStream(resource)!;
            using var output = File.Create(target);
            input.CopyTo(output);
            count++;
        }
        return count;
    }

    /// <summary>Lê o texto de um exemplo embutido (usado nos testes).</summary>
    public static string ReadSample(string skin)
    {
        var (resource, _, _) = Samples().First(s => s.Skin.Equals(skin, StringComparison.OrdinalIgnoreCase) && s.File == SkinParser.FileName);
        using var stream = typeof(SkinLibrary).Assembly.GetManifestResourceStream(resource)!;
        return new StreamReader(stream).ReadToEnd();
    }

    private static IEnumerable<(string Resource, string Skin, string File)> Samples()
    {
        foreach (var name in typeof(SkinLibrary).Assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            // "AuroraDesk.widgets\relogio\skin.json"
            var parts = name[(ResourcePrefix.Length + 1)..].Split('\\', '/');
            if (parts.Length < 2) continue;
            yield return (name, parts[0], string.Join(Path.DirectorySeparatorChar, parts[1..]));
        }
    }
}

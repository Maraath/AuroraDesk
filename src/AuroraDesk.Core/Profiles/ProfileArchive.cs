using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using AuroraDesk.Core.Config;

namespace AuroraDesk.Core.Profiles;

/// <summary>Resultado de uma importação.</summary>
public sealed record ImportResult(int Files, IReadOnlyList<string> Skipped, AppConfig? Config);

/// <summary>
/// Perfil compartilhável (.zip): perfil.json (tema, fonte, widgets e dock) + themes\ + widgets\ + fonts\.
/// A importação é defensiva: nada fora das pastas de destino ("zip slip"), sem executáveis
/// e com limites de tamanho.
/// </summary>
public static class ProfileArchive
{
    public const string ManifestName = "perfil.json";
    private const long MaxTotalBytes = 200L * 1024 * 1024;
    private const int MaxEntries = 5000;

    /// <summary>Extensões nunca importadas (um skin pode ter "acao" que abre arquivos da própria pasta).</summary>
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".scr", ".lnk", ".url", ".hta", ".cpl", ".reg", ".jar", ".sys", ".pif", ".appx", ".msix",
    };

    private static readonly string[] Sections = ["themes", "widgets", "fonts"];

    public static void Export(string zipPath, AppConfig config, AppPaths paths)
    {
        string temp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream))
                writer.Write(ManifestFor(config));

            foreach (var (section, folder) in Folders(paths))
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, $"{section}/{relative}", CompressionLevel.Optimal);
                }
            }
        }
        File.Move(temp, zipPath, overwrite: true);
    }

    /// <summary>Só o que faz sentido levar para outro PC (sem flags de primeiro uso nem caminhos de monitor).</summary>
    internal static string ManifestFor(AppConfig config)
    {
        var copy = ConfigStore.Deserialize(ConfigStore.Serialize(config))!;
        foreach (var w in copy.Widgets) w.Monitor = null;
        copy.Dock.Monitor = null;
        var node = JsonNode.Parse(ConfigStore.Serialize(copy))!.AsObject();
        node.Remove("firstRunDone");
        node.Remove("performance");
        node.Remove("weather");
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Extrai o perfil para as pastas do usuário (sobrescrevendo arquivos com o mesmo nome) e
    /// devolve a configuração do perfil já mesclada sobre <paramref name="current"/>.
    /// </summary>
    public static ImportResult Import(string zipPath, AppPaths paths, AppConfig current)
    {
        var skipped = new List<string>();
        int files = 0;
        AppConfig? merged = null;

        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException($"Perfil com arquivos demais ({zip.Entries.Count})");
        long total = zip.Entries.Sum(e => e.Length);
        if (total > MaxTotalBytes) throw new InvalidDataException("Perfil grande demais (limite de 200 MB)");

        var folders = Folders(paths).ToDictionary(p => p.Section, p => Path.GetFullPath(p.Folder), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in zip.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue; // pasta

            if (name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(entry.Open());
                merged = Merge(current, reader.ReadToEnd());
                continue;
            }

            int slash = name.IndexOf('/');
            if (slash <= 0 || !folders.TryGetValue(name[..slash], out var root)) { skipped.Add(name); continue; }
            if (Blocked.Contains(Path.GetExtension(name))) { skipped.Add(name); continue; }

            string target = SafeCombine(root, name[(slash + 1)..]);
            if (target.Length == 0) { skipped.Add(name); continue; }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            files++;
        }
        return new ImportResult(files, skipped, merged);
    }

    /// <summary>Caminho dentro de <paramref name="root"/>, ou vazio se tentar sair dele (../, C:\, \\servidor).</summary>
    internal static string SafeCombine(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) return "";
        string full = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : "";
    }

    private static AppConfig Merge(AppConfig current, string manifest)
    {
        var imported = ConfigStore.Deserialize(manifest) ?? throw new InvalidDataException("perfil.json inválido");
        var result = ConfigStore.Deserialize(ConfigStore.Serialize(current))!;
        result.Theme = imported.Theme;
        result.FollowWindowsTheme = imported.FollowWindowsTheme;
        result.DarkTheme = imported.DarkTheme;
        result.LightTheme = imported.LightTheme;
        result.FollowWindowsAccent = imported.FollowWindowsAccent;
        result.FontOverride = imported.FontOverride;
        result.Widgets = imported.Widgets;
        result.Dock = imported.Dock;
        result.Dock.Initialized = true;
        return result;
    }

    private static IEnumerable<(string Section, string Folder)> Folders(AppPaths paths)
    {
        yield return (Sections[0], paths.Themes);
        yield return (Sections[1], paths.Widgets);
        yield return (Sections[2], paths.Fonts);
    }
}

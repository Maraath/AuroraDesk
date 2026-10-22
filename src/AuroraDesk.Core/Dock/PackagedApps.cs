using AuroraDesk.Core.Config;

namespace AuroraDesk.Core.Dock;

/// <summary>
/// Apps da Microsoft Store (MSIX) fixados pelo caminho do executável. O caminho inclui a versão
/// (WindowsApps\Claude_3.1623.1.0_x64__pzs8sxrjxfjjc\...) e some na primeira atualização; o
/// AppUserModelID (Claude_pzs8sxrjxfjjc!Claude) continua valendo.
/// </summary>
public static class PackagedApps
{
    private const string Marker = @"\WindowsApps\";

    /// <summary>
    /// Nome da família do pacote ("Claude_pzs8sxrjxfjjc") de um caminho em WindowsApps, ou null.
    /// O nome completo é Nome_Versão_Arquitetura_Recurso_Editor; o nome não tem "_".
    /// </summary>
    public static string? FamilyName(string path)
    {
        int at = path.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        string rest = path[(at + Marker.Length)..];
        int slash = rest.IndexOf('\\');
        string fullName = slash < 0 ? rest : rest[..slash];
        string[] parts = fullName.Split('_');
        return parts.Length >= 5 && parts[0].Length > 0 && parts[^1].Length > 0 ? parts[0] + "_" + parts[^1] : null;
    }

    /// <summary>AppUserModelID da família entre os nomes de análise dos apps do menu Iniciar.</summary>
    public static string? FindAppId(string family, IEnumerable<string> startApps) =>
        startApps.FirstOrDefault(a => a.StartsWith(family + "!", StringComparison.OrdinalIgnoreCase));

    /// <summary>Caminhos de itens (e de atalhos dentro de grupos) que ainda apontam para um exe em WindowsApps.</summary>
    public static IEnumerable<string> Candidates(IEnumerable<DockItem> items) =>
        items.SelectMany(i => i.IsGroup ? Candidates(i.Items ?? []) : i.Kind == "arquivo" && FamilyName(i.Path) is not null ? [i.Path] : []);

    public static bool NeedsMigration(IEnumerable<DockItem> items) => Candidates(items).Any();

    /// <summary>Troca o caminho com versão pelo AppUserModelID ("app"). Devolve se algo mudou.</summary>
    public static bool Migrate(IEnumerable<DockItem> items, IReadOnlyCollection<string> startApps)
    {
        bool changed = false;
        foreach (var item in items)
        {
            if (item.IsGroup) { changed |= Migrate(item.Items ?? [], startApps); continue; }
            if (item.Kind != "arquivo" || FamilyName(item.Path) is not { } family) continue;
            if (FindAppId(family, startApps) is not { } appId) continue;
            item.Kind = "app";
            item.Path = appId;
            changed = true;
        }
        return changed;
    }
}

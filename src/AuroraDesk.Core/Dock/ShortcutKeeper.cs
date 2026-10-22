using AuroraDesk.Core.Config;
using AuroraDesk.Core.Logging;

namespace AuroraDesk.Core.Dock;

/// <summary>
/// Guarda uma cópia de cada atalho (.lnk/.url) fixado no dock numa pasta do app. Assim o item
/// continua abrindo mesmo se o atalho original for apagado (ex.: limpar a área de trabalho
/// depois de arrastar os atalhos para um grupo).
/// </summary>
public sealed class ShortcutKeeper(string folder)
{
    public string Folder { get; } = folder;

    public static bool IsShortcut(string path) =>
        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Devolve o caminho da cópia guardada (criando-a se preciso). Arquivos que não são atalho,
    /// que já estão na pasta ou que não existem voltam como estão.
    /// </summary>
    public string Keep(string path)
    {
        if (!IsShortcut(path) || IsKept(path) || !File.Exists(path)) return path;
        try
        {
            Directory.CreateDirectory(Folder);
            string name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            string target = Path.Combine(Folder, name + ext);
            for (int n = 2; File.Exists(target); n++)
            {
                if (SameContent(target, path)) return target; // o mesmo atalho já foi guardado
                target = Path.Combine(Folder, $"{name} ({n}){ext}");
            }
            File.Copy(path, target);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Não foi possível guardar uma cópia do atalho '{path}': {ex.Message}");
            return path;
        }
    }

    /// <summary>Troca, em todos os itens (e dentro dos grupos), atalhos externos pela cópia guardada.</summary>
    public bool KeepAll(IEnumerable<DockItem> items)
    {
        bool changed = false;
        foreach (var item in items)
        {
            if (item.IsGroup) { changed |= KeepAll(item.Items ?? []); continue; }
            if (item.Kind != "arquivo") continue;
            string kept = Keep(item.Path);
            if (kept == item.Path) continue;
            item.Name ??= Path.GetFileNameWithoutExtension(item.Path); // o nome original, sem o "(2)"
            item.Path = kept;
            changed = true;
        }
        return changed;
    }

    private bool IsKept(string path) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path))?.TrimEnd('\\'), Path.GetFullPath(Folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        return fa.Length == fb.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}

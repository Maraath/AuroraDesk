namespace AuroraDesk.Core.Config;

/// <summary>Pastas do usuário. Tudo fica no perfil do usuário atual; nada exige administrador.</summary>
public sealed class AppPaths
{
    public AppPaths(string? roamingRoot = null, string? localRoot = null)
    {
        Roaming = roamingRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuroraDesk");
        Local = localRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuroraDesk");
    }

    public string Roaming { get; }
    public string Local { get; }

    public string ConfigFile => Path.Combine(Roaming, "config.json");
    public string Widgets => Path.Combine(Roaming, "widgets");
    public string Themes => Path.Combine(Roaming, "themes");
    public string Fonts => Path.Combine(Roaming, "fonts");
    public string Backup => Path.Combine(Roaming, "backup");
    /// <summary>Cópias dos atalhos (.lnk/.url) fixados no dock: continuam abrindo se o original for apagado.</summary>
    public string Shortcuts => Path.Combine(Roaming, "atalhos");
    /// <summary>Skins de imagens do dock e das ilhas (uma pasta por skin, com skin.json e PNGs).</summary>
    public string DockSkins => Path.Combine(Roaming, "skins-dock");
    public string Logs => Path.Combine(Local, "logs");
    public string IconCache => Path.Combine(Local, "cache", "icons");

    /// <summary>
    /// Cópias da pasta de dados criadas pelo Windows para apps empacotados (MSIX). Um processo
    /// aberto por um desses apps (ex.: o Claude desktop) tem o AppData redirecionado para
    /// Packages\&lt;pacote&gt;\LocalCache: a configuração gravada lá não é a do usuário.
    /// </summary>
    public IEnumerable<string> FindPackageCopies(string? packagesRoot = null)
    {
        packagesRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        string[] packages;
        try { packages = Directory.GetDirectories(packagesRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var package in packages)
        {
            string copy = Path.Combine(package, "LocalCache", "Roaming", Path.GetFileName(Roaming));
            if (File.Exists(Path.Combine(copy, "config.json"))) yield return copy;
        }
    }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { Roaming, Widgets, Themes, Fonts, Backup, Shortcuts, DockSkins, Logs, IconCache })
            Directory.CreateDirectory(dir);
    }
}

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
    public string Logs => Path.Combine(Local, "logs");
    public string IconCache => Path.Combine(Local, "cache", "icons");

    public void EnsureCreated()
    {
        foreach (var dir in new[] { Roaming, Widgets, Themes, Fonts, Backup, Shortcuts, Logs, IconCache })
            Directory.CreateDirectory(dir);
    }
}

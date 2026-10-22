namespace AuroraDesk.Core.Dock;

/// <summary>
/// Identidade de um aplicativo para agrupar janelas com itens fixados: AppUserModelID
/// (UWP e apps que o definem) e/ou caminho do executável.
/// </summary>
public readonly record struct AppIdentity(string? AppId, string? ExePath)
{
    public bool IsEmpty => string.IsNullOrEmpty(AppId) && string.IsNullOrEmpty(ExePath);

    /// <summary>Mesma aplicação se o AppId coincide, ou (sem AppId em um dos lados) o executável coincide.</summary>
    public bool Matches(AppIdentity other)
    {
        if (!string.IsNullOrEmpty(AppId) && !string.IsNullOrEmpty(other.AppId))
            return string.Equals(AppId, other.AppId, StringComparison.OrdinalIgnoreCase);
        // App da Store fixado pelo AppUserModelID e janela que só informa o exe (ex.: Claude, em
        // Electron): o mesmo app se o exe está na pasta do pacote dessa família.
        if (SamePackage(this, other) || SamePackage(other, this)) return true;
        return !string.IsNullOrEmpty(ExePath) && !string.IsNullOrEmpty(other.ExePath)
            && string.Equals(NormalizePath(ExePath), NormalizePath(other.ExePath), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePackage(AppIdentity withId, AppIdentity withExe) =>
        !string.IsNullOrEmpty(withId.AppId) && string.IsNullOrEmpty(withExe.AppId) && !string.IsNullOrEmpty(withExe.ExePath)
        && PackagedApps.FamilyName(withExe.ExePath) is { } family
        && withId.AppId.StartsWith(family + "!", StringComparison.OrdinalIgnoreCase);

    public static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        }
        catch
        {
            return path;
        }
    }

    /// <summary>Chave estável (cache de ícones, agrupamento).</summary>
    public string Key => !string.IsNullOrEmpty(AppId) ? "id:" + AppId.ToLowerInvariant() : "exe:" + NormalizePath(ExePath ?? "").ToLowerInvariant();
}

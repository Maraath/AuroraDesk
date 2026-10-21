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
        return !string.IsNullOrEmpty(ExePath) && !string.IsNullOrEmpty(other.ExePath)
            && string.Equals(NormalizePath(ExePath), NormalizePath(other.ExePath), StringComparison.OrdinalIgnoreCase);
    }

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

using AuroraDesk.Core.Logging;
using AuroraDesk.Core.Search;
using AuroraDesk.Interop;

namespace AuroraDesk.Search;

/// <summary>
/// Lista de apps do menu Iniciar para a pesquisa. Lida em segundo plano na primeira pesquisa e
/// guardada por 10 minutos (só nomes, poucos KB), para reabrir a pesquisa na hora.
/// </summary>
internal static class AppCatalog
{
    private static Task<IReadOnlyList<SearchItem>>? _loading;
    private static DateTime _loadedAt;

    public static Task<IReadOnlyList<SearchItem>> GetAsync()
    {
        if (_loading is null || (_loading.IsCompleted && DateTime.Now - _loadedAt > TimeSpan.FromMinutes(10)))
        {
            _loadedAt = DateTime.Now;
            _loading = Task.Run(Load);
        }
        return _loading;
    }

    private static IReadOnlyList<SearchItem> Load()
    {
        try
        {
            var started = DateTime.Now;
            var apps = ShellInterop.ListStartApps()
                // Atalhos de desinstalação, leia-me e ajuda poluem a pesquisa.
                .Where(a => !a.Name.Contains("Desinstalar", StringComparison.OrdinalIgnoreCase)
                            && !a.Name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)
                            && !a.ParsingName.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                            && !a.ParsingName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                            && !a.ParsingName.EndsWith(".chm", StringComparison.OrdinalIgnoreCase)
                            && !a.ParsingName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                .DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => new SearchItem(a.Name, a.ParsingName, ResultKind.App))
                .ToList();
            Log.Info($"Pesquisa: {apps.Count} apps do menu Iniciar em {(DateTime.Now - started).TotalMilliseconds:0} ms");
            return apps;
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível listar os apps do menu Iniciar", ex);
            return [];
        }
    }
}

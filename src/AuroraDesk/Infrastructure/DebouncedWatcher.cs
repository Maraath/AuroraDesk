using System.Windows.Threading;

namespace AuroraDesk.Infrastructure;

/// <summary>
/// Observa uma pasta e chama <see cref="Changed"/> na thread de interface, uma vez por rajada
/// de alterações (editores costumam gravar o arquivo várias vezes seguidas).
/// </summary>
internal sealed class DebouncedWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public DebouncedWatcher(string folder, string filter, bool recursive, Dispatcher dispatcher, int delayMs = 300)
    {
        Directory.CreateDirectory(folder);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(delayMs), DispatcherPriority.Background, OnTick, dispatcher) { IsEnabled = false };
        _watcher = new FileSystemWatcher(folder, filter)
        {
            IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 8192,
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Renamed += (s, e) => { OnFsEvent(s, e); Add(e.OldFullPath); };
        _watcher.Error += (_, e) => Core.Logging.Log.Warn($"Observador de pasta: {e.GetException().Message}");
        _watcher.EnableRaisingEvents = true;
        _dispatcher = dispatcher;
    }

    private readonly Dispatcher _dispatcher;

    /// <summary>Caminhos alterados desde o último aviso.</summary>
    public event Action<IReadOnlyCollection<string>>? Changed;

    private void OnFsEvent(object sender, FileSystemEventArgs e) => Add(e.FullPath);

    private void Add(string path)
    {
        lock (_lock) _pending.Add(path);
        // Reinicia a espera na thread de interface (DispatcherTimer não é thread-safe).
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => { _timer.Stop(); _timer.Start(); });
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        string[] paths;
        lock (_lock)
        {
            paths = [.. _pending];
            _pending.Clear();
        }
        if (paths.Length == 0) return;
        try { Changed?.Invoke(paths); }
        catch (Exception ex) { Core.Logging.Log.Error("Erro ao recarregar após mudança de arquivo", ex); }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Stop();
    }
}

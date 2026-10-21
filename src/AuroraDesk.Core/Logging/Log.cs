using System.Text;

namespace AuroraDesk.Core.Logging;

/// <summary>
/// Log mínimo em arquivo, rotativo (até 2 arquivos de 512 KB). Sem dependências e sem
/// thread própria: cada linha é gravada com o arquivo aberto em modo de anexar.
/// </summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static string? FilePath => _path;

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "aurora.log");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("AVISO", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERRO", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        var path = _path;
        if (path is null) return;
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch
            {
                // Log nunca pode derrubar o app.
            }
        }
    }
}

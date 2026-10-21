using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using AuroraDesk.Core.Logging;
using AuroraDesk.Interop;

namespace AuroraDesk.Dock;

/// <summary>
/// Ícones do dock: cache em memória (limitado, LRU) + cache em disco (PNG). Ler o PNG é muito
/// mais leve que pedir ao shell, que carrega extensões e DLLs no processo.
/// </summary>
internal sealed class IconCache(string folder, int capacity = 96)
{
    private readonly Dictionary<string, (BitmapSource Image, long Used)> _memory = new(StringComparer.OrdinalIgnoreCase);
    private long _clock;

    public int Count => _memory.Count;

    /// <summary>Ícone de um caminho do shell no tamanho pedido (pixels físicos).</summary>
    public BitmapSource? Get(string parsingName, int size)
    {
        string key = parsingName + "|" + size;
        if (_memory.TryGetValue(key, out var hit))
        {
            _memory[key] = (hit.Image, ++_clock);
            return hit.Image;
        }

        var image = LoadFromDisk(key, parsingName) ?? LoadFromShell(key, parsingName, size);
        if (image is null) return null;
        if (_memory.Count >= capacity)
        {
            var oldest = _memory.MinBy(p => p.Value.Used).Key;
            _memory.Remove(oldest);
        }
        _memory[key] = (image, ++_clock);
        return image;
    }

    public void Clear() => _memory.Clear();

    private string DiskPath(string key)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key.ToLowerInvariant()));
        return Path.Combine(folder, Convert.ToHexString(hash)[..20] + ".png");
    }

    private BitmapSource? LoadFromDisk(string key, string parsingName)
    {
        string path = DiskPath(key);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            // Arquivo de origem mudou depois do cache? Recria. Itens sem arquivo expiram em 7 dias.
            bool stale = File.Exists(parsingName) || Directory.Exists(parsingName)
                ? File.GetLastWriteTimeUtc(parsingName) > info.LastWriteTimeUtc
                : DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromDays(7);
            if (stale) return null;

            using var stream = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            Log.Warn($"Cache de ícone corrompido ({Path.GetFileName(path)}): {ex.Message}");
            return null;
        }
    }

    private BitmapSource? LoadFromShell(string key, string parsingName, int size)
    {
        var image = ShellInterop.GetIcon(parsingName, size);
        if (image is null) return null;
        try
        {
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(DiskPath(key));
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível gravar o cache de ícone: {ex.Message}");
        }
        return image;
    }
}

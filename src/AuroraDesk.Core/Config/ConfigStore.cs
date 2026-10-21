using System.Text.Json;
using AuroraDesk.Core.Logging;

namespace AuroraDesk.Core.Config;

/// <summary>Lê e grava config.json de forma atômica. Arquivo corrompido vira .corrompido e o app segue com o padrão.</summary>
public sealed class ConfigStore(string path)
{
    public string Path { get; } = path;

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppConfig();
            using var stream = File.OpenRead(Path);
            return JsonSerializer.Deserialize(stream, ConfigJsonContext.Default.AppConfig) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("config.json inválido; usando o padrão", ex);
            try { File.Copy(Path, Path + ".corrompido", overwrite: true); } catch { }
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, config, ConfigJsonContext.Default.AppConfig);
        File.Move(temp, Path, overwrite: true);
    }

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);

    public static AppConfig? Deserialize(string json) => JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig);
}

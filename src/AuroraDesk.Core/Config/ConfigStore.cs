using System.Text.Json;
using AuroraDesk.Core.Logging;

namespace AuroraDesk.Core.Config;

/// <summary>
/// Lê e grava config.json de forma atômica, sem nunca trocar a configuração do usuário pelo
/// padrão por causa de uma falha passageira:
/// <list type="bullet">
/// <item>Arquivo preso por outro programa (antivírus, sincronização...): tenta de novo por alguns segundos.</item>
/// <item>Cada leitura boa deixa uma cópia em config.json.bak; arquivo corrompido ou ilegível usa a cópia.</item>
/// <item>Se nada puder ser lido, o app segue com o padrão em <see cref="ReadOnly"/>: nada é gravado,
/// para não apagar grupos, ilhas e atalhos do usuário.</item>
/// </list>
/// </summary>
public sealed class ConfigStore(string path, int attempts = 15, int delayMs = 200)
{
    public string Path { get; } = path;
    public string BackupPath => Path + ".bak";

    /// <summary>A configuração existente não pôde ser lida: gravar agora apagaria a do usuário.</summary>
    public bool ReadOnly { get; private set; }

    public AppConfig Load()
    {
        ReadOnly = false;
        if (!File.Exists(Path)) return File.Exists(BackupPath) ? LoadBackup("config.json não existe") : new AppConfig();
        try
        {
            var config = Retry(() =>
            {
                using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize(stream, ConfigJsonContext.Default.AppConfig);
            }) ?? throw new JsonException("config.json vazio");
            try { Retry(() => { File.Copy(Path, BackupPath, overwrite: true); return true; }); }
            catch (Exception ex) when (IsIo(ex)) { Log.Warn($"Não foi possível atualizar config.json.bak: {ex.Message}"); }
            return config;
        }
        catch (JsonException ex)
        {
            Log.Error("config.json inválido", ex);
            try { File.Copy(Path, Path + ".corrompido", overwrite: true); } catch { }
            return LoadBackup("config.json inválido");
        }
        catch (Exception ex) when (IsIo(ex))
        {
            Log.Error("config.json não pôde ser lido (preso por outro programa?)", ex);
            return LoadBackup("config.json ilegível");
        }
    }

    private AppConfig LoadBackup(string reason)
    {
        try
        {
            var config = Retry(() =>
            {
                using var stream = new FileStream(BackupPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize(stream, ConfigJsonContext.Default.AppConfig);
            });
            if (config is not null)
            {
                Log.Warn($"{reason}: usando a cópia config.json.bak");
                return config;
            }
        }
        catch (Exception ex) when (ex is JsonException || IsIo(ex)) { Log.Error("config.json.bak também não pôde ser lido", ex); }

        // Sem nenhuma cópia legível: segue com o padrão, mas sem gravar por cima do arquivo do usuário.
        ReadOnly = File.Exists(Path);
        if (ReadOnly) Log.Error($"{reason}: usando o padrão SEM salvar, para não apagar a configuração existente. Reabra o app.");
        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        if (ReadOnly) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, config, ConfigJsonContext.Default.AppConfig);
        Retry(() => { File.Move(temp, Path, overwrite: true); return true; });
    }

    private T Retry<T>(Func<T> action)
    {
        for (int i = 1; ; i++)
        {
            try { return action(); }
            catch (Exception ex) when (IsIo(ex) && i < attempts) { Thread.Sleep(delayMs); }
        }
    }

    private static bool IsIo(Exception ex) => ex is IOException or UnauthorizedAccessException;

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);

    public static AppConfig? Deserialize(string json) => JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig);
}

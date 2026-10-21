using AuroraDesk.Core.Backup;
using AuroraDesk.Core.Logging;
using Microsoft.Win32;

namespace AuroraDesk.Infrastructure;

/// <summary>Registro do usuário atual (HKCU). Nada aqui exige administrador.</summary>
internal sealed class UserRegistry : IUserRegistry
{
    public string? Get(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name) as string;
    }

    public void Set(string key, string name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>"Iniciar com o Windows" pela chave Run do usuário, com backup do valor original.</summary>
internal sealed class StartupRegistration(OriginalValues backup, IUserRegistry registry)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AuroraDesk";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public bool IsEnabled => registry.Get(RunKey, ValueName) is not null;

    /// <summary>Aponta para outro executável (ex.: o app foi movido)?</summary>
    public bool PointsElsewhere => registry.Get(RunKey, ValueName) is { } v && !v.Equals(Command, StringComparison.OrdinalIgnoreCase);

    public void Set(bool enabled)
    {
        try
        {
            if (enabled) backup.Set(RunKey, ValueName, Command);
            else backup.Delete(RunKey, ValueName);
            Log.Info(enabled ? "Iniciar com o Windows: ligado" : "Iniciar com o Windows: desligado");
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível alterar 'Iniciar com o Windows'", ex);
        }
    }
}

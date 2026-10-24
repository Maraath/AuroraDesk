using AuroraDesk.Core.Backup;
using AuroraDesk.Core.Logging;
using Microsoft.Win32;

namespace AuroraDesk.Infrastructure;

/// <summary>
/// Registro do usuário atual (HKCU). Nada aqui exige administrador. Valores como texto
/// codificado (<see cref="RegistryValueCodec"/>): texto, "dword:N" ou "binary:HEX".
/// </summary>
internal sealed class UserRegistry : IUserRegistry
{
    public string? Get(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        object? value = k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value is null ? null : RegistryValueCodec.Encode(value);
    }

    public void Set(string key, string name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        switch (RegistryValueCodec.Decode(value))
        {
            case uint u: k.SetValue(name, unchecked((int)u), RegistryValueKind.DWord); break;
            case byte[] b: k.SetValue(name, b, RegistryValueKind.Binary); break;
            default: k.SetValue(name, value, RegistryValueKind.String); break;
        }
    }

    public void Delete(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>
/// "Iniciar com o Windows" por uma tarefa agendada no logon do usuário (sem administrador).
/// Os programas da chave Run só abrem ~20 s depois do logon, porque o Windows os atrasa de
/// propósito; a tarefa abre o app assim que o usuário entra, com prioridade normal (a padrão das
/// tarefas é "abaixo do normal"). A chave Run fica só como reserva, se a tarefa não puder ser criada.
/// Versões anteriores usavam a chave Run: <see cref="Migrate"/> converte a entrada.
/// </summary>
internal sealed class StartupRegistration(IUserRegistry registry)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AuroraDesk";
    private const string TaskName = "AuroraDesk";

    /// <summary>Argumento da abertura pelo logon: espera o Explorer e não abre as configurações se já houver uma instância.</summary>
    public const string LogonArgument = "--logon";

    private static string Exe => Environment.ProcessPath!;

    /// <summary>Como o app inicia com o Windows agora. Roda o schtasks: chame fora de laços.</summary>
    public (bool Enabled, bool PointsElsewhere) Read()
    {
        try
        {
            string? target = TaskTarget() ?? RunTarget();
            return (target is not null, target is not null && !SameFile(target, Exe));
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível ler 'Iniciar com o Windows'", ex);
            return (false, false);
        }
    }

    public void Set(bool enabled)
    {
        try
        {
            if (enabled && CreateTask())
            {
                registry.Delete(RunKey, ValueName);
                Log.Info("Iniciar com o Windows: ligado (tarefa agendada no logon)");
            }
            else if (enabled)
            {
                registry.Set(RunKey, ValueName, $"\"{Exe}\" {LogonArgument}");
                Log.Warn("Iniciar com o Windows: ligado pela chave Run (a tarefa agendada não pôde ser criada)");
            }
            else
            {
                Schtasks($"/Delete /TN {TaskName} /F", out _);
                registry.Delete(RunKey, ValueName);
                Log.Info("Iniciar com o Windows: desligado");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível alterar 'Iniciar com o Windows'", ex);
        }
    }

    /// <summary>Troca a entrada antiga da chave Run (deste executável) pela tarefa agendada.</summary>
    public void Migrate()
    {
        try
        {
            if (RunTarget() is { } target && SameFile(target, Exe)) Set(true);
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível migrar 'Iniciar com o Windows' para a tarefa agendada", ex);
        }
    }

    /// <summary>Executável da entrada da chave Run (com ou sem aspas e argumentos), ou null.</summary>
    private string? RunTarget()
    {
        if (registry.Get(RunKey, ValueName) is not { Length: > 0 } value) return null;
        value = value.Trim();
        if (value.StartsWith('"')) return value[1..Math.Max(1, value.IndexOf('"', 1))];
        int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? value[..(exe + 4)] : value;
    }

    /// <summary>Executável da tarefa agendada, ou null se ela não existir.</summary>
    private static string? TaskTarget()
    {
        if (Schtasks($"/Query /TN {TaskName} /XML ONE", out string xml) != 0) return null;
        int start = xml.IndexOf("<Command>", StringComparison.Ordinal);
        int end = xml.IndexOf("</Command>", StringComparison.Ordinal);
        if (start < 0 || end < start) return null;
        return System.Net.WebUtility.HtmlDecode(xml[(start + 9)..end]).Trim().Trim('"');
    }

    private static bool CreateTask()
    {
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        string Esc(string s) => System.Security.SecurityElement.Escape(s);
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Abre o Aurora Desk ao entrar no Windows.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>false</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>"{Esc(Exe)}"</Command><Arguments>{LogonArgument}</Arguments><WorkingDirectory>{Esc(Path.GetDirectoryName(Exe)!)}</WorkingDirectory></Exec></Actions>
            </Task>
            """;
        string file = Path.Combine(Path.GetTempPath(), $"aurora-tarefa-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode); // o schtasks exige UTF-16
            if (Schtasks($"/Create /TN {TaskName} /XML \"{file}\" /F", out string output) == 0) return true;
            Log.Warn($"schtasks recusou a tarefa: {output.Trim()}");
            return false;
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private static int Schtasks(string args, out string output)
    {
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(info)!;
        var error = p.StandardError.ReadToEndAsync();
        output = p.StandardOutput.ReadToEnd() + error.Result;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}

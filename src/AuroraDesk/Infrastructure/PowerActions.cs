using System.Diagnostics;
using AuroraDesk.Core.Logging;
using AuroraDesk.Interop;
using AuroraDesk.Tray;
using Microsoft.Win32;

namespace AuroraDesk.Infrastructure;

/// <summary>
/// Opções de energia do menu Iniciar do Windows: bloquear, suspender, hibernar, reiniciar e desligar.
/// Desligar e reiniciar usam o shutdown.exe (não exige privilégio extra no processo); suspender e
/// hibernar usam a mesma API do Windows.
/// </summary>
internal static class PowerActions
{
    /// <summary>Menu com as opções, na ordem do menu Iniciar (a mais "forte" por último).</summary>
    public static NativeMenu Menu()
    {
        var menu = new NativeMenu()
            .Item("Bloquear", Lock)
            .Item("Suspender", Sleep);
        if (HibernateAvailable()) menu.Item("Hibernar", Hibernate);
        return menu
            .Separator()
            .Item("Reiniciar", Restart)
            .Item("Desligar", Shutdown);
    }

    public static void Lock() => Run("bloquear", () => Win32.LockWorkStation());

    public static void Sleep() => Run("suspender", () => Win32.SetSuspendState(false, false, false));

    public static void Hibernate() => Run("hibernar", () => Win32.SetSuspendState(true, false, false));

    public static void Restart() => Run("reiniciar", () => ShutdownExe("/r /t 0"));

    /// <summary>Com a inicialização rápida ligada, desliga como o menu Iniciar (/hybrid), e o próximo boot é mais rápido.</summary>
    public static void Shutdown() => Run("desligar", () => ShutdownExe(FastStartup() ? "/s /hybrid /t 0" : "/s /t 0"));

    private static bool ShutdownExe(string args)
    {
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        return p is not null;
    }

    private static void Run(string name, Func<bool> action)
    {
        try
        {
            Log.Info($"Energia: {name}");
            if (!action()) Log.Warn($"Energia: o Windows recusou '{name}'");
        }
        catch (Exception ex)
        {
            Log.Error($"Energia: falha ao {name}", ex);
        }
    }

    private static bool FastStartup()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            return key?.GetValue("HiberbootEnabled") is int v && v != 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>"Hibernar" só aparece no menu Iniciar quando ligado nas opções de energia; aqui, quando o PC permite.</summary>
    private static bool HibernateAvailable()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FlyoutMenuSettings");
            return key?.GetValue("ShowHibernateOption") is int v && v != 0 && Win32.IsPwrHibernateAllowed();
        }
        catch (Exception) { return false; }
    }
}

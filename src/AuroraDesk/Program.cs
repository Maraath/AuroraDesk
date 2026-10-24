using System.Runtime.CompilerServices;
using AuroraDesk.Interop;

namespace AuroraDesk;

internal static class Program
{
    /// <summary>
    /// Ponto de entrada. Garante instância única antes de carregar o WPF.
    /// Argumentos: --sair (fecha a instância aberta), --configuracoes (abre as configurações),
    /// --logon (aberto com o Windows: espera o Explorer), --registrar-inicio e --remover-inicio
    /// (instalador), --vigia &lt;pid&gt; (uso interno: restaura a barra de tarefas se o app cair).
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // O vigia roda antes de tudo e não toca em WPF nem na instância única.
        int watch = Array.FindIndex(args, a => a.Equals("--vigia", StringComparison.OrdinalIgnoreCase));
        if (watch >= 0 && watch + 1 < args.Length && int.TryParse(args[watch + 1], out int watched))
            return Infrastructure.TaskbarWatchdog.RunWatchdog(watched, BackupFile());

        // Usados pelo instalador: só ligam/desligam "Iniciar com o Windows" e saem.
        if (Has(args, "--registrar-inicio")) return SetStartup(true);
        if (Has(args, "--remover-inicio")) return SetStartup(false);

        bool exit = Has(args, "--sair");
        bool logon = Has(args, Infrastructure.StartupRegistration.LogonArgument);

        // --reiniciar <pid>: espera a instância anterior fechar antes de tentar a instância única.
        int restart = Array.FindIndex(args, a => a.Equals("--reiniciar", StringComparison.OrdinalIgnoreCase));
        if (restart >= 0 && restart + 1 < args.Length && int.TryParse(args[restart + 1], out int pid))
        {
            try { System.Diagnostics.Process.GetProcessById(pid).WaitForExit(10_000); }
            catch (ArgumentException) { } // já fechou
        }

        using var mutex = new Mutex(true, @"Local\AuroraDesk.Instancia", out bool created);
        if (!created)
        {
            // Aberto pelo logon com o app já aberto (ex.: tarefa e chave Run antigas juntas): nada a fazer.
            if (!logon) ControlWindow.SendToRunningInstance(exit ? ControlWindow.CommandExit : ControlWindow.CommandOpenSettings);
            return 0;
        }
        if (exit) return 0;

        return RunApp(Has(args, "--configuracoes"), logon);
    }

    private static bool Has(string[] args, string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Separado do Main para o vigia não carregar as DLLs do WPF ao compilar o Main.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApp(bool openSettings, bool logon) => new App(openSettings, logon).Run();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int SetStartup(bool enabled)
    {
        new Infrastructure.StartupRegistration(new Infrastructure.UserRegistry()).Set(enabled);
        return 0;
    }

    internal static string BackupFile() => Path.Combine(new Core.Config.AppPaths().Backup, "barra-de-tarefas.json");
}

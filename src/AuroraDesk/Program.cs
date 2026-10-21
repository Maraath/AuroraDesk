using AuroraDesk.Interop;

namespace AuroraDesk;

internal static class Program
{
    /// <summary>
    /// Ponto de entrada. Garante instância única antes de carregar o WPF.
    /// Argumentos: --sair (fecha a instância aberta), --configuracoes (abre as configurações).
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        bool exit = args.Any(a => a.Equals("--sair", StringComparison.OrdinalIgnoreCase));

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
            ControlWindow.SendToRunningInstance(exit ? ControlWindow.CommandExit : ControlWindow.CommandOpenSettings);
            return 0;
        }
        if (exit) return 0;

        bool openSettings = args.Any(a => a.Equals("--configuracoes", StringComparison.OrdinalIgnoreCase));
        var app = new App(openSettings);
        return app.Run();
    }
}

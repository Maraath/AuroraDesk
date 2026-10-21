using System.Runtime;
using System.Windows.Threading;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Logging;
using AuroraDesk.Interop;

namespace AuroraDesk.Infrastructure;

/// <summary>
/// Devolve ao Windows a memória que o app não está usando: depois da inicialização e ao fechar a
/// janela de configurações. Faz uma coleta compactando o heap e então reduz o working set
/// (as páginas voltam sob demanda, sem perder nada). No máximo uma vez a cada 5 minutos.
/// </summary>
internal sealed class MemoryTrimmer(PerformanceConfig config, Dispatcher dispatcher)
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);
    private DateTime _last = DateTime.MinValue;
    private DispatcherTimer? _delay;

    public DateTime LastTrim => _last;

    /// <summary>Agenda uma redução para daqui a <paramref name="delay"/> (cancela a anterior).</summary>
    public void Schedule(TimeSpan delay, bool force = false)
    {
        _delay?.Stop();
        _delay = new DispatcherTimer(delay, DispatcherPriority.ApplicationIdle, (_, _) => { _delay!.Stop(); TrimNow(force); }, dispatcher);
        _delay.Start();
    }

    public void TrimNow(bool force = false)
    {
        if (!config.TrimWorkingSet && !force) return;
        if (!force && DateTime.Now - _last < MinInterval) return;
        _last = DateTime.Now;

        long before = Environment.WorkingSet;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        Win32.SetProcessWorkingSetSize(Win32.GetCurrentProcess(), -1, -1);
        Log.Info($"Memória liberada: working set {before / 1048576.0:0.0} → {Environment.WorkingSet / 1048576.0:0.0} MB");
    }
}

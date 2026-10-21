namespace AuroraDesk.Core.Measures;

/// <summary>
/// Contexto de uma rodada do agendador. As amostras de sistema (CPU, rede) são lidas
/// no máximo uma vez por rodada, mesmo que várias medidas dependam delas.
/// </summary>
public sealed class SampleContext
{
    internal SampleContext(SystemSources sources, DateTimeOffset now, long tick)
    {
        Sources = sources;
        Now = now;
        Tick = tick;
    }

    public SystemSources Sources { get; }
    public DateTimeOffset Now { get; }
    public long Tick { get; }
}

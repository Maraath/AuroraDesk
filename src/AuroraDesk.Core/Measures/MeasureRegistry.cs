namespace AuroraDesk.Core.Measures;

/// <summary>
/// Catálogo de tipos de medida. Para criar uma medida nova, escreva uma classe que herda de
/// <see cref="Measure"/> e registre-a aqui com <see cref="Register"/>; o motor não muda.
/// </summary>
public sealed class MeasureRegistry
{
    private readonly Dictionary<string, Func<MeasureSpec, Measure>> _factories = new(StringComparer.OrdinalIgnoreCase);

    public static MeasureRegistry CreateDefault()
    {
        var r = new MeasureRegistry();
        r.Register("cpu", s => new CpuMeasure(s));
        r.Register("memoria", s => new MemoryMeasure(s));
        r.Register("ram", s => new MemoryMeasure(s));
        r.Register("disco", s => new DiskMeasure(s));
        r.Register("rede", s => new NetworkMeasure(s));
        r.Register("bateria", s => new BatteryMeasure(s));
        r.Register("relogio", s => new ClockMeasure(s));
        r.Register("uptime", s => new UptimeMeasure(s));
        return r;
    }

    public void Register(string type, Func<MeasureSpec, Measure> factory) => _factories[type] = factory;

    public bool IsKnown(string type) => _factories.ContainsKey(type);

    public IEnumerable<string> Types => _factories.Keys;

    public Measure Create(MeasureSpec spec) =>
        _factories.TryGetValue(spec.Type, out var f)
            ? f(spec)
            : throw new ArgumentException($"Tipo de medida desconhecido: '{spec.Type}'");
}

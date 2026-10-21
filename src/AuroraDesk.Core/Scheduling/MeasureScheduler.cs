using AuroraDesk.Core.Measures;

namespace AuroraDesk.Core.Scheduling;

/// <summary>
/// Agendador central. Um único timer de disparo único é reprogramado para o próximo
/// vencimento: sem medidas, ele não acorda; com uma medida de 1 s, acorda 1 vez por segundo.
/// As leituras rodam fora da thread de interface e só as medidas que mudaram são
/// entregues, em lote, por <see cref="Changed"/> (via o delegate de postagem).
/// </summary>
public sealed class MeasureScheduler : IDisposable
{
    private sealed class Entry(Measure measure)
    {
        public Measure Measure { get; } = measure;
        public List<TimeSpan> Intervals { get; } = [];
        public TimeSpan Interval { get; set; }
        public DateTimeOffset NextDue { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly MeasureRegistry _registry;
    private readonly Action<Action> _post;
    private readonly TimeProvider _time;
    private readonly SystemSources _sources = new();
    private readonly ITimer? _timer;
    private long _tick;
    private bool _paused;
    private double _multiplier = 1;
    private bool _disposed;

    /// <param name="post">Como entregar o lote de mudanças (ex.: Dispatcher.BeginInvoke).</param>
    /// <param name="useTimer">Falso nos testes, que chamam <see cref="RunDue"/> diretamente.</param>
    public MeasureScheduler(MeasureRegistry registry, Action<Action> post, TimeProvider? time = null, bool useTimer = true)
    {
        _registry = registry;
        _post = post;
        _time = time ?? TimeProvider.System;
        if (useTimer)
            _timer = _time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Medidas que mudaram nesta rodada. Chamado pelo delegate de postagem.</summary>
    public event Action<IReadOnlyList<Measure>>? Changed;

    /// <summary>Número de rodadas executadas (aparece na aba Desempenho).</summary>
    public long TickCount => Interlocked.Read(ref _tick);

    public int ActiveMeasures { get { lock (_lock) return _entries.Count; } }

    /// <summary>Pausa todas as leituras (tela bloqueada, tela cheia...).</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            lock (_lock)
            {
                if (_paused == value) return;
                _paused = value;
                if (!value)
                {
                    var now = _time.GetUtcNow();
                    foreach (var e in _entries.Values) e.NextDue = now;
                }
            }
            Reschedule(TimeSpan.Zero);
        }
    }

    /// <summary>Multiplica todos os intervalos (ex.: 2 no modo bateria).</summary>
    public double IntervalMultiplier
    {
        get => _multiplier;
        set
        {
            lock (_lock)
            {
                _multiplier = Math.Clamp(value, 1, 10);
                foreach (var e in _entries.Values) e.Interval = Effective(e);
            }
        }
    }

    /// <summary>Assina uma medida. Pedidos com a mesma chave compartilham a instância.</summary>
    public Measure Subscribe(MeasureSpec spec)
    {
        Entry entry;
        lock (_lock)
        {
            if (!_entries.TryGetValue(spec.Key, out entry!))
            {
                entry = new Entry(_registry.Create(spec)) { NextDue = _time.GetUtcNow() };
                _entries[spec.Key] = entry;
            }
            else
            {
                entry.NextDue = _time.GetUtcNow(); // novo assinante recebe valor logo
            }
            var requested = spec.IntervalMs is > 0 ? TimeSpan.FromMilliseconds(Math.Max(spec.IntervalMs.Value, 100)) : entry.Measure.DefaultInterval;
            entry.Intervals.Add(requested);
            entry.Interval = Effective(entry);
        }
        Reschedule(TimeSpan.Zero);
        return entry.Measure;
    }

    public void Unsubscribe(MeasureSpec spec)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(spec.Key, out var entry)) return;
            var requested = spec.IntervalMs is > 0 ? TimeSpan.FromMilliseconds(Math.Max(spec.IntervalMs.Value, 100)) : entry.Measure.DefaultInterval;
            int i = entry.Intervals.IndexOf(requested);
            entry.Intervals.RemoveAt(i >= 0 ? i : 0);
            if (entry.Intervals.Count == 0) _entries.Remove(spec.Key);
            else entry.Interval = Effective(entry);
        }
    }

    private TimeSpan Effective(Entry e) => (e.Intervals.Count == 0 ? e.Measure.DefaultInterval : e.Intervals.Min()) * _multiplier;

    /// <summary>
    /// Lê as medidas vencidas em <paramref name="now"/>. Devolve as que mudaram e o tempo até o
    /// próximo vencimento (null quando não há nada agendado).
    /// </summary>
    internal (List<Measure> Changed, TimeSpan? Next) RunDue(DateTimeOffset now)
    {
        var changed = new List<Measure>();
        lock (_lock)
        {
            if (_paused || _entries.Count == 0) return (changed, null);

            var context = new SampleContext(_sources, now, Interlocked.Increment(ref _tick));
            DateTimeOffset? next = null;
            foreach (var e in _entries.Values)
            {
                if (e.NextDue <= now)
                {
                    if (e.Measure.Update(context)) changed.Add(e.Measure);
                    e.NextDue = e.Measure.NextDue(now, e.Interval);
                    if (e.NextDue <= now) e.NextDue = now + TimeSpan.FromMilliseconds(100);
                }
                if (next is null || e.NextDue < next) next = e.NextDue;
            }
            return (changed, next - now);
        }
    }

    private void OnTimer()
    {
        if (_disposed) return;
        List<Measure> changed;
        TimeSpan? next;
        try
        {
            (changed, next) = RunDue(_time.GetUtcNow());
        }
        catch (Exception ex)
        {
            Logging.Log.Error("Erro no agendador", ex);
            changed = [];
            next = TimeSpan.FromSeconds(1);
        }

        if (changed.Count > 0 && Changed is { } handler)
            _post(() => handler(changed));

        if (next is { } delay) Reschedule(delay);
    }

    private void Reschedule(TimeSpan delay)
    {
        if (_disposed || _timer is null) return;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
    }
}

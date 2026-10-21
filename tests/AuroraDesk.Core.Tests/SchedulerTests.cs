using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Scheduling;

namespace AuroraDesk.Core.Tests;

public class SchedulerTests
{
    /// <summary>Medida de teste: conta leituras e muda de valor só quando mandamos.</summary>
    private sealed class CountingMeasure(MeasureSpec spec) : Measure(spec)
    {
        public int Reads;
        public double Next;
        public override TimeSpan DefaultInterval => TimeSpan.FromSeconds(1);
        protected override void Read(SampleContext context) { Reads++; Value = Next; }
    }

    private sealed class FailingMeasure(MeasureSpec spec) : Measure(spec)
    {
        protected override void Read(SampleContext context) => throw new InvalidOperationException("falhou");
    }

    private static (MeasureScheduler Scheduler, List<CountingMeasure> Created) Create()
    {
        var created = new List<CountingMeasure>();
        var registry = new MeasureRegistry();
        registry.Register("conta", s => { var m = new CountingMeasure(s); created.Add(m); return m; });
        registry.Register("falha", s => new FailingMeasure(s));
        return (new MeasureScheduler(registry, a => a(), useTimer: false), created);
    }

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PedidosIguaisCompartilhamUmaInstancia()
    {
        var (s, created) = Create();
        var a = s.Subscribe(new MeasureSpec("conta"));
        var b = s.Subscribe(new MeasureSpec("conta"));
        Assert.Same(a, b);
        Assert.Single(created);
        Assert.Equal(1, s.ActiveMeasures);
    }

    [Fact]
    public void LeSoQuandoVenceEUsaOMenorIntervalo()
    {
        var (s, created) = Create();
        s.Subscribe(new MeasureSpec("conta", intervalMs: 5000));
        s.Subscribe(new MeasureSpec("conta", intervalMs: 2000));
        var m = created[0];

        var (_, next) = s.RunDue(DateTimeOffset.UtcNow.AddDays(1)); // vence tudo
        Assert.Equal(1, m.Reads);
        Assert.Equal(TimeSpan.FromSeconds(2), next);
    }

    [Fact]
    public void SoEntregaMedidasQueMudaram()
    {
        var (s, created) = Create();
        s.Subscribe(new MeasureSpec("conta"));
        var m = created[0];

        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        m.Next = 5;
        Assert.Single(s.RunDue(now).Changed);

        now = now.AddSeconds(1);
        Assert.Empty(s.RunDue(now).Changed); // mesmo valor: nada a redesenhar

        now = now.AddSeconds(1);
        m.Next = 6;
        Assert.Single(s.RunDue(now).Changed);
        Assert.Equal(3, m.Reads);
    }

    [Fact]
    public void NaoLeAntesDoVencimento()
    {
        var (s, created) = Create();
        s.Subscribe(new MeasureSpec("conta"));
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        s.RunDue(now);
        s.RunDue(now.AddMilliseconds(500));
        Assert.Equal(1, created[0].Reads);
    }

    [Fact]
    public void PausadoNaoLeNada()
    {
        var (s, created) = Create();
        s.Subscribe(new MeasureSpec("conta"));
        s.Paused = true;
        var (changed, next) = s.RunDue(DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Empty(changed);
        Assert.Null(next);
        Assert.Equal(0, created[0].Reads);
    }

    [Fact]
    public void SemAssinantesNaoAgendaNada()
    {
        var (s, _) = Create();
        var spec = new MeasureSpec("conta");
        s.Subscribe(spec);
        s.Unsubscribe(spec);
        Assert.Equal(0, s.ActiveMeasures);
        Assert.Null(s.RunDue(DateTimeOffset.UtcNow.AddSeconds(5)).Next);
    }

    [Fact]
    public void MultiplicadorDeIntervalo()
    {
        var (s, _) = Create();
        s.Subscribe(new MeasureSpec("conta"));
        s.IntervalMultiplier = 3;
        var (_, next) = s.RunDue(DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(3), next);
    }

    [Fact]
    public void MedidaQueFalhaNaoDerrubaAsOutras()
    {
        var (s, created) = Create();
        var bad = s.Subscribe(new MeasureSpec("falha"));
        s.Subscribe(new MeasureSpec("conta"));
        created[0].Next = 1;
        var (changed, _) = s.RunDue(DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Single(changed);
        Assert.Equal("falhou", bad.Error);
    }
}

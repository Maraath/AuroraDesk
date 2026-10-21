using AuroraDesk.Core.Measures;

namespace AuroraDesk.Core.Tests;

public class MeasureTests
{
    [Fact]
    public void UsoDeCpuDescontaOTempoOcioso()
    {
        var a = new CpuTimes(Idle: 1000, Kernel: 2000, User: 1000);
        var b = new CpuTimes(Idle: 1750, Kernel: 3000, User: 1000); // 1000 de tempo total, 750 ocioso
        Assert.Equal(25, CpuTimes.Usage(a, b), 3);
    }

    [Fact]
    public void UsoDeCpuSemTempoDecorridoEhZero()
    {
        var a = new CpuTimes(1, 2, 3);
        Assert.Equal(0, CpuTimes.Usage(a, a));
    }

    [Fact]
    public void TaxaDeRedeIgnoraContadorReiniciado()
    {
        Assert.Equal(512, NetworkSampler.Rate(1000, 2024, 2));
        Assert.Equal(0, NetworkSampler.Rate(5000, 100, 1));
        Assert.Equal(0, NetworkSampler.Rate(0, 100, 0));
    }

    [Fact]
    public void PicoDeRedeDecaiMasTemPiso()
    {
        double peak = NetworkMeasure.AutoPeak(10_000_000, 0);
        Assert.Equal(9_800_000, peak);
        Assert.Equal(128 * 1024, NetworkMeasure.AutoPeak(0, 10));
        Assert.Equal(20_000_000, NetworkMeasure.AutoPeak(1_000_000, 20_000_000));
    }

    [Fact]
    public void TipoDeConexaoPrefereCabo()
    {
        Assert.Equal(ConnectionKind.Wifi, NetworkSampler.KindOf(71));
        Assert.Equal(ConnectionKind.Wired, NetworkSampler.KindOf(6));
        Assert.Equal(ConnectionKind.Wired, NetworkSampler.Best(ConnectionKind.Wifi, ConnectionKind.Wired));
        Assert.Equal(ConnectionKind.Wifi, NetworkSampler.Best(ConnectionKind.None, ConnectionKind.Wifi));
        Assert.Equal("Sem conexão", ConnectionMeasure.Describe(ConnectionKind.None, Formatting.Strings.Lang.PtBr));
    }

    [Fact]
    public void VolumeLeSemErroOuInformaIndisponivel()
    {
        var m = new VolumeMeasure(new MeasureSpec("volume"));
        m.Update(new SampleContext(new SystemSources(), DateTimeOffset.Now, 1));
        Assert.Null(m.Error);
        if (m.Available) Assert.InRange(m.Value, 0, 100);
        else Assert.Equal("--", m.Text);
    }

    [Fact]
    public void LayoutDeMibIfRow2TemOTamanhoDoWindows() => Assert.Equal(1352, NetworkSampler.RowSize);

    [Fact]
    public void RelogioAlinhaNaViradaDoMinuto()
    {
        var now = new DateTimeOffset(2026, 1, 1, 10, 15, 42, 300, TimeSpan.FromHours(-3));
        var next = ClockMeasure.AlignToNext(now, TimeSpan.FromMinutes(1));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 10, 16, 0, 15, TimeSpan.FromHours(-3)), next);
    }

    [Fact]
    public void RelogioSemSegundosAtualizaPorMinuto()
    {
        var m = new ClockMeasure(new MeasureSpec("relogio", new Dictionary<string, string> { ["formato"] = "HH:mm" }));
        Assert.Equal(TimeSpan.FromMinutes(1), m.DefaultInterval);
        var s = new ClockMeasure(new MeasureSpec("relogio", new Dictionary<string, string> { ["formato"] = "HH:mm:ss" }));
        Assert.Equal(TimeSpan.FromSeconds(1), s.DefaultInterval);
    }

    [Fact]
    public void ChaveDaMedidaIndependeDaOrdemEDoCaso()
    {
        var a = new MeasureSpec("CPU", new Dictionary<string, string> { ["Nucleo"] = "2", ["b"] = "x" });
        var b = new MeasureSpec("cpu", new Dictionary<string, string> { ["b"] = "x", ["nucleo"] = "2" }, intervalMs: 500);
        Assert.Equal(a.Key, b.Key);
    }

    [Fact]
    public void MedidasReaisLeemSemErro()
    {
        // Lê as fontes reais do Windows uma vez: garante que as chamadas Win32 funcionam aqui.
        var registry = MeasureRegistry.CreateDefault();
        var sources = new SystemSources();
        foreach (var type in new[] { "cpu", "memoria", "disco", "rede", "bateria", "relogio", "uptime", "conexao", "volume" })
        {
            var m = registry.Create(new MeasureSpec(type));
            m.Update(new SampleContext(sources, DateTimeOffset.Now, 1));
            m.Update(new SampleContext(sources, DateTimeOffset.Now, 2));
            Assert.Null(m.Error);
        }
    }

    [Fact]
    public void MemoriaReportaUsoEntreZeroECem()
    {
        var m = new MemoryMeasure(new MeasureSpec("memoria"));
        m.Update(new SampleContext(new SystemSources(), DateTimeOffset.Now, 1));
        Assert.InRange(m.Value, 1, 100);
        Assert.Equal(100, m.Max);
    }
}

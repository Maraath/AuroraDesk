using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Measures;

namespace AuroraDesk.Core.Tests;

public class FormattingTests
{
    private static readonly DateTime Sample = new(2026, 3, 3, 14, 5, 9); // terça-feira

    [Theory]
    [InlineData("HH:mm", "14:05")]
    [InlineData("H:m:s", "14:5:9")]
    [InlineData("hh:mm tt", "02:05 PM")]
    [InlineData("dddd, d 'de' MMMM 'de' yyyy", "terça-feira, 3 de março de 2026")]
    [InlineData("ddd dd/MM/yy", "ter 03/03/26")]
    [InlineData("MMM", "mar")]
    [InlineData(@"HH\h", "14h")]
    public void FormataDatasEmPortugues(string format, string expected) =>
        Assert.Equal(expected, DateFormatter.Format(Sample, format, Strings.Lang.PtBr));

    [Fact]
    public void FormataDatasEmIngles() =>
        Assert.Equal("Tuesday, March 3", DateFormatter.Format(Sample, "dddd, MMMM d", Strings.Lang.En));

    [Theory]
    [InlineData("HH:mm", false)]
    [InlineData("HH:mm:ss", true)]
    [InlineData("'segundos' HH:mm", false)]
    public void DetectaSegundos(string format, bool expected) => Assert.Equal(expected, DateFormatter.UsesSeconds(format));

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5,0 GB")]
    [InlineData(150L * 1024 * 1024, "150 MB")]
    public void FormataBytes(double bytes, string expected) => Assert.Equal(expected, ValueFormatter.Bytes(bytes, Strings.Lang.PtBr));

    [Fact]
    public void FormataNumeroNoIdioma()
    {
        Assert.Equal("12,35", ValueFormatter.Number(12.346, 2, Strings.Lang.PtBr));
        Assert.Equal("12.3", ValueFormatter.Number(12.34, 1, Strings.Lang.En));
    }

    [Fact]
    public void FormataDuracao()
    {
        Assert.Equal("12m", ValueFormatter.Duration(TimeSpan.FromMinutes(12)));
        Assert.Equal("2h 5m", ValueFormatter.Duration(new TimeSpan(2, 5, 0)));
        Assert.Equal("3d 4h 0m", ValueFormatter.Duration(new TimeSpan(3, 4, 0, 30)));
    }

    private sealed class FakeMeasure : Measure
    {
        public FakeMeasure(double value, double max, string text) : base(new MeasureSpec("falsa"))
        {
            Value = value; Max = max; Text = text;
        }
        protected override void Read(SampleContext context) { }
    }

    [Fact]
    public void ModeloDeTextoSubstituiMedidas()
    {
        Strings.Language = Strings.Lang.PtBr;
        var t = TextTemplate.Parse("CPU {cpu:0.0}% | {mem.pct:0}% de {mem.max:bytes} | {x} {{literal}}");
        Assert.Equal(["cpu", "mem", "x"], t.MeasureNames);

        var measures = new Dictionary<string, Measure>
        {
            ["cpu"] = new FakeMeasure(12.34, 100, "12"),
            ["mem"] = new FakeMeasure(4L * 1024 * 1024 * 1024, 16L * 1024 * 1024 * 1024, "4 GB"),
        };
        string result = t.Render(n => measures.GetValueOrDefault(n));
        Assert.Equal("CPU 12,3% | 25% de 16,0 GB | -- {literal}", result);
    }

    [Fact]
    public void ModeloSemMedidasEhConstante()
    {
        var t = TextTemplate.Parse("Olá");
        Assert.True(t.IsConstant);
        Assert.Equal("Olá", t.Render(_ => null));
    }

    [Fact]
    public void TextoPadraoUsaTextoDaMedida()
    {
        var t = TextTemplate.Parse("{hora} {hora:maiusculas}");
        var m = new FakeMeasure(0, 1, "ter");
        Assert.Equal("ter TER", t.Render(_ => m));
    }
}

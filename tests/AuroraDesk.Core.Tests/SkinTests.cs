using System.Text.Json;
using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Skins;

namespace AuroraDesk.Core.Tests;

public class SkinTests
{
    private static readonly MeasureRegistry Registry = MeasureRegistry.CreateDefault();

    [Fact]
    public void InterpretaSkinCompleto()
    {
        var skin = SkinParser.Parse("""
            {
              // comentário permitido
              "nome": "Teste",
              "tamanho": [200, 100],
              "tema": "Mocha",
              "fonte": { "familia": "Inter", "tamanho": 12 },
              "fundo": { "cor": "@superficie", "raio": 6 },
              "medidas": {
                "cpu": { "tipo": "cpu", "intervalo": "2s", "nucleo": 1 },
                "ram": { "tipo": "memoria" },
              },
              "elementos": [
                { "tipo": "texto", "texto": "CPU {cpu:0}%", "x": 10, "y": 5, "alinhamento": "direita", "peso": "Bold" },
                { "tipo": "barra", "medida": "ram", "x": 10, "y": 30, "largura": 100, "altura": 4, "acao": "taskmgr.exe" },
                { "tipo": "arco", "medida": "cpu", "x": 0, "y": 0, "largura": 50, "altura": 50, "abertura": 500 },
                { "tipo": "grafico", "medida": "cpu", "x": 0, "y": 60, "largura": 100, "altura": 30, "estilo": "barras" },
                { "tipo": "forma", "forma": "elipse", "x": 0, "y": 0, "largura": 10, "altura": 10 },
                { "tipo": "relogioAnalogico", "x": 0, "y": 0, "largura": 50, "altura": 50, "segundos": false },
              ]
            }
            """, @"C:\skins\teste", Registry);

        Assert.Equal("Teste", skin.Name);
        Assert.Equal((200d, 100d), (skin.Width, skin.Height));
        Assert.Equal("Mocha", skin.ThemeName);
        Assert.Equal("Inter", skin.FontFamily);
        Assert.Equal(12, skin.FontSize);
        Assert.Empty(skin.Warnings);

        Assert.Equal(7, skin.Elements.Count); // fundo + 6
        var bg = Assert.IsType<ShapeElementDef>(skin.Elements[0]);
        Assert.Equal(6, bg.Radius);

        var text = Assert.IsType<TextElementDef>(skin.Elements[1]);
        Assert.Equal(TextAlign.Right, text.Align);
        Assert.Equal(["cpu"], text.Dependencies);

        Assert.Equal("taskmgr.exe", skin.Elements[2].Action);
        Assert.Equal(360, Assert.IsType<ArcElementDef>(skin.Elements[3]).Sweep);
        Assert.True(Assert.IsType<GraphElementDef>(skin.Elements[4]).Bars);
        Assert.True(Assert.IsType<ShapeElementDef>(skin.Elements[5]).Ellipse);

        Assert.Equal(2000, skin.Measures["cpu"].IntervalMs);
        Assert.Equal("1", skin.Measures["cpu"].Get("nucleo"));
        Assert.Equal("HH:mm", skin.Measures[AnalogClockElementDef.ClockMeasureName].Get("formato"));
    }

    [Fact]
    public void JsonInvalidoEhErroFatal()
    {
        var ex = Assert.Throws<SkinException>(() => SkinParser.Parse("{ \"tamanho\": [1, ", "", Registry));
        Assert.Contains("JSON inválido", ex.Message);
    }

    [Fact]
    public void SemTamanhoEhErroFatal() =>
        Assert.Throws<SkinException>(() => SkinParser.Parse("""{ "elementos": [] }""", "", Registry));

    [Fact]
    public void ProblemasNaoFataisViramAvisos()
    {
        var skin = SkinParser.Parse("""
            {
              "tamanho": [100, 100],
              "medidas": {
                "x": { "tipo": "inexistente" },
                "y": { "semTipo": 1 },
                "z": { "tipo": "cpu", "intervalo": "rapido" }
              },
              "elementos": [
                { "tipo": "foguete" },
                { "tipo": "barra", "x": 0, "y": 0, "largura": 10, "altura": 2 },
                { "tipo": "barra", "medida": "nada", "x": 0, "y": 0, "largura": 10, "altura": 2 },
                { "tipo": "forma", "x": 0, "y": 0 },
                { "tipo": "texto", "texto": "ok" }
              ]
            }
            """, "", Registry);

        Assert.Equal(2, skin.Elements.Count); // barra "nada" + texto
        Assert.Contains(skin.Warnings, w => w.Contains("inexistente"));
        Assert.Contains(skin.Warnings, w => w.Contains("sem \"tipo\""));
        Assert.Contains(skin.Warnings, w => w.Contains("intervalo inválido"));
        Assert.Contains(skin.Warnings, w => w.Contains("foguete"));
        Assert.Contains(skin.Warnings, w => w.Contains("falta \"medida\""));
        Assert.Contains(skin.Warnings, w => w.Contains("\"nada\""));
        Assert.Contains(skin.Warnings, w => w.Contains("largura"));
    }

    [Fact]
    public void NomePadraoEhAPasta()
    {
        var skin = SkinParser.Parse("""{ "tamanho": [10, 10], "elementos": [] }""", @"C:\x\meu-widget", Registry);
        Assert.Equal("meu-widget", skin.Name);
    }

    [Theory]
    [InlineData("500", 500)]
    [InlineData("\"250ms\"", 250)]
    [InlineData("\"2s\"", 2000)]
    [InlineData("\"1.5s\"", 1500)]
    [InlineData("\"1min\"", 60000)]
    [InlineData("\"0s\"", null)]
    [InlineData("-1", null)]
    public void InterpretaIntervalos(string json, int? expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, SkinParser.ParseInterval(doc.RootElement));
    }

    [Theory]
    [InlineData("relogio")]
    [InlineData("sistema")]
    [InlineData("rede")]
    [InlineData("bateria")]
    [InlineData("relogio-analogico")]
    [InlineData("clima")]
    public void ExemplosEmbutidosNaoTemAvisos(string name)
    {
        var skin = SkinParser.Parse(SkinLibrary.ReadSample(name), name, Registry);
        Assert.Empty(skin.Warnings);
        Assert.NotEmpty(skin.Elements);
    }

    [Fact]
    public void InstalaExemplosSemSobrescrever()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aurora-skins-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(SkinLibrary.InstallSamples(dir) >= 4);
            string file = SkinLibrary.SkinFile(dir, "relogio");
            File.WriteAllText(file, "editado");
            Assert.Equal(0, SkinLibrary.InstallSamples(dir));
            Assert.Equal("editado", File.ReadAllText(file));
            Assert.Contains("sistema", SkinLibrary.Discover(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

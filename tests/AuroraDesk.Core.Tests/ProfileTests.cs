using System.IO.Compression;
using AuroraDesk.Core.Backup;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Profiles;

namespace AuroraDesk.Core.Tests;

public sealed class ProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora-perfil-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private AppPaths Paths(string name)
    {
        var p = new AppPaths(Path.Combine(_root, name, "roaming"), Path.Combine(_root, name, "local"));
        p.EnsureCreated();
        return p;
    }

    [Fact]
    public void ExportaEImportaTemaWidgetsEFontes()
    {
        var src = Paths("origem");
        File.WriteAllText(Path.Combine(src.Themes, "meu.json"), """{ "nome": "Meu" }""");
        Directory.CreateDirectory(Path.Combine(src.Widgets, "relogio"));
        File.WriteAllText(Path.Combine(src.Widgets, "relogio", "skin.json"), "{}");
        File.WriteAllBytes(Path.Combine(src.Fonts, "Inter.ttf"), [1, 2, 3]);
        var config = new AppConfig { Theme = "Meu", FontOverride = "Inter", FirstRunDone = true };
        config.Widgets.Add(new WidgetInstance { Skin = "relogio", X = 10, Monitor = @"\\.\DISPLAY2" });

        string zip = Path.Combine(_root, "perfil.zip");
        ProfileArchive.Export(zip, config, src);

        var dst = Paths("destino");
        var current = new AppConfig { Theme = "Grafite", Language = "en" };
        var result = ProfileArchive.Import(zip, dst, current);

        Assert.Equal(3, result.Files);
        Assert.Empty(result.Skipped);
        Assert.True(File.Exists(Path.Combine(dst.Widgets, "relogio", "skin.json")));
        Assert.True(File.Exists(Path.Combine(dst.Fonts, "Inter.ttf")));
        Assert.Equal("Meu", result.Config!.Theme);
        Assert.Equal("Inter", result.Config.FontOverride);
        Assert.Equal("en", result.Config.Language); // preferências locais ficam
        Assert.Null(result.Config.Widgets[0].Monitor); // monitor é do PC de origem
    }

    [Fact]
    public void ImportacaoBloqueiaZipSlipEExecutaveis()
    {
        string zip = Path.Combine(_root, "malicioso.zip");
        Directory.CreateDirectory(_root);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "widgets/../../fora.txt", "widgets/x/virus.exe", "widgets/x/atalho.lnk", "outra/arquivo.txt", "themes/ok.json", "widgets/C:/abs.txt" })
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write("x");
            }
        }

        var dst = Paths("destino");
        var result = ProfileArchive.Import(zip, dst, new AppConfig());
        Assert.Equal(1, result.Files);
        Assert.Equal(5, result.Skipped.Count);
        Assert.False(File.Exists(Path.Combine(dst.Roaming, "fora.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "destino", "fora.txt")));
        Assert.True(File.Exists(Path.Combine(dst.Themes, "ok.json")));
        Assert.Null(result.Config);
    }

    [Theory]
    [InlineData("a/b.json", true)]
    [InlineData("../b.json", false)]
    [InlineData("a/../../b.json", false)]
    [InlineData(@"C:\Windows\b.json", false)]
    [InlineData(@"\\servidor\b.json", false)]
    [InlineData("", false)]
    public void CombinacaoSegura(string relative, bool allowed)
    {
        string root = Path.Combine(_root, "raiz");
        Assert.Equal(allowed, ProfileArchive.SafeCombine(root, relative).Length > 0);
    }

    private sealed class FakeRegistry : IUserRegistry
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Get(string key, string name) => Values.TryGetValue(key + "|" + name, out var v) ? v : null;
        public void Set(string key, string name, string value) => Values[key + "|" + name] = value;
        public void Delete(string key, string name) => Values.Remove(key + "|" + name);
    }

    [Fact]
    public void RestaurarOriginalDesfazTudo()
    {
        var reg = new FakeRegistry();
        reg.Set("Run", "Outro", "x.exe");
        reg.Set("Tema", "Valor", "original");
        var backup = new OriginalValues(Path.Combine(_root, "backup", "windows-original.json"), reg);

        backup.Set("Run", "AuroraDesk", "aurora.exe");   // não existia
        backup.Set("Tema", "Valor", "mudado");          // existia
        backup.Set("Tema", "Valor", "mudado de novo");  // o original continua sendo o primeiro
        backup.Delete("Run", "Outro");                  // existia e foi apagado
        Assert.Equal(3, backup.Recorded.Count);

        Assert.Equal(3, backup.RestoreAll());
        Assert.Null(reg.Get("Run", "AuroraDesk"));
        Assert.Equal("original", reg.Get("Tema", "Valor"));
        Assert.Equal("x.exe", reg.Get("Run", "Outro"));
        Assert.Empty(backup.Recorded);
    }

    [Fact]
    public void InterpretaRespostaDoClima()
    {
        var w = WeatherSource.Parse("""
            { "current": { "temperature_2m": 23.4, "relative_humidity_2m": 61, "wind_speed_10m": 9.5, "weather_code": 2 } }
            """, DateTimeOffset.Now);
        Assert.Equal(23.4, w.Temperature);
        Assert.Equal(61, w.Humidity);
        Assert.Equal(2, w.Code);
        Assert.Equal("Parcialmente nublado", WeatherSource.Describe(w.Code, Strings.Lang.PtBr));
        Assert.Equal("Thunderstorm", WeatherSource.Describe(95, Strings.Lang.En));
    }

    [Fact]
    public void InterpretaBuscaDeCidadeEOrdenaPorPopulacao()
    {
        // Trecho real da resposta da geocodificação da Open-Meteo para "Belém".
        var places = Geocoding.Parse("""
            { "results": [
              { "name": "Belem", "latitude": 31.70487, "longitude": 35.20376, "country": "", "admin1": "Margem Ocidental", "population": 29019 },
              { "name": "Belém", "latitude": -1.45583, "longitude": -48.50444, "country": "Brasil", "admin1": "Pará", "population": 1499641 },
              { "name": "Belém", "latitude": -6.69167, "longitude": -35.53333, "country": "Brasil", "admin1": "Paraíba" }
            ] }
            """);
        Assert.Equal(3, places.Count);
        Assert.Equal("Belém, Pará, Brasil", places[0].Display);
        Assert.Equal(-1.45583, places[0].Latitude);
        Assert.Equal("-1.4558, -48.5044", places[0].Coordinates);
        Assert.Equal("Belem, Margem Ocidental", places[1].Display);
        Assert.Empty(Geocoding.Parse("""{ "generationtime_ms": 0.5 }""")); // nada encontrado
    }

    [Fact]
    public void ClimaSemDadosReleLogo()
    {
        var now = DateTimeOffset.Now;
        var ligado = new WeatherMeasure(new MeasureSpec("clima"), new WeatherSource { Enabled = true });
        Assert.Equal(now.AddSeconds(5), ligado.NextDue(now, TimeSpan.FromMinutes(1)));
        var desligado = new WeatherMeasure(new MeasureSpec("clima"), new WeatherSource { Enabled = false });
        Assert.Equal(now.AddMinutes(1), desligado.NextDue(now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ClimaDesligadoNaoUsaRede()
    {
        var source = new WeatherSource { Enabled = false };
        var m = new WeatherMeasure(new MeasureSpec("clima"), source);
        m.Update(new SampleContext(new SystemSources(), DateTimeOffset.Now, 1));
        Assert.Contains("desligado", m.Text);
        Assert.Null(source.Current);
    }
}

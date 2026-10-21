using AuroraDesk.Core.Theming;

namespace AuroraDesk.Core.Tests;

public sealed class ThemeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-temas-" + Guid.NewGuid().ToString("N"));

    public ThemeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("#FFF", 255, 255, 255, 255)]
    [InlineData("#1B1D22", 255, 0x1B, 0x1D, 0x22)]
    [InlineData("#801B1D22", 0x80, 0x1B, 0x1D, 0x22)]
    [InlineData("transparente", 0, 0, 0, 0)]
    public void InterpretaCores(string text, byte a, byte r, byte g, byte b)
    {
        Assert.True(ArgbColor.TryParse(text, out var c));
        Assert.Equal(new ArgbColor(a, r, g, b), c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("vermelho")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void RejeitaCoresInvalidas(string text) => Assert.False(ArgbColor.TryParse(text, out _));

    [Fact]
    public void TemasEmbutidosExistem()
    {
        var store = new ThemeStore(null);
        Assert.Equal(["Grafite", "Mocha", "Neve"], store.All.Select(t => t.Name).Order().ToArray());
        Assert.True(store.Get("Grafite").Dark);
        Assert.False(store.Get("Neve").Dark);
        foreach (var t in store.All)
            foreach (var key in Theme.RequiredColors)
                Assert.True(t.HasColor(key), $"{t.Name} sem {key}");
    }

    [Fact]
    public void NomeDesconhecidoCaiNoGrafite() => Assert.Equal("Grafite", new ThemeStore(null).Get("nao-existe").Name);

    [Fact]
    public void ResolveReferenciasDeCorComOpacidade()
    {
        var t = new ThemeStore(null).Get("Grafite");
        Assert.Equal(t.Accent, t.Resolve("@destaque", ArgbColor.Black));
        Assert.Equal(t.Text.WithOpacity(0.5), t.Resolve("@texto/50", ArgbColor.Black));
        Assert.Equal(t.Background.WithOpacity(t.Opacity), t.Resolve("@painel", ArgbColor.Black));
        Assert.Equal(ArgbColor.Parse("#FF0000"), t.Resolve("#FF0000", ArgbColor.Black));
        Assert.Equal(ArgbColor.Black, t.Resolve("@inexistente", ArgbColor.Black));
        Assert.Equal(ArgbColor.White, t.Resolve(null, ArgbColor.White));
    }

    [Fact]
    public void TemaDoUsuarioCompletaCoresFaltantesESubstituiEmbutido()
    {
        File.WriteAllText(Path.Combine(_dir, "meu.json"), """
            { "nome": "Grafite", "cores": { "destaque": "#FF00FF" }, "raio": 4 } // sobrescreve
            """);
        File.WriteAllText(Path.Combine(_dir, "quebrado.json"), "{ nada");
        var store = new ThemeStore(_dir);
        var t = store.Get("Grafite");
        Assert.False(t.BuiltIn);
        Assert.Equal(ArgbColor.Parse("#FF00FF"), t.Accent);
        Assert.Equal(4, t.CornerRadius);
        Assert.True(t.HasColor("fundo"));
        Assert.Equal(3, store.All.Count); // o quebrado foi ignorado
    }

    [Fact]
    public void ModoEscuroDeduzidoPeloFundo()
    {
        var t = ThemeStore.Parse("""{ "nome": "x", "cores": { "fundo": "#FAFAFA" } }""", null);
        Assert.False(t.Dark);
    }

    [Fact]
    public void SeguirWindowsEscolheEscuroOuClaroEAplicaDestaque()
    {
        var store = new ThemeStore(null);
        Assert.Equal("Mocha", store.Resolve("Grafite", true, true, "Mocha", "Neve", null).Name);
        Assert.Equal("Neve", store.Resolve("Grafite", true, false, "Mocha", "Neve", null).Name);
        var accent = ArgbColor.Parse("#00FF00");
        var t = store.Resolve("Grafite", false, false, "Mocha", "Neve", accent);
        Assert.Equal("Grafite", t.Name);
        Assert.Equal(accent, t.Accent);
    }

    [Fact]
    public void SerializaEReinterpretaOTema()
    {
        var original = new ThemeStore(null).Get("Mocha");
        var copy = ThemeStore.Parse(ThemeStore.Serialize(original), null);
        Assert.Equal(original.Name, copy.Name);
        Assert.Equal(original.Accent, copy.Accent);
        Assert.Equal(original.CornerRadius, copy.CornerRadius);
        Assert.Equal(original.Colors.Count, copy.Colors.Count);
    }
}

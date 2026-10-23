using AuroraDesk.Core.Theming;

namespace AuroraDesk.Core.Tests;

public sealed class DockSkinTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "aurora-skin-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Skin(string name, string json, params string[] files)
    {
        string dir = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        File.WriteAllText(Path.Combine(dir, DockSkinStore.FileName), json);
        foreach (var f in files) File.WriteAllBytes(Path.Combine(dir, f), [0x89, 0x50, 0x4E, 0x47]);
        return dir;
    }

    [Fact]
    public void LeTodasAsPecasEEspelhaAPontaDireita()
    {
        string dir = Skin("Azeroth", """
            // comentário
            {
              "nome": "Azeroth",
              "painel": { "imagem": "painel.png", "margens": [10, 20, 30, 40] },
              "painelEsquerda": "esq.png",
              "encaixe": { "imagem": "encaixe.png", "fundo": "fundo.png", "recuo": 0.2, "margem": 0.1 },
              "pontas": { "esquerda": "grifo.png", "altura": 1.8, "sobreposicao": 0.4 },
              "quadro": { "moldura": "moldura.png", "barraCpu": "verde.png" },
              "medalhao": "medalhao.png",
              "fonte": "Cinzel",
              "corTexto": "#FFD100",
            }
            """, "painel.png", "esq.png", "encaixe.png", "fundo.png", "grifo.png", "moldura.png", "verde.png", "medalhao.png");

        var warnings = new List<string>();
        var s = DockSkinStore.Load(dir, warnings);

        Assert.Empty(warnings);
        Assert.Equal("Azeroth", s.Name);
        Assert.Equal(new SliceMargins(10, 20, 30, 40), s.Panel!.Margins);
        Assert.EndsWith("esq.png", s.PanelFor('e')!.Path);
        Assert.Same(s.Panel, s.PanelFor('d')); // sem painel próprio: o geral
        Assert.Equal((0.2, 0.1), (s.SlotInset, s.SlotOutset));
        Assert.True(s.Caps!.MirrorRight);
        Assert.Equal((1.8, 0.4), (s.Caps.Height, s.Caps.Overlap));
        Assert.EndsWith("verde.png", s.UnitFrame!.CpuBar!.Path);
        Assert.Null(s.UnitFrame.RamBar);
        Assert.Equal(new ArgbColor(255, 0xFF, 0xD1, 0x00), s.TextColor);
        Assert.Equal(8, s.Images().Count());
    }

    [Fact]
    public void QuadroInteiroComAreas()
    {
        string dir = Skin("Q", """
            { "quadro": { "imagem": "quadro.png", "areaRetrato": [4, 2, 60, 60], "areaNome": [70, 3, 116, 17],
                          "areaCpu": [70, 22, 116, 10], "areaRam": [70, 34, 116, 10], "nome": "Thrall", "barraCpu": "verde.png" } }
            """, "quadro.png", "verde.png");
        var q = DockSkinStore.Load(dir, []).UnitFrame!;
        Assert.True(q.HasFullFrame);
        Assert.Equal(new SkinRect(4, 2, 60, 60), q.PortraitArea);
        Assert.Equal(new SkinRect(70, 34, 116, 10), q.RamArea);
        Assert.Equal("Thrall", q.NameText);

        string sem = Skin("Q2", """{ "quadro": { "imagem": "q.png", "areaRetrato": [1, 2, 0, 4] } }""", "q.png");
        Assert.False(DockSkinStore.Load(sem, []).UnitFrame!.HasFullFrame); // área inválida e barras sem área
    }

    [Fact]
    public void ImagemQueNaoExisteViraAvisoEAPecaFicaDeFora()
    {
        string dir = Skin("X", """{ "painel": "nao-tem.png", "medalhao": "m.png" }""", "m.png");
        var warnings = new List<string>();
        var s = DockSkinStore.Load(dir, warnings);
        Assert.Null(s.Panel);
        Assert.NotNull(s.Medallion);
        Assert.Contains(warnings, w => w.Contains("nao-tem.png"));
        Assert.Equal("X", s.Name); // sem "nome": o da pasta
    }

    [Theory]
    [InlineData("20", 20, 20, 20, 20)]
    [InlineData("[8, 12]", 8, 12, 8, 12)]
    [InlineData("[1, 2, 3, 4]", 1, 2, 3, 4)]
    public void MargensEmTresFormatos(string margens, double l, double t, double r, double b)
    {
        string dir = Skin("M" + Guid.NewGuid().ToString("N")[..6], $$"""{ "painel": { "imagem": "p.png", "margens": {{margens}} } }""", "p.png");
        Assert.Equal(new SliceMargins(l, t, r, b), DockSkinStore.Load(dir, []).Panel!.Margins);
    }

    [Fact]
    public void ListaSoPastasComSkinJsonEOModeloEhValido()
    {
        Skin("B", "{}");
        Skin("A", DockSkinStore.Template("A"));
        Directory.CreateDirectory(Path.Combine(_root, "sem-json"));
        Assert.Equal(["A", "B"], DockSkinStore.List(_root));
        Assert.Empty(DockSkinStore.List(Path.Combine(_root, "nao-existe")));

        var warnings = new List<string>();
        var s = DockSkinStore.Load(Path.Combine(_root, "A"), warnings);
        Assert.Equal("Cinzel", s.Font); // o modelo comentado é JSON válido
        Assert.NotEmpty(warnings);       // e avisa das imagens que ainda não foram colocadas
    }
}

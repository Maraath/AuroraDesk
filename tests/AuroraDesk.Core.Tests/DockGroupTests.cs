using AuroraDesk.Core.Config;

namespace AuroraDesk.Core.Tests;

public class DockGroupTests
{
    private static DockItem F(string path) => new() { Kind = "arquivo", Path = path };

    [Fact]
    public void CriaGrupoNoLugarDoAtalho()
    {
        var a = F("a.exe"); var b = F("b.exe"); var c = F("c.exe");
        var items = new List<DockItem> { a, b, c };
        var g = DockGroups.Create(items, "Jogos", b);
        Assert.Equal([a, g, c], items);
        Assert.Equal([b], g.Items!);
        Assert.True(g.IsGroup);
    }

    [Fact]
    public void GrupoVazioVaiProFim()
    {
        var items = new List<DockItem> { F("a.exe") };
        var g = DockGroups.Create(items, "Novo");
        Assert.Same(g, items[^1]);
        Assert.Empty(g.Items!);
    }

    [Fact]
    public void AdicionaTiraDoDockEIgnoraRepetidoSeparadorEGrupo()
    {
        var a = F("a.exe"); var sep = new DockItem { Kind = "separador" };
        var items = new List<DockItem> { a, sep };
        var g = DockGroups.Create(items, "G");
        var outro = DockGroups.Create(items, "H");

        Assert.True(DockGroups.Add(items, g, a));
        Assert.DoesNotContain(a, items);
        Assert.False(DockGroups.Add(items, g, F("A.EXE"))); // mesmo alvo
        Assert.False(DockGroups.Add(items, g, sep));
        Assert.False(DockGroups.Add(items, g, outro)); // grupos não se aninham
        Assert.Single(g.Items!);
    }

    [Fact]
    public void RemoveEDesagrupa()
    {
        var a = F("a.exe"); var b = F("b.exe"); var z = F("z.exe");
        var items = new List<DockItem> { z };
        var g = DockGroups.Create(items, "G");
        DockGroups.Add(items, g, a);
        DockGroups.Add(items, g, b);

        DockGroups.Remove(items, g, a, toDock: true);
        Assert.Equal([z, g, a], items);
        DockGroups.Ungroup(items, g);
        Assert.Equal([z, b, a], items);
    }

    [Fact]
    public void GruposNoFimFormamUmBlocoFixoNaDireita()
    {
        DockItem Sep() => new() { Kind = "separador" };
        DockItem G() => new() { Kind = "grupo", Items = [] };
        var a = F("a.exe"); var b = F("b.exe");

        Assert.Equal(2, DockGroups.TrailingGroupsStart([a, b, Sep(), Sep(), G(), G(), G()])); // seps entram no bloco
        Assert.Equal(2, DockGroups.TrailingGroupsStart([a, b, G()]));
        Assert.Equal(3, DockGroups.TrailingGroupsStart([a, b, Sep()])); // sem grupo: tudo no fim
        Assert.Equal(4, DockGroups.TrailingGroupsStart([a, G(), b, F("c.exe")])); // grupo no meio não prende nada
        Assert.Equal(0, DockGroups.TrailingGroupsStart([]));
    }

    [Fact]
    public void GrupoSobreviveAoConfigJson()
    {
        var config = new AppConfig();
        var g = DockGroups.Create(config.Dock.Items, "Trabalho");
        DockGroups.Add(config.Dock.Items, g, F(@"C:\x\app.exe"));
        var loaded = ConfigStore.Deserialize(ConfigStore.Serialize(config))!;
        var lg = Assert.Single(loaded.Dock.Items);
        Assert.True(lg.IsGroup);
        Assert.Equal("Trabalho", lg.Name);
        Assert.Equal(@"C:\x\app.exe", Assert.Single(lg.Items!).Path);
    }

    [Fact]
    public void CamposDeVersaoMaisNovaSobrevivemAoSalvar()
    {
        // Uma versão antiga não pode apagar o que uma mais nova gravou (ex.: grupos, ilhas).
        const string json = """
            { "futuro": 1, "dock": { "ilhaNova": ["x"], "items": [ { "kind": "grupo", "name": "G", "corDoGrupo": "#fff" } ] } }
            """;
        var config = ConfigStore.Deserialize(json)!;
        string saved = ConfigStore.Serialize(config);
        Assert.Contains("\"futuro\": 1", saved);
        Assert.Contains("\"ilhaNova\"", saved);
        Assert.Contains("\"corDoGrupo\": \"#fff\"", saved);
    }
}

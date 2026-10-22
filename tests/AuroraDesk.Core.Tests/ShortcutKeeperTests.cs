using AuroraDesk.Core.Config;
using AuroraDesk.Core.Dock;

namespace AuroraDesk.Core.Tests;

public sealed class ShortcutKeeperTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-atalhos-" + Guid.NewGuid().ToString("N"));
    private readonly string _desktop;
    private readonly ShortcutKeeper _keeper;

    public ShortcutKeeperTests()
    {
        _desktop = Directory.CreateDirectory(Path.Combine(_dir, "desktop")).FullName;
        _keeper = new ShortcutKeeper(Path.Combine(_dir, "atalhos"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Arquivo(string name, string content = "x")
    {
        string path = Path.Combine(_desktop, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AtalhoContinuaAbrindoDepoisDeApagarOOriginal()
    {
        string lnk = Arquivo("Steam.lnk");
        var item = new DockItem { Path = lnk };
        var grupo = new DockItem { Kind = "grupo", Name = "Jogos", Items = [item] };

        Assert.True(_keeper.KeepAll([grupo]));
        File.Delete(lnk);

        Assert.StartsWith(_keeper.Folder, item.Path);
        Assert.True(File.Exists(item.Path));
        Assert.Equal("Steam", item.Name);
        Assert.False(_keeper.KeepAll([grupo])); // já guardado: nada muda
    }

    [Fact]
    public void ExecutavelESemArquivoFicamComoEstao()
    {
        string exe = Arquivo("app.exe");
        string sumiu = Path.Combine(_desktop, "sumiu.lnk");
        Assert.Equal(exe, _keeper.Keep(exe));
        Assert.Equal(sumiu, _keeper.Keep(sumiu));
    }

    [Fact]
    public void MesmoNomeComConteudoDiferenteNaoSobrescreve()
    {
        string a = _keeper.Keep(Arquivo("Jogo.url", "a"));
        Directory.CreateDirectory(Path.Combine(_desktop, "outra"));
        string bPath = Path.Combine(_desktop, "outra", "Jogo.url");
        File.WriteAllText(bPath, "b");
        string b = _keeper.Keep(bPath);

        Assert.NotEqual(a, b);
        Assert.Equal("a", File.ReadAllText(a));
        Assert.Equal("b", File.ReadAllText(b));
        Assert.Equal(a, _keeper.Keep(Arquivo("Jogo.url", "a"))); // igual: reaproveita
    }
}

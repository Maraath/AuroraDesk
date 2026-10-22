using AuroraDesk.Core.Config;

namespace AuroraDesk.Core.Tests;

/// <summary>A configuração do usuário nunca pode virar o padrão por causa de uma falha passageira.</summary>
public sealed class ConfigStoreRobustezTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "aurora-cfg-" + Guid.NewGuid().ToString("N"))).FullName;
    private string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AppConfig ComGrupo()
    {
        var config = new AppConfig();
        DockGroups.Create(config.Dock.Items, "Jogos");
        return config;
    }

    [Fact]
    public void ArquivoPresoPorOutroProgramaNaoViraPadraoNemESobrescrito()
    {
        new ConfigStore(ConfigPath).Save(ComGrupo());
        File.Delete(ConfigPath + ".bak");
        string original = File.ReadAllText(ConfigPath);

        var store = new ConfigStore(ConfigPath, attempts: 2, delayMs: 1);
        AppConfig loaded;
        using (new FileStream(ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) // outro programa prende o arquivo
            loaded = store.Load();

        Assert.True(store.ReadOnly);
        Assert.Empty(loaded.Dock.Items);
        store.Save(loaded); // o padrão não pode ir para o disco
        Assert.Equal(original, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void ArquivoPresoUsaACopiaDoUltimoBom()
    {
        var first = new ConfigStore(ConfigPath);
        first.Save(ComGrupo());
        first.Load(); // leitura boa: grava config.json.bak

        var store = new ConfigStore(ConfigPath, attempts: 2, delayMs: 1);
        AppConfig loaded;
        using (new FileStream(ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            loaded = store.Load();

        Assert.False(store.ReadOnly);
        Assert.Equal("Jogos", Assert.Single(loaded.Dock.Items).Name);
    }

    [Fact]
    public void ArquivoCorrompidoUsaACopia()
    {
        var store = new ConfigStore(ConfigPath);
        store.Save(ComGrupo());
        store.Load();
        File.WriteAllText(ConfigPath, "{ quebrado");

        var loaded = store.Load();
        Assert.Equal("Jogos", Assert.Single(loaded.Dock.Items).Name);
        Assert.True(File.Exists(ConfigPath + ".corrompido"));
    }

    [Fact]
    public void AchaCopiaDosDadosRedirecionadaParaUmPacote()
    {
        string packages = Path.Combine(_dir, "Packages");
        string copia = Directory.CreateDirectory(Path.Combine(packages, "Claude_abc", "LocalCache", "Roaming", "AuroraDesk")).FullName;
        Directory.CreateDirectory(Path.Combine(packages, "Outro_xyz", "LocalCache", "Roaming"));
        var paths = new AppPaths(Path.Combine(_dir, "Roaming", "AuroraDesk"), Path.Combine(_dir, "Local", "AuroraDesk"));

        Assert.Empty(paths.FindPackageCopies(packages)); // pasta sem config.json não conta
        File.WriteAllText(Path.Combine(copia, "config.json"), "{}");
        Assert.Equal(copia, Assert.Single(paths.FindPackageCopies(packages)));
        Assert.Empty(paths.FindPackageCopies(Path.Combine(_dir, "nao-existe")));
    }

    [Fact]
    public void FalhaPassageiraEhVencidaComNovasTentativas()
    {
        new ConfigStore(ConfigPath).Save(ComGrupo());
        File.Delete(ConfigPath + ".bak");
        var lockHandle = new FileStream(ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _ = Task.Delay(150, TestContext.Current.CancellationToken).ContinueWith(_ => lockHandle.Dispose(), TestContext.Current.CancellationToken); // o outro programa solta logo

        var store = new ConfigStore(ConfigPath, attempts: 20, delayMs: 50);
        var loaded = store.Load();
        Assert.False(store.ReadOnly);
        Assert.Single(loaded.Dock.Items);
    }
}

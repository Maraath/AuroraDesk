using AuroraDesk.Core.Config;

namespace AuroraDesk.Core.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-testes-" + Guid.NewGuid().ToString("N"));

    public ConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SalvaECarregaConfiguracao()
    {
        var store = new ConfigStore(Path.Combine(_dir, "config.json"));
        var config = new AppConfig { Theme = "Mocha" };
        config.Widgets.Add(new WidgetInstance { Skin = "relogio", X = 10, Y = 20 });
        config.Dock.Items.Add(new DockItem { Path = @"C:\Windows\notepad.exe" });
        store.Save(config);

        var loaded = store.Load();
        Assert.Equal("Mocha", loaded.Theme);
        Assert.Equal("relogio", loaded.Widgets[0].Skin);
        Assert.Equal(20, loaded.Widgets[0].Y);
        Assert.Single(loaded.Dock.Items);
    }

    [Fact]
    public void PosicaoNaoDefinidaSobreviveComoNaN()
    {
        var json = ConfigStore.Serialize(new AppConfig { Widgets = [new WidgetInstance()] });
        Assert.True(double.IsNaN(ConfigStore.Deserialize(json)!.Widgets[0].X));
    }

    [Fact]
    public void ArquivoCorrompidoVoltaAoPadraoEGuardaCopia()
    {
        string path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{ isto não é json");
        var config = new ConfigStore(path).Load();
        Assert.Equal("Grafite", config.Theme);
        Assert.True(File.Exists(path + ".corrompido"));
    }

    [Fact]
    public void ReservaDoDockSegueABarraDoWindows()
    {
        var d = new DockConfig();
        Assert.False(d.EffectiveReserve(taskbarHidden: false)); // barra visível: flutuante por padrão
        Assert.True(d.EffectiveReserve(taskbarHidden: true));   // barra oculta: reserva por padrão

        d.SetReserve(taskbarHidden: true, value: false);        // muda só o modo atual
        Assert.False(d.EffectiveReserve(true));
        Assert.False(d.ReserveSpace);
        d.SetReserve(taskbarHidden: false, value: true);
        Assert.True(d.EffectiveReserve(false));
        Assert.False(d.EffectiveReserve(true));
    }

    [Fact]
    public void ConfigAntigaGanhaReservaComBarraOculta()
    {
        // config.json gravado antes da opção existir: o padrão novo (ligado) vale.
        var config = ConfigStore.Deserialize("""{ "dock": { "reserveSpace": false } }""")!;
        Assert.True(config.Dock.ReserveWhenTaskbarHidden);
    }

    [Fact]
    public void AceitaComentariosEVirgulaFinal()
    {
        string path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{ // comentário\n \"theme\": \"Neve\", }");
        Assert.Equal("Neve", new ConfigStore(path).Load().Theme);
    }
}

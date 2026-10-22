using AuroraDesk.Core.Config;
using AuroraDesk.Core.Dock;

namespace AuroraDesk.Core.Tests;

public class PackagedAppsTests
{
    private const string Antigo = @"C:\Program Files\WindowsApps\Claude_3.1623.1.0_x64__pzs8sxrjxfjjc\app\claude.exe";
    private static readonly string[] Iniciar = ["Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Claude_pzs8sxrjxfjjc!Claude", @"C:\Windows\explorer.exe"];

    [Theory]
    [InlineData(Antigo, "Claude_pzs8sxrjxfjjc")]
    [InlineData(@"c:\program files\windowsapps\Microsoft.WindowsTerminal_1.21.0.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe", "Microsoft.WindowsTerminal_8wekyb3d8bbwe")]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", null)]
    [InlineData(@"C:\Program Files\WindowsApps\quebrado\x.exe", null)]
    public void ExtraiAFamiliaDoPacote(string path, string? familia) => Assert.Equal(familia, PackagedApps.FamilyName(path));

    [Fact]
    public void AtalhoComVersaoViraAppInclusiveDentroDeGrupo()
    {
        var solto = new DockItem { Path = Antigo, Name = "Claude" };
        var noGrupo = new DockItem { Path = Antigo };
        var chrome = new DockItem { Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe" };
        var items = new List<DockItem> { solto, chrome, new() { Kind = "grupo", Name = "G", Items = [noGrupo] } };

        Assert.True(PackagedApps.NeedsMigration(items));
        Assert.True(PackagedApps.Migrate(items, Iniciar));

        Assert.Equal(("app", "Claude_pzs8sxrjxfjjc!Claude", "Claude"), (solto.Kind, solto.Path, solto.Name));
        Assert.Equal("app", noGrupo.Kind);
        Assert.Equal("arquivo", chrome.Kind);
        Assert.False(PackagedApps.NeedsMigration(items));
        Assert.False(PackagedApps.Migrate(items, Iniciar));
    }

    [Fact]
    public void JanelaQueSoInformaOExeEhOMesmoAppFixadoPeloId()
    {
        var fixado = new AppIdentity("Claude_pzs8sxrjxfjjc!Claude", null);
        var janela = new AppIdentity(null, @"C:\Program Files\WindowsApps\Claude_3.6556.1.0_x64__pzs8sxrjxfjjc\app\claude.exe");
        Assert.True(fixado.Matches(janela));
        Assert.True(janela.Matches(fixado));
        Assert.False(new AppIdentity("Outro_pzs8sxrjxfjjc!App", null).Matches(new AppIdentity(null, @"C:\Program Files\WindowsApps\Claudex_1.0.0.0_x64__pzs8sxrjxfjjc\a.exe")));
        Assert.False(fixado.Matches(new AppIdentity(null, @"C:\Program Files\Google\Chrome\Application\chrome.exe")));
    }

    [Fact]
    public void SemAppNoMenuIniciarFicaComoEsta()
    {
        var item = new DockItem { Path = Antigo };
        Assert.False(PackagedApps.Migrate([item], ["Outro_abc!App"]));
        Assert.Equal("arquivo", item.Kind);
    }
}

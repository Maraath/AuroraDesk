using AuroraDesk.Core.Search;

namespace AuroraDesk.Core.Tests;

public class SearchTests
{
    private static readonly SearchItem[] Apps =
    [
        new("Visual Studio Code", "vscode", ResultKind.App),
        new("Visual Studio Installer", "vsi", ResultKind.App),
        new("Calculadora", "calc", ResultKind.App),
        new("Google Chrome", "chrome", ResultKind.App),
        new("Discord", "discord", ResultKind.App),
        new("Configurações", "settings", ResultKind.App),
        new("Bloco de Notas", "notepad", ResultKind.App),
    ];

    [Fact]
    public void IgnoraAcentosEMaiusculas()
    {
        Assert.Equal("configuracoes", SearchRanker.Fold("Configurações"));
        Assert.Equal("Configurações", SearchRanker.Rank("CONFIGURACOES", Apps)[0].Name);
    }

    [Fact]
    public void PrefereComecoDoNomeDepoisPalavraDepoisIniciais()
    {
        Assert.Equal("Calculadora", SearchRanker.Rank("calc", Apps)[0].Name);
        Assert.Equal("Google Chrome", SearchRanker.Rank("chrome", Apps)[0].Name); // começo de palavra
        Assert.Equal("Visual Studio Code", SearchRanker.Rank("vsc", Apps)[0].Name); // iniciais
        Assert.Equal("Bloco de Notas", SearchRanker.Rank("notas", Apps)[0].Name);
    }

    [Fact]
    public void ContemEPalavrasChave()
    {
        Assert.Equal("Discord", SearchRanker.Rank("scor", Apps)[0].Name);
        var wifi = SearchRanker.Rank("internet", SettingsCatalog.All);
        Assert.Contains(wifi, i => i.Target == "ms-settings:network-wifi");
        Assert.Equal("ms-settings:windowsupdate", SearchRanker.Rank("atualiz", SettingsCatalog.All)[0].Target);
    }

    [Fact]
    public void IgnoraPontuacaoEEspacos()
    {
        Assert.Equal("ms-settings:network-wifi", SearchRanker.Rank("wifi", SettingsCatalog.All)[0].Target);
        Assert.Equal("Bloco de Notas", SearchRanker.Rank("blocode", Apps)[0].Name);
    }

    [Fact]
    public void SemCombinacaoOuVazioNaoDevolveNada()
    {
        Assert.Empty(SearchRanker.Rank("", Apps));
        Assert.Empty(SearchRanker.Rank("   ", Apps));
        Assert.Empty(SearchRanker.Rank("zzzz", Apps));
    }

    [Fact]
    public void LimitaQuantidadeEEmpataPorNomeMaisCurto()
    {
        var r = SearchRanker.Rank("visual", Apps, max: 1);
        Assert.Single(r);
        Assert.Equal("Visual Studio Code", r[0].Name);
    }

    [Theory]
    [InlineData("google", "https://www.google.com/search?q=cafe%20com%20leite")]
    [InlineData("bing", "https://www.bing.com/search?q=cafe%20com%20leite")]
    [InlineData("duckduckgo", "https://duckduckgo.com/?q=cafe%20com%20leite")]
    public void EnderecoDaPesquisaNaWeb(string engine, string expected) =>
        Assert.Equal(expected, SearchRanker.WebUrl(" cafe com leite ", engine));
}

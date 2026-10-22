using AuroraDesk.Core.Backup;
using AuroraDesk.Core.Theming;

namespace AuroraDesk.Core.Tests;

public sealed class WindowsColorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-cores-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void CodificaValoresDoRegistro()
    {
        Assert.Equal("dword:1", RegistryValueCodec.Encode(1));
        Assert.Equal("dword:4294967295", RegistryValueCodec.Encode(-1));
        Assert.Equal("binary:0AFF", RegistryValueCodec.Encode(new byte[] { 0x0A, 0xFF }));
        Assert.Equal(123u, RegistryValueCodec.Decode("dword:123"));
        Assert.Equal(new byte[] { 0x0A, 0xFF }, RegistryValueCodec.Decode("binary:0AFF"));
        Assert.Equal("\"C:\\app.exe\"", RegistryValueCodec.Decode("\"C:\\app.exe\"")); // texto antigo continua texto
    }

    [Fact]
    public void PlanoDoTemaEscuroComDestaque()
    {
        var mocha = new ThemeStore(null).Get("Mocha");
        var plan = WindowsColorPlan.Build(mocha, new WindowsColorOptions(DarkMode: true, Accent: true, AccentOnTitleBars: true));
        string Value(string name) => plan.Single(w => w.Name == name).Value;

        Assert.Equal("dword:0", Value("AppsUseLightTheme"));
        Assert.Equal("dword:0", Value("SystemUsesLightTheme"));
        Assert.Equal("dword:1", Value("ColorPrevalence"));
        // Destaque do Mocha #CBA6F7 em ABGR = 0xFFF7A6CB
        Assert.Equal(RegistryValueCodec.Dword(0xFFF7A6CB), Value("AccentColor"));
        Assert.Equal(RegistryValueCodec.Dword(0xC4CBA6F7), Value("ColorizationColor"));
        var palette = (byte[])RegistryValueCodec.Decode(Value("AccentPalette"));
        Assert.Equal(32, palette.Length);
        Assert.Equal(new byte[] { 0xCB, 0xA6, 0xF7, 0 }, palette[12..16]); // tom base no índice 3
    }

    [Fact]
    public void TemaClaroEOpcoesDesligadas()
    {
        var neve = new ThemeStore(null).Get("Neve");
        var plan = WindowsColorPlan.Build(neve, new WindowsColorOptions(DarkMode: true, Accent: false, AccentOnTitleBars: false));
        Assert.Equal(2, plan.Count);
        Assert.All(plan, w => Assert.Equal("dword:1", w.Value));
        Assert.Empty(WindowsColorPlan.Build(neve, new WindowsColorOptions(false, false, false)));
    }

    private sealed class FakeRegistry : IUserRegistry
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Get(string key, string name) => Values.TryGetValue(key + "|" + name, out var v) ? v : null;
        public void Set(string key, string name, string value) => Values[key + "|" + name] = value;
        public void Delete(string key, string name) => Values.Remove(key + "|" + name);
    }

    [Fact]
    public void RestauraSoAsCoresEMantemOResto()
    {
        var reg = new FakeRegistry();
        reg.Set(WindowsColorPlan.DwmKey, "ColorPrevalence", "dword:0");
        var backup = new OriginalValues(Path.Combine(_dir, "b.json"), reg);
        backup.Set(@"Software\Microsoft\Windows\CurrentVersion\Run", "AuroraDesk", "x.exe");
        backup.Set(WindowsColorPlan.DwmKey, "ColorPrevalence", "dword:1");

        int n = backup.RestoreWhere(v => WindowsColorPlan.Keys.Contains(v.Key));
        Assert.Equal(1, n);
        Assert.Equal("dword:0", reg.Get(WindowsColorPlan.DwmKey, "ColorPrevalence"));
        Assert.Equal("x.exe", reg.Get(@"Software\Microsoft\Windows\CurrentVersion\Run", "AuroraDesk")); // intocado
        Assert.Single(backup.Recorded); // o Run continua no backup
    }
}

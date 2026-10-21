using AuroraDesk.Core.Dock;

namespace AuroraDesk.Core.Tests;

public class DockTests
{
    private static readonly bool[] FourIcons = [false, false, false, false];

    [Fact]
    public void LayoutSemZoomEhUniforme()
    {
        var slots = DockLayout.Compute(FourIcons, 40, 10, 8, mouse: null, zoom: 1.5, out double length);
        Assert.Equal(8 + 4 * 40 + 3 * 10 + 8, length);
        Assert.Equal(8, slots[0].Start);
        Assert.Equal(58, slots[1].Start);
        Assert.All(slots, s => Assert.Equal(1, s.Scale));
    }

    [Fact]
    public void SeparadorEhEstreito()
    {
        var slots = DockLayout.Compute([false, true, false], 40, 10, 8, null, 1, out double length);
        Assert.Equal(DockLayout.SeparatorSize, slots[1].Size);
        Assert.Equal(8 + 40 + 10 + DockLayout.SeparatorSize + 10 + 40 + 8, length);
    }

    [Fact]
    public void ZoomAmpliaOIconeSobOMouseEMenosOsVizinhos()
    {
        var baseSlots = DockLayout.Compute(FourIcons, 40, 10, 8, null, 1.5, out double baseLength);
        var slots = DockLayout.Compute(FourIcons, 40, 10, 8, baseSlots[1].Center, 1.5, out double length);
        Assert.Equal(1.5, slots[1].Scale, 3);
        Assert.InRange(slots[0].Scale, 1.0001, 1.5);
        Assert.InRange(slots[2].Scale, 1.0001, 1.5);
        Assert.True(slots[1].Scale > slots[0].Scale);
        Assert.True(length > baseLength);
        // Itens continuam em ordem e sem sobreposição.
        for (int i = 1; i < slots.Length; i++) Assert.True(slots[i].Start >= slots[i - 1].End);
    }

    [Fact]
    public void ZoomDesligadoNaoAmplia()
    {
        var slots = DockLayout.Compute(FourIcons, 40, 10, 8, 60, 1, out _);
        Assert.All(slots, s => Assert.Equal(1, s.Scale));
    }

    [Fact]
    public void AcertaOItemSobOPonto()
    {
        var sep = new[] { false, true, false };
        var slots = DockLayout.Compute(sep, 40, 10, 8, null, 1, out _);
        Assert.Equal(0, DockLayout.HitTest(slots, sep, 20, 10));
        Assert.Equal(-1, DockLayout.HitTest(slots, sep, slots[1].Center, 10)); // separador
        Assert.Equal(2, DockLayout.HitTest(slots, sep, slots[2].Center, 10));
        Assert.Equal(-1, DockLayout.HitTest(slots, sep, 9999, 10));
    }

    [Fact]
    public void IndiceDeInsercao()
    {
        var slots = DockLayout.Compute(FourIcons, 40, 10, 8, null, 1, out _);
        Assert.Equal(0, DockLayout.InsertIndex(slots, 0));
        Assert.Equal(1, DockLayout.InsertIndex(slots, slots[0].Center + 1));
        Assert.Equal(4, DockLayout.InsertIndex(slots, 10_000));
    }

    [Fact]
    public void IdentidadePriorizaAppIdESenaoUsaOExecutavel()
    {
        var term = new AppIdentity("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", @"C:\Program Files\WindowsApps\x\wt.exe");
        Assert.True(term.Matches(new AppIdentity("microsoft.windowsterminal_8wekyb3d8bbwe!app", null)));
        Assert.False(term.Matches(new AppIdentity("Outro!App", @"C:\Program Files\WindowsApps\x\wt.exe")));

        var notepad = new AppIdentity(null, @"C:\Windows\notepad.exe");
        Assert.True(notepad.Matches(new AppIdentity("Algum.Id", @"c:\windows\NOTEPAD.EXE")));
        Assert.False(notepad.Matches(new AppIdentity(null, @"C:\Windows\explorer.exe")));
        Assert.False(new AppIdentity(null, null).Matches(new AppIdentity(null, null)));
    }
}

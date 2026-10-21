using AuroraDesk.Core.Measures;
using AuroraDesk.Core.Skins;

namespace AuroraDesk.Core.Tests;

public class RainmeterTests
{
    private const string Sample = """
        [Rainmeter]
        Update=1000
        BackgroundMode=2
        SolidColor=0,0,0,150

        [Variables]
        Cor=255,255,255,220
        Fonte=Segoe UI

        ; ---------- medidas ----------
        [MeasureCPU]
        Measure=CPU

        [MeasureRAM]
        Measure=PhysicalMemory
        UpdateDivider=2

        [MeasureTime]
        Measure=Time
        Format=%H:%M

        [MeasureLua]
        Measure=Script
        ScriptFile=x.lua

        ; ---------- meters ----------
        [MeterTime]
        Meter=String
        MeasureName=MeasureTime
        X=10
        Y=5
        FontFace=#Fonte#
        FontSize=24
        FontColor=#Cor#
        StringAlign=Left

        [MeterCPU]
        Meter=String
        MeasureName=MeasureCPU
        X=0r
        Y=40R
        H=20
        Text=CPU %1%
        LeftMouseUpAction=[!Refresh]

        [MeterBar]
        Meter=Bar
        MeasureName=MeasureCPU
        X=10
        Y=5R
        W=180
        H=4
        BarColor=7AA2F7
        SolidColor=FFFFFF30

        [MeterRing]
        Meter=Roundline
        MeasureName=MeasureRAM
        X=200
        Y=0
        W=60
        H=60
        LineLength=30
        LineStart=24
        Solid=1
        StartAngle=4.712
        RotationAngle=6.2832

        [MeterLua]
        Meter=String
        MeasureName=MeasureLua

        [MeterShape]
        Meter=Shape
        """;

    [Fact]
    public void ConverteSubconjuntoSuportado()
    {
        var result = RainmeterImporter.Convert(Sample, "teste");
        var skin = SkinParser.Parse(result.Json, "teste", MeasureRegistry.CreateDefault());

        Assert.Empty(skin.Warnings);
        Assert.Equal(["MeasureCPU", "MeasureRAM", "MeasureTime"], skin.Measures.Keys.Order().ToArray());
        Assert.Equal("HH:mm", skin.Measures["MeasureTime"].Get("formato"));
        Assert.Equal(2000, skin.Measures["MeasureRAM"].IntervalMs);

        var texts = skin.Elements.OfType<TextElementDef>().ToList();
        Assert.Equal("{MeasureTime}", texts[0].Template.Source);
        Assert.Equal("Segoe UI", texts[0].FontFamily);
        Assert.Equal(32, texts[0].FontSize);
        Assert.Equal("#DCFFFFFF", texts[0].Color);
        Assert.Equal("CPU {MeasureCPU:0}%", texts[1].Template.Source);

        var bar = skin.Elements.OfType<BarElementDef>().Single();
        Assert.Equal("#FF7AA2F7", bar.Color);
        Assert.Equal("#30FFFFFF", bar.Background);

        var arc = skin.Elements.OfType<ArcElementDef>().Single();
        Assert.Equal(6, arc.Thickness);
        Assert.Equal(270, arc.StartAngle, 0);
        Assert.Equal(360, arc.Sweep, 0);

        Assert.Contains(result.Warnings, w => w.Contains("Script"));
        Assert.Contains(result.Warnings, w => w.Contains("Shape"));
        Assert.Contains(result.Warnings, w => w.Contains("bangs"));
        Assert.Contains(result.Warnings, w => w.Contains("MeasureLua"));
    }

    [Fact]
    public void PosicoesRelativas()
    {
        Assert.Equal(15, RainmeterImporter.Position("15", 100, 20));
        Assert.Equal(105, RainmeterImporter.Position("5r", 100, 20));
        Assert.Equal(125, RainmeterImporter.Position("5R", 100, 20));
        Assert.Equal(0, RainmeterImporter.Position("", 100, 20));
    }

    [Theory]
    [InlineData("255,0,0", "#FFFF0000")]
    [InlineData("255,0,0,128", "#80FF0000")]
    [InlineData("00FF00", "#FF00FF00")]
    [InlineData("00FF0080", "#8000FF00")]
    [InlineData("azul", null)]
    public void ConverteCores(string input, string? expected) => Assert.Equal(expected, RainmeterImporter.Color(input));

    [Theory]
    [InlineData("%H:%M:%S", "HH:mm:ss")]
    [InlineData("%#d de %B", "d' de 'MMMM")]
    [InlineData("%A, %d/%m/%Y", "dddd', 'dd'/'MM'/'yyyy")]
    [InlineData("%I:%M %p", "hh':'mm' 'tt")]
    public void ConverteFormatoDeHora(string strftime, string expected)
    {
        // Os separadores viram literais entre aspas; o resultado formata igual.
        string actual = RainmeterImporter.StrftimeToNet(strftime);
        var t = new DateTime(2026, 3, 3, 14, 5, 9);
        Assert.Equal(
            Formatting.DateFormatter.Format(t, expected, Formatting.Strings.Lang.PtBr),
            Formatting.DateFormatter.Format(t, actual, Formatting.Strings.Lang.PtBr));
    }
}

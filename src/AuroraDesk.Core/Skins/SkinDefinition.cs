using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Measures;

namespace AuroraDesk.Core.Skins;

/// <summary>Skin já interpretado e validado (skin.json). Imutável: hot reload cria outro.</summary>
public sealed class SkinDefinition
{
    public required string Name { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    /// <summary>Pasta do skin (para imagens relativas). Vazio em testes.</summary>
    public string Folder { get; init; } = "";
    /// <summary>Tema próprio do widget (sobrescreve o tema ativo). Null = herda.</summary>
    public string? ThemeName { get; init; }
    public string? FontFamily { get; init; }
    public double? FontSize { get; init; }
    public IReadOnlyDictionary<string, MeasureSpec> Measures { get; init; } = new Dictionary<string, MeasureSpec>();
    public IReadOnlyList<ElementDef> Elements { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Propriedades comuns a todos os elementos visuais (meters).</summary>
public abstract record ElementDef
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double Opacity { get; init; } = 1;
    /// <summary>Programa, pasta ou URL aberto ao clicar.</summary>
    public string? Action { get; init; }

    /// <summary>Medidas das quais o elemento depende (redesenha quando elas mudam).</summary>
    public virtual IEnumerable<string> Dependencies => [];
}

public enum TextAlign { Left, Center, Right }

public sealed record TextElementDef : ElementDef
{
    public required TextTemplate Template { get; init; }
    public string? FontFamily { get; init; }
    public double? FontSize { get; init; }
    public string Weight { get; init; } = "Normal";
    public bool Italic { get; init; }
    public TextAlign Align { get; init; }
    public string? Color { get; init; }
    public override IEnumerable<string> Dependencies => Template.MeasureNames;
}

public sealed record BarElementDef : ElementDef
{
    public required string Measure { get; init; }
    public bool Vertical { get; init; }
    public string? Color { get; init; }
    public string? Background { get; init; }
    public double? Radius { get; init; }
    public override IEnumerable<string> Dependencies => [Measure];
}

public sealed record ArcElementDef : ElementDef
{
    public required string Measure { get; init; }
    public double Thickness { get; init; } = 8;
    public double StartAngle { get; init; } = 135;
    public double Sweep { get; init; } = 270;
    public string? Color { get; init; }
    public string? Background { get; init; }
    public override IEnumerable<string> Dependencies => [Measure];
}

public sealed record GraphElementDef : ElementDef
{
    public required string Measure { get; init; }
    public int Samples { get; init; } = 60;
    public bool Bars { get; init; }
    public string? Color { get; init; }
    public string? Fill { get; init; }
    public double Thickness { get; init; } = 1.5;
    public override IEnumerable<string> Dependencies => [Measure];
}

public enum ImageFit { Uniform, Fill, None }

public sealed record ImageElementDef : ElementDef
{
    public required string File { get; init; }
    public ImageFit Fit { get; init; } = ImageFit.Uniform;
}

public sealed record ShapeElementDef : ElementDef
{
    public bool Ellipse { get; init; }
    public string? Fill { get; init; }
    public string? Stroke { get; init; }
    public double StrokeThickness { get; init; }
    /// <summary>Raio dos cantos. Null = raio do tema.</summary>
    public double? Radius { get; init; }
}

public sealed record AnalogClockElementDef : ElementDef
{
    public bool Seconds { get; init; } = true;
    public string? Face { get; init; }
    public string? HourHand { get; init; }
    public string? MinuteHand { get; init; }
    public string? SecondHand { get; init; }
    public string? Ticks { get; init; }

    /// <summary>Nome interno da medida de relógio criada automaticamente.</summary>
    public const string ClockMeasureName = "__relogioAnalogico";
    public override IEnumerable<string> Dependencies => [ClockMeasureName];
}

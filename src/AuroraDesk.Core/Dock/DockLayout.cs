namespace AuroraDesk.Core.Dock;

/// <summary>Posição de um item ao longo do eixo do dock (horizontal ou vertical).</summary>
public readonly record struct DockSlot(double Start, double Size, double Scale)
{
    public double Center => Start + Size / 2;
    public double End => Start + Size;
}

/// <summary>
/// Cálculo do layout do dock com ampliação ao passar o mouse (estilo "lupa"): os ícones perto
/// do cursor crescem e empurram os vizinhos. Coordenadas ao longo do eixo principal, a partir
/// do início do painel. Tudo puro (sem WPF) para poder testar.
/// </summary>
public static class DockLayout
{
    public const double SeparatorSize = 10;

    /// <param name="separators">Para cada item, se é um separador.</param>
    /// <param name="mouse">Posição do mouse no layout sem zoom (null = mouse fora).</param>
    /// <param name="zoom">Escala máxima (1 = sem ampliação).</param>
    /// <param name="length">Comprimento total do painel, com preenchimento.</param>
    public static DockSlot[] Compute(IReadOnlyList<bool> separators, double iconSize, double spacing, double padding,
        double? mouse, double zoom, out double length) =>
        Compute(separators, iconSize, spacing, padding, mouse, zoom, out length, out _);

    /// <param name="offset">
    /// Quanto o início do painel recua em relação ao painel sem zoom, para o ponto sob o mouse
    /// ficar parado enquanto o painel cresce para os dois lados.
    /// </param>
    public static DockSlot[] Compute(IReadOnlyList<bool> separators, double iconSize, double spacing, double padding,
        double? mouse, double zoom, out double length, out double offset)
    {
        int n = separators.Count;
        var slots = new DockSlot[n];

        // 1) Layout base, sem zoom: define onde o mouse "está".
        var baseCenters = new double[n];
        double pos = padding;
        for (int i = 0; i < n; i++)
        {
            double size = separators[i] ? SeparatorSize : iconSize;
            baseCenters[i] = pos + size / 2;
            pos += size + (i < n - 1 ? spacing : 0);
        }
        double baseLength = pos + padding;

        // 2) Escala de cada ícone pela distância ao mouse (curva cosseno, raio de 2,5 ícones).
        double radius = iconSize * 2.5;
        pos = padding;
        for (int i = 0; i < n; i++)
        {
            double scale = 1;
            if (!separators[i] && mouse is { } m && zoom > 1)
            {
                double d = Math.Abs(m - baseCenters[i]);
                if (d < radius) scale = 1 + (zoom - 1) * (Math.Cos(d / radius * Math.PI) + 1) / 2;
            }
            double size = separators[i] ? SeparatorSize : iconSize * scale;
            slots[i] = new DockSlot(pos, size, scale);
            pos += size + (i < n - 1 ? spacing : 0);
        }
        length = pos + padding;

        // 3) O painel cresce proporcionalmente para os dois lados do mouse.
        offset = 0;
        if (mouse is { } mm && length > baseLength)
        {
            double t = Math.Clamp((mm - padding) / Math.Max(1, baseLength - 2 * padding), 0, 1);
            offset = (length - baseLength) * t;
        }
        return slots;
    }

    /// <summary>Índice do item no ponto (ou -1). Separadores não contam.</summary>
    public static int HitTest(DockSlot[] slots, IReadOnlyList<bool> separators, double point, double spacing)
    {
        for (int i = 0; i < slots.Length; i++)
            if (!separators[i] && point >= slots[i].Start - spacing / 2 && point < slots[i].End + spacing / 2)
                return i;
        return -1;
    }

    /// <summary>Posição de inserção (0..n) para arrastar e soltar, pelo ponto no eixo.</summary>
    public static int InsertIndex(DockSlot[] slots, double point)
    {
        for (int i = 0; i < slots.Length; i++)
            if (point < slots[i].Center) return i;
        return slots.Length;
    }
}

using System.Globalization;

namespace AuroraDesk.Core.Theming;

/// <summary>Cor ARGB de 32 bits, independente de WPF.</summary>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static readonly ArgbColor Transparent = new(0, 0, 0, 0);
    public static readonly ArgbColor White = new(255, 255, 255, 255);
    public static readonly ArgbColor Black = new(255, 0, 0, 0);

    public uint Value => (uint)(A << 24 | R << 16 | G << 8 | B);

    public static ArgbColor FromUInt(uint argb) => new((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public ArgbColor WithOpacity(double opacity) => this with { A = (byte)Math.Round(A * Math.Clamp(opacity, 0, 1)) };

    /// <summary>Aceita #RGB, #RRGGBB, #AARRGGBB e "transparente".</summary>
    public static bool TryParse(string? text, out ArgbColor color)
    {
        color = Transparent;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.Equals("transparente", StringComparison.OrdinalIgnoreCase) || text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text[0] != '#') return false;

        var hex = text.AsSpan(1);
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
        switch (hex.Length)
        {
            case 3:
                byte r = (byte)((v >> 8 & 0xF) * 17), g = (byte)((v >> 4 & 0xF) * 17), b = (byte)((v & 0xF) * 17);
                color = new ArgbColor(255, r, g, b);
                return true;
            case 6:
                color = FromUInt(0xFF000000 | v);
                return true;
            case 8:
                color = FromUInt(v);
                return true;
            default:
                return false;
        }
    }

    public static ArgbColor Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"Cor inválida: '{text}'");

    /// <summary>Luminância relativa (0 = preto, 1 = branco), útil para decidir texto claro/escuro.</summary>
    public double Luminance => (0.2126 * R + 0.7152 * G + 0.0722 * B) / 255.0;

    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

using System.Globalization;

namespace AuroraDesk.Core.Formatting;

/// <summary>Formatação de números, bytes e durações no idioma atual.</summary>
public static class ValueFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>Número com N casas decimais e o separador do idioma.</summary>
    public static string Number(double value, int decimals, Strings.Lang? lang = null)
    {
        string s = Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals, CultureInfo.InvariantCulture);
        return (lang ?? Strings.Language) == Strings.Lang.PtBr ? s.Replace('.', ',') : s;
    }

    /// <summary>Bytes em unidade legível (base 1024): 1,5 GB, 820 KB.</summary>
    public static string Bytes(double bytes, Strings.Lang? lang = null)
    {
        int unit = 0;
        double v = Math.Max(bytes, 0);
        while (v >= 1024 && unit < Units.Length - 1) { v /= 1024; unit++; }
        int decimals = unit == 0 || v >= 100 ? 0 : 1;
        return Number(v, decimals, lang) + " " + Units[unit];
    }

    /// <summary>Duração compacta: "3d 4h 12m", "4h 12m", "12m".</summary>
    public static string Duration(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
        return $"{t.Minutes}m";
    }
}

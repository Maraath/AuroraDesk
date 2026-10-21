namespace AuroraDesk.Core.Formatting;

/// <summary>
/// Textos e nomes localizados. O app roda com InvariantGlobalization (sem ICU, menos RAM),
/// então nomes de meses/dias e separador decimal vêm destas tabelas próprias.
/// </summary>
public static class Strings
{
    public enum Lang { PtBr, En }

    public static Lang Language { get; set; } = Lang.PtBr;

    public static Lang Parse(string? code) =>
        code is not null && code.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? Lang.En : Lang.PtBr;

    public static char DecimalSeparator(Lang lang) => lang == Lang.PtBr ? ',' : '.';

    internal static readonly string[] MonthsPt = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];
    internal static readonly string[] MonthsEn = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    internal static readonly string[] DaysPt = ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"];
    internal static readonly string[] DaysEn = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    internal static readonly string[] ShortDaysPt = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

    public static string MonthName(int month, Lang lang) => (lang == Lang.PtBr ? MonthsPt : MonthsEn)[month - 1];

    public static string ShortMonthName(int month, Lang lang) => MonthName(month, lang)[..3];

    public static string DayName(DayOfWeek day, Lang lang) => (lang == Lang.PtBr ? DaysPt : DaysEn)[(int)day];

    public static string ShortDayName(DayOfWeek day, Lang lang) => lang == Lang.PtBr ? ShortDaysPt[(int)day] : DaysEn[(int)day][..3];

    public static string BatteryState(bool hasBattery, bool pluggedIn, bool charging) => (Language, hasBattery, pluggedIn, charging) switch
    {
        (Lang.PtBr, false, _, _) => "Sem bateria",
        (Lang.PtBr, _, _, true) => "Carregando",
        (Lang.PtBr, _, true, _) => "Na tomada",
        (Lang.PtBr, _, _, _) => "Na bateria",
        (_, false, _, _) => "No battery",
        (_, _, _, true) => "Charging",
        (_, _, true, _) => "Plugged in",
        _ => "On battery",
    };
}

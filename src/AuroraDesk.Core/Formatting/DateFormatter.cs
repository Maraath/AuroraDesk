using System.Text;

namespace AuroraDesk.Core.Formatting;

/// <summary>
/// Subconjunto dos formatos personalizados do .NET (d, dd, ddd, dddd, M..MMMM, yy, yyyy,
/// H, HH, h, hh, m, mm, s, ss, tt), com literais entre aspas e escape com \.
/// Implementado à mão porque o app usa InvariantGlobalization.
/// </summary>
public static class DateFormatter
{
    public static bool UsesSeconds(string format)
    {
        bool quoted = false;
        char quote = '\0';
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (quoted) { if (c == quote) quoted = false; continue; }
            if (c is '\'' or '"') { quoted = true; quote = c; continue; }
            if (c == '\\') { i++; continue; }
            if (c == 's') return true;
        }
        return false;
    }

    public static string Format(DateTime time, string format, Strings.Lang lang)
    {
        var sb = new StringBuilder(format.Length + 16);
        int i = 0;
        while (i < format.Length)
        {
            char c = format[i];
            if (c is '\'' or '"')
            {
                int end = format.IndexOf(c, i + 1);
                if (end < 0) end = format.Length;
                sb.Append(format, i + 1, end - i - 1);
                i = end + 1;
                continue;
            }
            if (c == '\\')
            {
                if (i + 1 < format.Length) sb.Append(format[i + 1]);
                i += 2;
                continue;
            }

            int run = 1;
            while (i + run < format.Length && format[i + run] == c) run++;
            i += run;

            switch (c)
            {
                case 'd':
                    if (run == 1) sb.Append(time.Day);
                    else if (run == 2) sb.Append(time.Day.ToString("00"));
                    else if (run == 3) sb.Append(Strings.ShortDayName(time.DayOfWeek, lang));
                    else sb.Append(Strings.DayName(time.DayOfWeek, lang));
                    break;
                case 'M':
                    if (run == 1) sb.Append(time.Month);
                    else if (run == 2) sb.Append(time.Month.ToString("00"));
                    else if (run == 3) sb.Append(Strings.ShortMonthName(time.Month, lang));
                    else sb.Append(Strings.MonthName(time.Month, lang));
                    break;
                case 'y':
                    sb.Append(run <= 2 ? (time.Year % 100).ToString("00") : time.Year.ToString());
                    break;
                case 'H':
                    sb.Append(run == 1 ? time.Hour.ToString() : time.Hour.ToString("00"));
                    break;
                case 'h':
                    int h12 = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
                    sb.Append(run == 1 ? h12.ToString() : h12.ToString("00"));
                    break;
                case 'm':
                    sb.Append(run == 1 ? time.Minute.ToString() : time.Minute.ToString("00"));
                    break;
                case 's':
                    sb.Append(run == 1 ? time.Second.ToString() : time.Second.ToString("00"));
                    break;
                case 't':
                    string ampm = time.Hour < 12 ? "AM" : "PM";
                    sb.Append(run == 1 ? ampm[..1] : ampm);
                    break;
                default:
                    sb.Append(c, run);
                    break;
            }
        }
        return sb.ToString();
    }
}

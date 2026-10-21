using System.Text;
using AuroraDesk.Core.Measures;

namespace AuroraDesk.Core.Formatting;

/// <summary>
/// Modelo de texto compilado uma vez: "CPU {cpu:0.0}%", "{rede:bytes}/s", "{memoria.max:bytes}".
/// Campos: (nada) = texto padrão da medida, .valor, .max, .pct, .texto.
/// Formatos: 0, 0.0, 0.00 (casas decimais), bytes, maiusculas, minusculas.
/// Use {{ e }} para chaves literais.
/// </summary>
public sealed class TextTemplate
{
    private readonly Segment[] _segments;

    private TextTemplate(Segment[] segments, string source)
    {
        _segments = segments;
        Source = source;
        MeasureNames = segments.Where(s => s.Name is not null).Select(s => s.Name!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string Source { get; }
    public IReadOnlyList<string> MeasureNames { get; }
    public bool IsConstant => MeasureNames.Count == 0;

    private sealed record Segment(string? Literal, string? Name, string Field, string? Format);

    public static TextTemplate Parse(string text)
    {
        var segments = new List<Segment>();
        var literal = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '{' && i + 1 < text.Length && text[i + 1] == '{') { literal.Append('{'); i += 2; continue; }
            if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { literal.Append('}'); i += 2; continue; }
            if (c == '{')
            {
                int end = text.IndexOf('}', i + 1);
                if (end < 0) { literal.Append(text, i, text.Length - i); break; }
                if (literal.Length > 0) { segments.Add(new Segment(literal.ToString(), null, "", null)); literal.Clear(); }
                segments.Add(ParsePlaceholder(text.AsSpan(i + 1, end - i - 1).ToString()));
                i = end + 1;
                continue;
            }
            literal.Append(c);
            i++;
        }
        if (literal.Length > 0) segments.Add(new Segment(literal.ToString(), null, "", null));
        return new TextTemplate(segments.ToArray(), text);
    }

    private static Segment ParsePlaceholder(string body)
    {
        string? format = null;
        int colon = body.IndexOf(':');
        if (colon >= 0) { format = body[(colon + 1)..].Trim(); body = body[..colon]; }
        body = body.Trim();
        string field = "";
        int dot = body.LastIndexOf('.');
        if (dot > 0)
        {
            string candidate = body[(dot + 1)..].ToLowerInvariant();
            if (candidate is "valor" or "max" or "pct" or "texto") { field = candidate; body = body[..dot]; }
        }
        return new Segment(null, body, field, format);
    }

    public string Render(Func<string, Measure?> resolve)
    {
        if (_segments.Length == 1 && _segments[0].Literal is { } only) return only;
        var sb = new StringBuilder();
        foreach (var s in _segments)
        {
            if (s.Literal is not null) { sb.Append(s.Literal); continue; }
            var m = resolve(s.Name!);
            sb.Append(m is null ? "--" : FormatMeasure(m, s.Field, s.Format));
        }
        return sb.ToString();
    }

    internal static string FormatMeasure(Measure m, string field, string? format)
    {
        double number = field switch
        {
            "max" => m.Max,
            "pct" => m.Fraction * 100,
            _ => m.Value,
        };

        string result;
        if (format is null)
            result = field is "" or "texto" ? m.Text : ValueFormatter.Number(number, 0);
        else if (format.Equals("bytes", StringComparison.OrdinalIgnoreCase))
            result = ValueFormatter.Bytes(number);
        else if (format.Equals("maiusculas", StringComparison.OrdinalIgnoreCase))
            result = (field == "" ? m.Text : ValueFormatter.Number(number, 0)).ToUpperInvariant();
        else if (format.Equals("minusculas", StringComparison.OrdinalIgnoreCase))
            result = (field == "" ? m.Text : ValueFormatter.Number(number, 0)).ToLowerInvariant();
        else if (format.StartsWith('0'))
        {
            int dot = format.IndexOf('.');
            int decimals = dot < 0 ? 0 : format.Length - dot - 1;
            result = ValueFormatter.Number(number, decimals);
        }
        else
            result = m.Text;

        return result;
    }
}

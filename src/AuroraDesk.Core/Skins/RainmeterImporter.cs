using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AuroraDesk.Core.Skins;

/// <summary>Resultado da conversão de um skin do Rainmeter.</summary>
public sealed record RainmeterImport(string Json, IReadOnlyList<string> Warnings, IReadOnlyList<string> Images);

/// <summary>
/// Converte um subconjunto de skins .ini do Rainmeter para skin.json.
/// Suportado: [Variables] (#Var#); medidas CPU, PhysicalMemory, FreeDiskSpace, NetIn/NetOut/NetTotal,
/// Time, Uptime e PowerPlugin (bateria); meters String, Bar, Roundline, Line/Histogram e Image;
/// posições absolutas e relativas (r/R). O resto (Lua, plugins, fórmulas Calc, ações com bangs,
/// estilos MeterStyle, máscaras) é ignorado com aviso: o Aurora Desk não executa código de skins.
/// </summary>
public static partial class RainmeterImporter
{
    private sealed class Section(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Get(string key) => Keys.TryGetValue(key, out var v) ? v : null;
    }

    public static RainmeterImport Convert(string ini, string skinName)
    {
        var sections = ParseIni(ini);
        var warnings = new List<string>();
        var images = new List<string>();
        var variables = sections.FirstOrDefault(s => s.Name.Equals("Variables", StringComparison.OrdinalIgnoreCase))?.Keys
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Expand(string? value) => value is null ? "" : VariableRegex().Replace(value, m => variables.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

        var rainmeter = sections.FirstOrDefault(s => s.Name.Equals("Rainmeter", StringComparison.OrdinalIgnoreCase));
        int update = int.TryParse(rainmeter?.Get("Update"), out var u) && u > 0 ? u : 1000;

        // ---------- medidas ----------
        var measures = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var percentMeasures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sections.Where(s => s.Get("Measure") is not null))
        {
            string type = Expand(s.Get("Measure")).Trim();
            int divider = int.TryParse(s.Get("UpdateDivider"), out var d) && d > 0 ? d : 1;
            var m = new Dictionary<string, string> { ["intervalo"] = (update * divider).ToString(CultureInfo.InvariantCulture) };
            switch (type.ToLowerInvariant())
            {
                case "cpu":
                    m["tipo"] = "cpu";
                    if (int.TryParse(s.Get("Processor"), out var core) && core > 0) m["nucleo"] = core.ToString(CultureInfo.InvariantCulture);
                    percentMeasures.Add(s.Name);
                    break;
                case "physicalmemory":
                case "memory":
                    m["tipo"] = "memoria";
                    m["modo"] = s.Get("Total") == "1" ? "total" : "usada";
                    break;
                case "freediskspace":
                    m["tipo"] = "disco";
                    m["unidade"] = Expand(s.Get("Drive") ?? "C:");
                    m["modo"] = s.Get("Total") == "1" ? "total" : s.Get("InvertMeasure") == "1" ? "usada" : "livre";
                    break;
                case "netin": m["tipo"] = "rede"; m["direcao"] = "download"; break;
                case "netout": m["tipo"] = "rede"; m["direcao"] = "upload"; break;
                case "nettotal": m["tipo"] = "rede"; m["direcao"] = "total"; break;
                case "uptime": m["tipo"] = "uptime"; break;
                case "time":
                    m["tipo"] = "relogio";
                    m["formato"] = StrftimeToNet(Expand(s.Get("Format") ?? "%H:%M:%S"));
                    m.Remove("intervalo");
                    break;
                case "plugin" when (s.Get("Plugin") ?? "").Contains("PowerPlugin", StringComparison.OrdinalIgnoreCase):
                    m["tipo"] = "bateria";
                    m["modo"] = (s.Get("PowerState") ?? "PERCENT").Equals("PERCENT", StringComparison.OrdinalIgnoreCase) ? "nivel" : "estado";
                    percentMeasures.Add(s.Name);
                    break;
                default:
                    warnings.Add($"[{s.Name}] Measure={type} não suportada (ignorada)");
                    continue;
            }
            measures[s.Name] = m;
        }

        // ---------- meters ----------
        var elements = new List<Dictionary<string, object>>();
        double prevX = 0, prevY = 0, prevW = 0, prevH = 0, maxX = 0, maxY = 0;
        foreach (var s in sections.Where(s => s.Get("Meter") is not null))
        {
            string type = Expand(s.Get("Meter")).Trim().ToLowerInvariant();
            if (s.Get("MeterStyle") is not null) warnings.Add($"[{s.Name}] MeterStyle não suportado: as propriedades do estilo foram ignoradas");
            if (s.Keys.Keys.Any(k => k.EndsWith("Action", StringComparison.OrdinalIgnoreCase))) warnings.Add($"[{s.Name}] ações (bangs) não são importadas");

            double w = Number(Expand(s.Get("W")), 0), h = Number(Expand(s.Get("H")), 0);
            double x = Position(Expand(s.Get("X")), prevX, prevW), y = Position(Expand(s.Get("Y")), prevY, prevH);
            string? measure = s.Get("MeasureName");
            if (measure is not null && !measures.ContainsKey(measure))
            {
                warnings.Add($"[{s.Name}] usa a medida \"{measure}\", que não foi importada");
                measure = null;
            }

            var e = new Dictionary<string, object> { ["x"] = x, ["y"] = y };
            switch (type)
            {
                case "string":
                {
                    double size = Number(Expand(s.Get("FontSize")), 10) * 4 / 3; // pontos → pixels
                    string text = Expand(s.Get("Text") ?? (measure is null ? "" : "%1"));
                    string value = measure is null ? "" : "{" + measure + (percentMeasures.Contains(measure) ? ":0" : "") + "}";
                    text = text.Replace("%1", value);
                    if (s.Get("Text") is null) text = Expand(s.Get("Prefix")) + text + Expand(s.Get("Postfix"));
                    e["tipo"] = "texto";
                    e["texto"] = text;
                    e["tamanho"] = Math.Round(size, 1);
                    if (s.Get("FontFace") is { } face) e["fonte"] = Expand(face);
                    if (Color(Expand(s.Get("FontColor"))) is { } fc) e["cor"] = fc;
                    string align = (s.Get("StringAlign") ?? "Left").ToLowerInvariant();
                    if (align.StartsWith("center")) e["alinhamento"] = "centro";
                    else if (align.StartsWith("right")) e["alinhamento"] = "direita";
                    if ((s.Get("StringStyle") ?? "").Equals("Bold", StringComparison.OrdinalIgnoreCase)) e["peso"] = "Bold";
                    if (w > 0) e["largura"] = w;
                    if (h <= 0) h = size * 1.4;
                    break;
                }
                case "bar":
                    if (measure is null) { warnings.Add($"[{s.Name}] Bar sem medida"); continue; }
                    e["tipo"] = "barra";
                    e["medida"] = measure;
                    e["vertical"] = (s.Get("BarOrientation") ?? "Horizontal").Equals("Vertical", StringComparison.OrdinalIgnoreCase);
                    if (Color(Expand(s.Get("BarColor"))) is { } bc) e["cor"] = bc;
                    if (Color(Expand(s.Get("SolidColor"))) is { } bg) e["fundo"] = bg;
                    e["raio"] = 0;
                    break;
                case "roundline":
                {
                    if (measure is null) { warnings.Add($"[{s.Name}] Roundline sem medida"); continue; }
                    double length = Number(Expand(s.Get("LineLength")), Math.Max(w, h) / 2);
                    double start = Number(Expand(s.Get("LineStart")), 0);
                    double thickness = s.Get("Solid") == "1" ? Math.Max(1, length - start) : Number(Expand(s.Get("LineWidth")), 2);
                    e["tipo"] = "arco";
                    e["medida"] = measure;
                    // O arco do Rainmeter é centrado no meter: ajusta a caixa para o raio externo.
                    double cx = x + (w > 0 ? w / 2 : length), cy = y + (h > 0 ? h / 2 : length);
                    e["x"] = cx - length;
                    e["y"] = cy - length;
                    w = h = length * 2;
                    e["espessura"] = thickness;
                    e["inicio"] = Math.Round(Number(Expand(s.Get("StartAngle")), 0) * 180 / Math.PI, 1);
                    e["abertura"] = Math.Round(Math.Clamp(Number(Expand(s.Get("RotationAngle")), 2 * Math.PI) * 180 / Math.PI, 1, 360), 1);
                    if (Color(Expand(s.Get("LineColor"))) is { } lc) e["cor"] = lc;
                    break;
                }
                case "line":
                case "histogram":
                    if (measure is null) { warnings.Add($"[{s.Name}] gráfico sem medida"); continue; }
                    e["tipo"] = "grafico";
                    e["medida"] = measure;
                    e["amostras"] = (int)Math.Clamp(w > 0 ? w / 2 : 60, 10, 300);
                    if (type == "histogram") e["estilo"] = "barras";
                    if (Color(Expand(s.Get(type == "line" ? "LineColor" : "PrimaryColor"))) is { } gc) e["cor"] = gc;
                    break;
                case "image":
                {
                    string? file = Expand(s.Get("ImageName"));
                    if (string.IsNullOrWhiteSpace(file) || file.Contains("#@#") || file.Contains('#')) { warnings.Add($"[{s.Name}] imagem com caminho de recurso não suportado"); continue; }
                    if (!Path.HasExtension(file)) file += ".png";
                    e["tipo"] = "imagem";
                    e["arquivo"] = file.Replace('\\', '/');
                    e["ajuste"] = "preencher";
                    images.Add(file);
                    break;
                }
                default:
                    warnings.Add($"[{s.Name}] Meter={s.Get("Meter")} não suportado (ignorado)");
                    continue;
            }

            if (type != "string" && type != "roundline")
            {
                if (w <= 0) w = 100;
                if (h <= 0) h = type == "bar" ? 6 : 40;
            }
            if (type is not "string" || w > 0)
            {
                e["largura"] = w;
                if (type is not "string") e["altura"] = h;
            }

            elements.Add(e);
            prevX = Number(e["x"]); prevY = Number(e["y"]); prevW = w; prevH = h;
            double right = type == "string" && w <= 0 ? prevX + 120 : prevX + w;
            maxX = Math.Max(maxX, right);
            maxY = Math.Max(maxY, prevY + h);
        }

        if (elements.Count == 0) warnings.Add("Nenhum meter suportado encontrado");

        // ---------- JSON ----------
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            j.WriteStartObject();
            j.WriteString("nome", skinName);
            j.WriteString("autor", "Importado do Rainmeter");
            j.WriteStartArray("tamanho");
            j.WriteNumberValue(Math.Ceiling(Math.Max(maxX, 20) + 4));
            j.WriteNumberValue(Math.Ceiling(Math.Max(maxY, 20) + 4));
            j.WriteEndArray();
            if (rainmeter?.Get("BackgroundMode") == "2" && Color(Expand(rainmeter.Get("SolidColor"))) is { } bgColor)
            {
                j.WriteStartObject("fundo");
                j.WriteString("cor", bgColor);
                j.WriteNumber("raio", 0);
                j.WriteEndObject();
            }
            j.WriteStartObject("medidas");
            foreach (var (name, m) in measures)
            {
                j.WriteStartObject(name);
                foreach (var (k, v) in m)
                {
                    if (k == "intervalo") j.WriteNumber(k, int.Parse(v, CultureInfo.InvariantCulture));
                    else j.WriteString(k, v);
                }
                j.WriteEndObject();
            }
            j.WriteEndObject();
            j.WriteStartArray("elementos");
            foreach (var e in elements)
            {
                j.WriteStartObject();
                foreach (var (k, v) in e)
                {
                    switch (v)
                    {
                        case string str: j.WriteString(k, str); break;
                        case bool b: j.WriteBoolean(k, b); break;
                        case int i: j.WriteNumber(k, i); break;
                        default: j.WriteNumber(k, Math.Round(Number(v), 2)); break;
                    }
                }
                j.WriteEndObject();
            }
            j.WriteEndArray();
            j.WriteEndObject();
        }
        return new RainmeterImport(Encoding.UTF8.GetString(ms.ToArray()), warnings, images);
    }

    private static List<Section> ParseIni(string ini)
    {
        var list = new List<Section>();
        Section? current = null;
        foreach (var raw in ini.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            if (line[0] == '[' && line.EndsWith(']'))
            {
                current = new Section(line[1..^1].Trim());
                list.Add(current);
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0 || current is null) continue;
            current.Keys[line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"');
        }
        return list;
    }

    /// <summary>"10" absoluto, "10r" relativo ao início do meter anterior, "10R" relativo ao fim dele.</summary>
    internal static double Position(string value, double prevStart, double prevSize)
    {
        value = value.Trim();
        if (value.Length == 0) return 0;
        char last = value[^1];
        if (last is 'r') return prevStart + Number(value[..^1], 0);
        if (last is 'R') return prevStart + prevSize + Number(value[..^1], 0);
        return Number(value, 0);
    }

    /// <summary>"255,255,255,200" (RGBA decimal) ou "FFFFFF"/"FFFFFFC8" (RRGGBB[AA]) → "#AARRGGBB".</summary>
    internal static string? Color(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        var parts = value.Split(',');
        if (parts.Length is 3 or 4 && parts.All(p => byte.TryParse(p.Trim(), out _)))
        {
            byte a = parts.Length == 4 ? byte.Parse(parts[3].Trim()) : (byte)255;
            return $"#{a:X2}{byte.Parse(parts[0].Trim()):X2}{byte.Parse(parts[1].Trim()):X2}{byte.Parse(parts[2].Trim()):X2}";
        }
        string hex = value.TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, null, out _)) return "#FF" + hex.ToUpperInvariant();
        if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, null, out _)) return "#" + hex[6..].ToUpperInvariant() + hex[..6].ToUpperInvariant();
        return null;
    }

    /// <summary>Formato strftime do Rainmeter (%H:%M, %#d, %A...) → formato .NET (HH:mm, d, dddd...).</summary>
    internal static string StrftimeToNet(string format)
    {
        var sb = new StringBuilder();
        var literal = new StringBuilder();
        void Flush()
        {
            if (literal.Length == 0) return;
            string text = literal.ToString().Replace("'", "");
            // Pontuação (":", "/", " ") fica como está; texto com letras vai entre aspas.
            if (text.Any(char.IsLetter)) sb.Append('\'').Append(text).Append('\'');
            else sb.Append(text);
            literal.Clear();
        }
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length) { literal.Append(format[i]); continue; }
            bool noPad = format[i + 1] == '#';
            if (noPad) i++;
            if (i + 1 >= format.Length) break;
            char c = format[++i];
            string? token = c switch
            {
                'H' => noPad ? "H" : "HH",
                'I' => noPad ? "h" : "hh",
                'M' => noPad ? "m" : "mm",
                'S' => noPad ? "s" : "ss",
                'd' => noPad ? "d" : "dd",
                'm' => noPad ? "M" : "MM",
                'y' => "yy",
                'Y' => "yyyy",
                'A' => "dddd",
                'a' => "ddd",
                'B' => "MMMM",
                'b' => "MMM",
                'p' => "tt",
                '%' => null,
                _ => null,
            };
            if (token is null) { literal.Append(c == '%' ? "%" : ""); continue; }
            Flush();
            sb.Append(token);
        }
        Flush();
        return sb.ToString();
    }

    private static double Number(string? s, double fallback) =>
        double.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static double Number(object v) => System.Convert.ToDouble(v, CultureInfo.InvariantCulture);

    [GeneratedRegex("#([A-Za-z0-9_]+)#")]
    private static partial Regex VariableRegex();
}

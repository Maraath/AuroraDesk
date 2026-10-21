using System.Globalization;
using System.Text.Json;
using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Measures;

namespace AuroraDesk.Core.Skins;

/// <summary>Erro fatal de skin (JSON inválido, sem tamanho...). O widget mantém a última versão válida.</summary>
public sealed class SkinException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Interpreta skin.json (JSON com comentários e vírgula final). Problemas não fatais viram
/// avisos (<see cref="SkinDefinition.Warnings"/>) e o elemento problemático é ignorado.
/// </summary>
public static class SkinParser
{
    public const string FileName = "skin.json";

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 32 };

    public static SkinDefinition ParseFile(string path, MeasureRegistry? registry = null)
    {
        string json;
        try { json = File.ReadAllText(path); }
        catch (IOException ex) { throw new SkinException($"Não foi possível ler {path}: {ex.Message}", ex); }
        return Parse(json, Path.GetDirectoryName(path) ?? "", registry);
    }

    public static SkinDefinition Parse(string json, string folder, MeasureRegistry? registry = null)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, Options); }
        catch (JsonException ex) { throw new SkinException($"JSON inválido na linha {ex.LineNumber + 1}, coluna {ex.BytePositionInLine + 1}", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new SkinException("O skin deve ser um objeto JSON");
            var warnings = new List<string>();

            var (width, height) = ReadSize(root);
            if (width <= 0 || height <= 0) throw new SkinException("Defina \"tamanho\": [largura, altura] com valores positivos");

            var measures = ReadMeasures(root, registry, warnings);
            var elements = new List<ElementDef>();

            if (root.TryGetProperty("fundo", out var bg))
            {
                if (bg.ValueKind == JsonValueKind.True)
                    elements.Add(new ShapeElementDef { Width = width, Height = height, Fill = "@painel", Stroke = "@borda/60", StrokeThickness = 1 });
                else if (bg.ValueKind == JsonValueKind.Object)
                    elements.Add(new ShapeElementDef
                    {
                        Width = width,
                        Height = height,
                        Fill = Str(bg, "cor") ?? "@painel",
                        Stroke = Str(bg, "borda"),
                        StrokeThickness = Num(bg, "espessuraBorda") ?? (Str(bg, "borda") is null ? 0 : 1),
                        Radius = Num(bg, "raio"),
                    });
            }

            if (root.TryGetProperty("elementos", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in list.EnumerateArray())
                {
                    index++;
                    try
                    {
                        if (ReadElement(item, warnings, index) is { } element)
                            elements.Add(element);
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException)
                    {
                        warnings.Add($"Elemento {index}: {ex.Message}");
                    }
                }
            }
            else
            {
                warnings.Add("O skin não tem \"elementos\"");
            }

            // Medidas internas exigidas por elementos (relógio analógico).
            if (elements.OfType<AnalogClockElementDef>().FirstOrDefault() is { } clock)
            {
                measures[AnalogClockElementDef.ClockMeasureName] = new MeasureSpec("relogio",
                    new Dictionary<string, string> { ["formato"] = clock.Seconds ? "HH:mm:ss" : "HH:mm" });
            }

            foreach (var e in elements)
                foreach (var dep in e.Dependencies)
                    if (!measures.ContainsKey(dep))
                        warnings.Add($"Medida \"{dep}\" usada mas não definida em \"medidas\"");

            string? font = null;
            double? fontSize = null;
            if (root.TryGetProperty("fonte", out var f))
            {
                if (f.ValueKind == JsonValueKind.String) font = f.GetString();
                else if (f.ValueKind == JsonValueKind.Object) { font = Str(f, "familia"); fontSize = Num(f, "tamanho"); }
            }

            return new SkinDefinition
            {
                Name = Str(root, "nome") ?? (Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : "Widget"),
                Author = Str(root, "autor"),
                Description = Str(root, "descricao"),
                Width = width,
                Height = height,
                Folder = folder,
                ThemeName = Str(root, "tema"),
                FontFamily = font,
                FontSize = fontSize,
                Measures = measures,
                Elements = elements,
                Warnings = warnings,
            };
        }
    }

    private static (double W, double H) ReadSize(JsonElement root)
    {
        if (root.TryGetProperty("tamanho", out var size) && size.ValueKind == JsonValueKind.Array && size.GetArrayLength() == 2)
            return (size[0].GetDouble(), size[1].GetDouble());
        return (Num(root, "largura") ?? 0, Num(root, "altura") ?? 0);
    }

    private static Dictionary<string, MeasureSpec> ReadMeasures(JsonElement root, MeasureRegistry? registry, List<string> warnings)
    {
        var result = new Dictionary<string, MeasureSpec>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("medidas", out var measures) || measures.ValueKind != JsonValueKind.Object) return result;

        foreach (var m in measures.EnumerateObject())
        {
            if (m.Value.ValueKind != JsonValueKind.Object) { warnings.Add($"Medida \"{m.Name}\" deve ser um objeto"); continue; }
            string? type = Str(m.Value, "tipo");
            if (type is null) { warnings.Add($"Medida \"{m.Name}\" sem \"tipo\""); continue; }
            if (registry is not null && !registry.IsKnown(type)) { warnings.Add($"Medida \"{m.Name}\": tipo desconhecido \"{type}\""); continue; }

            var options = new Dictionary<string, string>();
            int? interval = null;
            foreach (var p in m.Value.EnumerateObject())
            {
                if (p.NameEquals("tipo")) continue;
                if (p.NameEquals("intervalo"))
                {
                    interval = ParseInterval(p.Value);
                    if (interval is null) warnings.Add($"Medida \"{m.Name}\": intervalo inválido");
                    continue;
                }
                options[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString()!,
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => p.Value.GetRawText(),
                };
            }
            result[m.Name] = new MeasureSpec(type, options, interval);
        }
        return result;
    }

    /// <summary>Intervalo em ms (número) ou texto: "500ms", "2s", "1min".</summary>
    internal static int? ParseInterval(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetInt32(out var ms) && ms > 0 ? ms : null;
        if (value.ValueKind != JsonValueKind.String) return null;
        string s = value.GetString()!.Trim().ToLowerInvariant();
        (string suffix, int factor)[] units = [("ms", 1), ("min", 60_000), ("s", 1000)];
        foreach (var (suffix, factor) in units)
            if (s.EndsWith(suffix) && double.TryParse(s[..^suffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0)
                return (int)(v * factor);
        return int.TryParse(s, out var plain) && plain > 0 ? plain : null;
    }

    private static ElementDef? ReadElement(JsonElement e, List<string> warnings, int index)
    {
        if (e.ValueKind != JsonValueKind.Object) { warnings.Add($"Elemento {index} deve ser um objeto"); return null; }
        string type = Str(e, "tipo")?.ToLowerInvariant() ?? "";

        ElementDef? def = type switch
        {
            "texto" => new TextElementDef
            {
                Template = TextTemplate.Parse(Str(e, "texto") ?? ""),
                FontFamily = Str(e, "fonte"),
                FontSize = Num(e, "tamanho"),
                Weight = Str(e, "peso") ?? "Normal",
                Italic = Bool(e, "italico"),
                Align = (Str(e, "alinhamento") ?? "").ToLowerInvariant() switch { "centro" => TextAlign.Center, "direita" => TextAlign.Right, _ => TextAlign.Left },
                Color = Str(e, "cor"),
            },
            "barra" => new BarElementDef
            {
                Measure = Required(e, "medida"),
                Vertical = Bool(e, "vertical"),
                Color = Str(e, "cor"),
                Background = Str(e, "fundo"),
                Radius = Num(e, "raio"),
            },
            "arco" => new ArcElementDef
            {
                Measure = Required(e, "medida"),
                Thickness = Num(e, "espessura") ?? 8,
                StartAngle = Num(e, "inicio") ?? 135,
                Sweep = Math.Clamp(Num(e, "abertura") ?? 270, 1, 360),
                Color = Str(e, "cor"),
                Background = Str(e, "fundo"),
            },
            "grafico" => new GraphElementDef
            {
                Measure = Required(e, "medida"),
                Samples = (int)Math.Clamp(Num(e, "amostras") ?? 60, 2, 600),
                Bars = (Str(e, "estilo") ?? "").Equals("barras", StringComparison.OrdinalIgnoreCase),
                Color = Str(e, "cor"),
                Fill = Str(e, "preenchimento"),
                Thickness = Num(e, "espessura") ?? 1.5,
            },
            "imagem" => new ImageElementDef
            {
                File = Required(e, "arquivo"),
                Fit = (Str(e, "ajuste") ?? "").ToLowerInvariant() switch { "preencher" => ImageFit.Fill, "nenhum" => ImageFit.None, _ => ImageFit.Uniform },
            },
            "forma" => new ShapeElementDef
            {
                Ellipse = (Str(e, "forma") ?? "").Equals("elipse", StringComparison.OrdinalIgnoreCase),
                Fill = Str(e, "preenchimento") ?? Str(e, "cor"),
                Stroke = Str(e, "contorno"),
                StrokeThickness = Num(e, "espessuraContorno") ?? (Str(e, "contorno") is null ? 0 : 1),
                Radius = Num(e, "raio"),
            },
            "relogioanalogico" => new AnalogClockElementDef
            {
                Seconds = !e.TryGetProperty("segundos", out var sec) || sec.ValueKind != JsonValueKind.False,
                Face = Str(e, "mostrador"),
                HourHand = Str(e, "ponteiroHoras"),
                MinuteHand = Str(e, "ponteiroMinutos"),
                SecondHand = Str(e, "ponteiroSegundos"),
                Ticks = Str(e, "marcas"),
            },
            _ => null,
        };

        if (def is null)
        {
            warnings.Add($"Elemento {index}: tipo desconhecido \"{Str(e, "tipo")}\"");
            return null;
        }

        def = def with
        {
            X = Num(e, "x") ?? 0,
            Y = Num(e, "y") ?? 0,
            Width = Num(e, "largura") ?? 0,
            Height = Num(e, "altura") ?? 0,
            Opacity = Math.Clamp(Num(e, "opacidade") ?? 1, 0, 1),
            Action = Str(e, "acao"),
        };

        if (def is not TextElementDef && (def.Width <= 0 || def.Height <= 0))
        {
            warnings.Add($"Elemento {index} ({type}): defina \"largura\" e \"altura\"");
            return null;
        }
        return def;
    }

    private static string Required(JsonElement e, string name) =>
        Str(e, name) ?? throw new FormatException($"falta \"{name}\"");

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

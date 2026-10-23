using System.Globalization;
using System.Text.Json;

namespace AuroraDesk.Core.Theming;

/// <summary>Margens de uma imagem cortada em 9 partes (pixels da imagem): os cantos não esticam.</summary>
public readonly record struct SliceMargins(double Left, double Top, double Right, double Bottom)
{
    public bool IsEmpty => Left <= 0 && Top <= 0 && Right <= 0 && Bottom <= 0;
}

/// <summary>Imagem de uma peça da skin (caminho absoluto) e, para molduras, as margens do corte em 9.</summary>
public sealed record SkinImage(string Path, SliceMargins Margins = default);

/// <summary>Esculturas nas pontas da ilha central (ex.: grifos), alinhadas ao pé do painel.</summary>
public sealed record SkinCaps(SkinImage? Left, SkinImage? Right, double Height, double Overlap, double OffsetY)
{
    /// <summary>Sem imagem própria, a ponta direita é a esquerda espelhada.</summary>
    public bool MirrorRight => Right is null;
}

/// <summary>Retângulo em pixels de uma imagem da skin.</summary>
public readonly record struct SkinRect(double X, double Y, double Width, double Height);

/// <summary>
/// Quadro do jogador na ilha esquerda: retrato redondo e barras de CPU (vida) e RAM (mana).
/// Com <see cref="Full"/>, uma imagem só traz a moldura inteira (como o quadro de um jogo) e as
/// áreas dizem onde ficam o retrato, o nome e as barras dentro dela.
/// </summary>
public sealed record SkinUnitFrame(SkinImage? Frame, SkinImage? Portrait, SkinImage? BarBackground, SkinImage? CpuBar, SkinImage? RamBar)
{
    public SkinImage? Full { get; init; }
    public SkinRect? PortraitArea { get; init; }
    public SkinRect? NameArea { get; init; }
    public SkinRect? CpuArea { get; init; }
    public SkinRect? RamArea { get; init; }
    /// <summary>Texto da área do nome (padrão: "Aurora").</summary>
    public string? NameText { get; init; }

    public bool HasFullFrame => Full is not null && PortraitArea is not null && CpuArea is not null && RamArea is not null;
}

/// <summary>
/// Skin das ilhas e do dock feita de imagens (como a interface de um jogo): uma pasta com PNGs e
/// um skin.json. Toda peça é opcional; a que faltar continua desenhada pelo tema.
/// </summary>
public sealed class DockSkin
{
    public required string Name { get; init; }
    public required string Folder { get; init; }

    public SkinImage? Panel { get; init; }
    public SkinImage? PanelLeft { get; init; }
    public SkinImage? PanelCenter { get; init; }
    public SkinImage? PanelRight { get; init; }

    /// <summary>Moldura desenhada por cima de cada ícone e o fundo atrás dele.</summary>
    public SkinImage? Slot { get; init; }
    public SkinImage? SlotBackground { get; init; }
    /// <summary>Quanto o ícone encolhe dentro do encaixe (fração do tamanho, de cada lado).</summary>
    public double SlotInset { get; init; } = 0.12;
    /// <summary>Quanto a moldura do encaixe passa para fora do ícone (fração, de cada lado).</summary>
    public double SlotOutset { get; init; } = 0.04;

    public SkinCaps? Caps { get; init; }
    public SkinUnitFrame? UnitFrame { get; init; }
    public SkinImage? Medallion { get; init; }
    public SkinImage? RoundButton { get; init; }

    /// <summary>Família da fonte; um .ttf/.otf da pasta da skin também vale.</summary>
    public string? Font { get; init; }
    public ArgbColor? TextColor { get; init; }
    public ArgbColor? SecondaryTextColor { get; init; }

    public SkinImage? PanelFor(char island) => island switch
    {
        'e' => PanelLeft ?? Panel,
        'd' => PanelRight ?? Panel,
        _ => PanelCenter ?? Panel,
    };

    /// <summary>Todas as imagens usadas (para carregar e para vigiar mudanças).</summary>
    public IEnumerable<SkinImage> Images() => new[]
    {
        Panel, PanelLeft, PanelCenter, PanelRight, Slot, SlotBackground, Caps?.Left, Caps?.Right,
        UnitFrame?.Frame, UnitFrame?.Portrait, UnitFrame?.BarBackground, UnitFrame?.CpuBar, UnitFrame?.RamBar, UnitFrame?.Full, Medallion, RoundButton,
    }.OfType<SkinImage>();
}

/// <summary>Lê skins de %APPDATA%\AuroraDesk\skins-dock\&lt;nome&gt;\skin.json (chaves em português).</summary>
public static class DockSkinStore
{
    public const string FileName = "skin.json";

    /// <summary>Pastas de skin válidas (com skin.json), em ordem alfabética.</summary>
    public static IReadOnlyList<string> List(string root)
    {
        if (!Directory.Exists(root)) return [];
        return Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, FileName)))
            .Select(Path.GetFileName).OfType<string>()
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Carrega a skin da pasta. Imagens que não existem viram aviso e a peça fica de fora.</summary>
    public static DockSkin Load(string folder, List<string> warnings)
    {
        string json = File.ReadAllText(Path.Combine(folder, FileName));
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var r = doc.RootElement;

        SkinImage? Img(JsonElement parent, string key)
        {
            if (!parent.TryGetProperty(key, out var v)) return null;
            string? file = v.ValueKind == JsonValueKind.String ? v.GetString() : Str(v, "imagem");
            if (string.IsNullOrWhiteSpace(file)) return null;
            string path = Path.GetFullPath(Path.Combine(folder, file));
            if (!File.Exists(path)) { warnings.Add($"{key}: '{file}' não existe"); return null; }
            return new SkinImage(path, v.ValueKind == JsonValueKind.Object ? Margins(v) : default);
        }

        JsonElement Obj(string key) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
        var slot = Obj("encaixe");
        var caps = Obj("pontas");
        var frame = Obj("quadro");

        SkinCaps? capsSpec = null;
        if (caps.ValueKind == JsonValueKind.Object)
        {
            var left = Img(caps, "esquerda");
            var right = Img(caps, "direita");
            if (left is not null || right is not null)
                capsSpec = new SkinCaps(left ?? right, right, Num(caps, "altura", 1.6), Math.Clamp(Num(caps, "sobreposicao", 0.3), 0, 1), Num(caps, "deslocamentoY", 0));
        }

        SkinUnitFrame? unit = null;
        if (frame.ValueKind == JsonValueKind.Object)
            unit = new SkinUnitFrame(Img(frame, "moldura"), Img(frame, "retrato"), Img(frame, "fundoBarra"), Img(frame, "barraCpu"), Img(frame, "barraRam"))
            {
                Full = Img(frame, "imagem"),
                PortraitArea = Area(frame, "areaRetrato"),
                NameArea = Area(frame, "areaNome"),
                CpuArea = Area(frame, "areaCpu"),
                RamArea = Area(frame, "areaRam"),
                NameText = Str(frame, "nome"),
            };

        return new DockSkin
        {
            Name = Str(r, "nome") ?? Path.GetFileName(folder),
            Folder = folder,
            Panel = Img(r, "painel"),
            PanelLeft = Img(r, "painelEsquerda"),
            PanelCenter = Img(r, "painelCentro"),
            PanelRight = Img(r, "painelDireita"),
            Slot = slot.ValueKind == JsonValueKind.Object ? Img(slot, "imagem") ?? Img(r, "encaixe") : Img(r, "encaixe"),
            SlotBackground = slot.ValueKind == JsonValueKind.Object ? Img(slot, "fundo") : null,
            SlotInset = slot.ValueKind == JsonValueKind.Object ? Math.Clamp(Num(slot, "recuo", 0.12), 0, 0.4) : 0.12,
            SlotOutset = slot.ValueKind == JsonValueKind.Object ? Math.Clamp(Num(slot, "margem", 0.04), 0, 0.5) : 0.04,
            Caps = capsSpec,
            UnitFrame = unit,
            Medallion = Img(r, "medalhao"),
            RoundButton = Img(r, "botaoRedondo"),
            Font = Str(r, "fonte"),
            TextColor = Color(r, "corTexto"),
            SecondaryTextColor = Color(r, "corTextoSecundario"),
        };
    }

    private static string? Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string key, double fallback) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    private static ArgbColor? Color(JsonElement e, string key) =>
        Str(e, key) is { } s && ArgbColor.TryParse(s, out var c) ? c : null;

    /// <summary>Área [x, y, largura, altura] em pixels da imagem.</summary>
    private static SkinRect? Area(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var n = v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetDouble()).ToArray();
        return n.Length == 4 && n[2] > 0 && n[3] > 0 ? new SkinRect(n[0], n[1], n[2], n[3]) : null;
    }

    /// <summary>"margens": 20 (todas), [h, v] ou [esquerda, cima, direita, baixo].</summary>
    private static SliceMargins Margins(JsonElement e)
    {
        if (!e.TryGetProperty("margens", out var m)) return default;
        if (m.ValueKind == JsonValueKind.Number) { double a = m.GetDouble(); return new(a, a, a, a); }
        if (m.ValueKind != JsonValueKind.Array) return default;
        var n = m.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetDouble()).ToArray();
        return n.Length switch
        {
            1 => new(n[0], n[0], n[0], n[0]),
            2 or 3 => new(n[0], n[1], n[0], n[1]),
            >= 4 => new(n[0], n[1], n[2], n[3]),
            _ => default,
        };
    }

    /// <summary>Texto de um skin.json comentado, para o exemplo e a documentação.</summary>
    public static string Template(string name) => string.Create(CultureInfo.InvariantCulture, $$"""
        // Skin do dock e das ilhas feita de imagens PNG. Toda peça é opcional.
        {
          "nome": "{{name}}",
          // Moldura das ilhas e do dock, cortada em 9 partes: os cantos (margens, em pixels da imagem) não esticam.
          "painel": { "imagem": "painel.png", "margens": [24, 24, 24, 24] },
          // Borda de cada ícone (por cima) e fundo (por baixo). recuo: quanto o ícone encolhe; margem: quanto a borda passa do ícone.
          "encaixe": { "imagem": "encaixe.png", "fundo": "fundo-encaixe.png", "recuo": 0.12, "margem": 0.06 },
          // Esculturas nas pontas da ilha central. altura: em relação ao painel; sobreposicao: quanto entra no painel (0 a 1).
          // Sem "direita", a esquerda é espelhada.
          "pontas": { "esquerda": "ponta-esquerda.png", "altura": 1.7, "sobreposicao": 0.3, "deslocamentoY": 0 },
          // Quadro do jogador na ilha esquerda: retrato redondo e barras de CPU e RAM.
          // Com "imagem" (o quadro inteiro, com furos transparentes) e as áreas [x, y, largura, altura]
          // em pixels dela, o quadro fica maior que a ilha, como num jogo:
          //   "imagem": "quadro.png", "areaRetrato": [..], "areaNome": [..], "areaCpu": [..], "areaRam": [..], "nome": "Aurora"
          "quadro": { "moldura": "moldura-retrato.png", "retrato": "retrato.png", "fundoBarra": "barra-fundo.png", "barraCpu": "barra-cpu.png", "barraRam": "barra-ram.png" },
          // Aro do relógio e fundo dos ícones da bandeja.
          "medalhao": "medalhao.png",
          "botaoRedondo": "botao-redondo.png",
          // Fonte (instalada ou um .ttf/.otf desta pasta) e cores do texto.
          "fonte": "Cinzel",
          "corTexto": "#FFD100",
          "corTextoSecundario": "#E8D9B0"
        }
        """);
}

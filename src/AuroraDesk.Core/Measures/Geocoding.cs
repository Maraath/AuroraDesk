using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using AuroraDesk.Core.Formatting;

namespace AuroraDesk.Core.Measures;

/// <summary>Uma cidade encontrada pela busca.</summary>
public sealed record Place(string Name, string? Region, string? Country, double Latitude, double Longitude, long Population)
{
    /// <summary>"Belém, Pará, Brasil".</summary>
    public string Display => string.Join(", ", new[] { Name, Region, Country }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Coordinates => string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.####}, {Longitude:0.####}");
}

/// <summary>
/// Busca de cidades pelo nome na geocodificação da Open-Meteo (pública, sem chave). Só é usada
/// quando o usuário pede, nas configurações do clima: envia apenas o nome digitado.
/// </summary>
public static class Geocoding
{
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });

    public static async Task<IReadOnlyList<Place>> SearchAsync(string name, Strings.Lang lang, CancellationToken cancel = default)
    {
        name = name.Trim();
        if (name.Length < 2) return [];
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&format=json"
                     + "&language=" + (lang == Strings.Lang.PtBr ? "pt" : "en")
                     + "&name=" + Uri.EscapeDataString(name);
        string json = await Http.Value.GetStringAsync(url, cancel).ConfigureAwait(false);
        return Parse(json);
    }

    /// <summary>Interpreta a resposta; a mais populosa primeiro (em geral, a que a pessoa quer).</summary>
    public static IReadOnlyList<Place> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return [];
        var list = new List<Place>();
        foreach (var r in results.EnumerateArray())
        {
            if (!r.TryGetProperty("latitude", out var lat) || !r.TryGetProperty("longitude", out var lon)) continue;
            list.Add(new Place(
                r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                r.TryGetProperty("admin1", out var a) ? a.GetString() : null,
                r.TryGetProperty("country", out var c) ? c.GetString() : null,
                lat.GetDouble(),
                lon.GetDouble(),
                r.TryGetProperty("population", out var p) && p.TryGetInt64(out var pop) ? pop : 0));
        }
        return list.OrderByDescending(p => p.Population).ToList();
    }
}

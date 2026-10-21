using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Logging;

namespace AuroraDesk.Core.Measures;

/// <summary>Condição atual do tempo.</summary>
public sealed record WeatherReport(double Temperature, double Humidity, double Wind, int Code, DateTimeOffset Time);

/// <summary>
/// Clima via Open-Meteo (API pública, sem chave). Desligado por padrão: é o único recurso que
/// usa a rede. Uma consulta a cada 30 min, compartilhada por todos os widgets; o HttpClient só é
/// criado quando o recurso é ligado. A consulta roda em segundo plano e nunca trava o agendador.
/// </summary>
public sealed class WeatherSource
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(30);
    private HttpClient? _http;
    private Task? _pending;
    private DateTimeOffset _lastAttempt;

    public bool Enabled { get; set; }
    public double Latitude { get; set; } = -23.55;
    public double Longitude { get; set; } = -46.63;
    public WeatherReport? Current { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Dispara uma atualização em segundo plano se o cache venceu.</summary>
    public void RefreshIfStale(DateTimeOffset now)
    {
        if (!Enabled || _pending is { IsCompleted: false }) return;
        bool stale = Current is null ? now - _lastAttempt > TimeSpan.FromMinutes(2) : now - Current.Time > CacheTime;
        if (!stale) return;
        _lastAttempt = now;
        _pending = FetchAsync();
    }

    private async Task FetchAsync()
    {
        try
        {
            _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            string url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={Latitude:0.####}&longitude={Longitude:0.####}&current=temperature_2m,relative_humidity_2m,wind_speed_10m,weather_code&timezone=auto");
            string json = await _http.GetStringAsync(url).ConfigureAwait(false);
            Current = Parse(json, DateTimeOffset.Now);
            Error = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (Error is null) Log.Warn($"Clima indisponível: {ex.Message}");
            Error = ex.Message;
        }
    }

    internal static WeatherReport Parse(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var c = doc.RootElement.GetProperty("current");
        return new WeatherReport(
            c.GetProperty("temperature_2m").GetDouble(),
            c.TryGetProperty("relative_humidity_2m", out var h) ? h.GetDouble() : 0,
            c.TryGetProperty("wind_speed_10m", out var w) ? w.GetDouble() : 0,
            c.TryGetProperty("weather_code", out var code) ? code.GetInt32() : -1,
            now);
    }

    /// <summary>Descrição do código WMO usado pela Open-Meteo.</summary>
    public static string Describe(int code, Strings.Lang lang)
    {
        bool pt = lang == Strings.Lang.PtBr;
        return code switch
        {
            0 => pt ? "Céu limpo" : "Clear sky",
            1 => pt ? "Poucas nuvens" : "Mainly clear",
            2 => pt ? "Parcialmente nublado" : "Partly cloudy",
            3 => pt ? "Nublado" : "Overcast",
            45 or 48 => pt ? "Neblina" : "Fog",
            51 or 53 or 55 or 56 or 57 => pt ? "Garoa" : "Drizzle",
            61 or 63 or 66 => pt ? "Chuva" : "Rain",
            65 or 67 => pt ? "Chuva forte" : "Heavy rain",
            71 or 73 or 75 or 77 => pt ? "Neve" : "Snow",
            80 or 81 => pt ? "Pancadas de chuva" : "Rain showers",
            82 => pt ? "Pancadas fortes" : "Violent showers",
            85 or 86 => pt ? "Pancadas de neve" : "Snow showers",
            95 or 96 or 99 => pt ? "Trovoadas" : "Thunderstorm",
            _ => "--",
        };
    }
}

/// <summary>"clima": "campo": "temperatura" (padrão), "descricao", "umidade", "vento" ou "codigo".</summary>
public sealed class WeatherMeasure(MeasureSpec spec, WeatherSource source) : Measure(spec)
{
    private readonly string _field = spec.Get("campo") ?? "temperatura";

    public override bool IsText => _field == "descricao";
    public override TimeSpan DefaultInterval => TimeSpan.FromMinutes(1);

    protected override void Read(SampleContext context)
    {
        source.RefreshIfStale(context.Now);
        if (!source.Enabled) { Text = Strings.Language == Strings.Lang.PtBr ? "Clima desligado" : "Weather off"; Value = 0; return; }
        if (source.Current is not { } w) { Text = "--"; Value = 0; return; }

        (Value, Max, Text) = _field switch
        {
            "descricao" => (w.Code, 99, WeatherSource.Describe(w.Code, Strings.Language)),
            "umidade" => (w.Humidity, 100, ValueFormatter.Number(w.Humidity, 0)),
            "vento" => (w.Wind, 100, ValueFormatter.Number(w.Wind, 0)),
            "codigo" => (w.Code, 99, w.Code.ToString(CultureInfo.InvariantCulture)),
            _ => (w.Temperature, 50, ValueFormatter.Number(w.Temperature, 0)),
        };
    }
}

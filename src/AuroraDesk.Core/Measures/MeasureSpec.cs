using System.Text;

namespace AuroraDesk.Core.Measures;

/// <summary>Pedido de medida: tipo + opções. Pedidos iguais compartilham a mesma instância.</summary>
public sealed class MeasureSpec
{
    public MeasureSpec(string type, IReadOnlyDictionary<string, string>? options = null, int? intervalMs = null)
    {
        Type = type.Trim().ToLowerInvariant();
        Options = options is null
            ? new SortedDictionary<string, string>(StringComparer.Ordinal)
            : new SortedDictionary<string, string>(options.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value), StringComparer.Ordinal);
        IntervalMs = intervalMs;
        Key = BuildKey();
    }

    public string Type { get; }
    public IReadOnlyDictionary<string, string> Options { get; }

    /// <summary>Intervalo pedido pelo skin (ms). Não entra na chave: o agendador usa o menor pedido.</summary>
    public int? IntervalMs { get; }

    public string Key { get; }

    public string? Get(string option) => Options.TryGetValue(option, out var v) ? v : null;

    public int GetInt(string option, int fallback) =>
        int.TryParse(Get(option), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public double GetDouble(string option, double fallback) =>
        double.TryParse(Get(option), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private string BuildKey()
    {
        var sb = new StringBuilder(Type);
        foreach (var (k, v) in Options)
            sb.Append('|').Append(k).Append('=').Append(v);
        return sb.ToString();
    }

    public override string ToString() => Key;
}

using AuroraDesk.Core.Formatting;
using AuroraDesk.Core.Native;

namespace AuroraDesk.Core.Measures;

/// <summary>"cpu": uso total, ou de um núcleo com "nucleo": 1..N.</summary>
public sealed class CpuMeasure(MeasureSpec spec) : Measure(spec)
{
    private readonly int _core = spec.GetInt("nucleo", 0);

    protected override void Read(SampleContext context)
    {
        var cpu = context.Sources.Cpu;
        cpu.Sample(context.Tick);
        double value = _core <= 0 ? cpu.Total : (_core <= cpu.Cores.Length ? cpu.Cores[_core - 1] : 0);
        Value = Math.Round(value, 1);
        Text = Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Modo comum de memória e disco: "uso" (%), "usada", "livre" ou "total" (bytes).</summary>
internal static class CapacityMode
{
    public static void Apply(Measure m, string mode, ulong total, ulong free, Action<double, double> set)
    {
        ulong used = total - Math.Min(free, total);
        switch (mode)
        {
            case "usada":
            case "usado": set(used, total); break;
            case "livre": set(free, total); break;
            case "total": set(total, total); break;
            default: set(total == 0 ? 0 : Math.Round(100.0 * used / total, 1), 100); break;
        }
    }
}

/// <summary>"memoria": RAM física.</summary>
public sealed class MemoryMeasure(MeasureSpec spec) : Measure(spec)
{
    private readonly string _mode = spec.Get("modo") ?? "uso";

    protected override void Read(SampleContext context)
    {
        var status = new NativeMethods.MEMORYSTATUSEX { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status)) throw new InvalidOperationException("GlobalMemoryStatusEx falhou");
        CapacityMode.Apply(this, _mode, status.TotalPhys, status.AvailPhys, Set);
    }

    private void Set(double value, double max)
    {
        Value = value;
        Max = max;
        Text = _mode == "uso" ? value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : ValueFormatter.Bytes(value);
    }

    public override TimeSpan DefaultInterval => TimeSpan.FromSeconds(2);
}

/// <summary>"disco": espaço de uma unidade ("unidade": "C:").</summary>
public sealed class DiskMeasure(MeasureSpec spec) : Measure(spec)
{
    private readonly string _drive = NormalizeDrive(spec.Get("unidade") ?? "C:");
    private readonly string _mode = spec.Get("modo") ?? "uso";

    public override TimeSpan DefaultInterval => TimeSpan.FromSeconds(30);

    internal static string NormalizeDrive(string drive)
    {
        drive = drive.Trim();
        if (drive.Length == 1) drive += ":";
        if (!drive.EndsWith('\\')) drive += "\\";
        return drive;
    }

    protected override void Read(SampleContext context)
    {
        if (!NativeMethods.GetDiskFreeSpaceEx(_drive, out _, out ulong total, out ulong free))
            throw new IOException($"Unidade {_drive} indisponível");
        CapacityMode.Apply(this, _mode, total, free, Set);
    }

    private void Set(double value, double max)
    {
        Value = value;
        Max = max;
        Text = _mode == "uso" ? value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : ValueFormatter.Bytes(value);
    }
}

/// <summary>
/// "rede": bytes/s de "download", "upload" ou "total". Sem "maximo", o máximo se ajusta ao
/// maior valor recente (com decaimento), para barras e gráficos fazerem sentido.
/// </summary>
public sealed class NetworkMeasure(MeasureSpec spec) : Measure(spec)
{
    private readonly string _direction = spec.Get("direcao") ?? "download";
    private readonly double _fixedMax = spec.GetDouble("maximo", 0);
    private double _peak = 128 * 1024;

    protected override void Read(SampleContext context)
    {
        var net = context.Sources.Network;
        net.Sample(context.Tick);
        double rate = _direction switch
        {
            "upload" => net.UploadRate,
            "total" => net.UploadRate + net.DownloadRate,
            _ => net.DownloadRate,
        };
        Value = Math.Round(rate);
        Max = _fixedMax > 0 ? _fixedMax : (_peak = AutoPeak(_peak, rate));
        Text = ValueFormatter.Bytes(rate) + "/s";
    }

    /// <summary>Pico com decaimento de 2% por leitura, nunca abaixo de 128 KB/s.</summary>
    internal static double AutoPeak(double peak, double rate) => Math.Max(Math.Max(rate, peak * 0.98), 128 * 1024);
}

/// <summary>"bateria": "modo": "nivel" (%) ou "estado" (texto).</summary>
public sealed class BatteryMeasure(MeasureSpec spec) : Measure(spec)
{
    private readonly string _mode = spec.Get("modo") ?? "nivel";

    public override bool IsText => _mode == "estado";
    public override TimeSpan DefaultInterval => TimeSpan.FromSeconds(30);

    public bool HasBattery { get; private set; }
    public bool Charging { get; private set; }
    public bool PluggedIn { get; private set; }

    protected override void Read(SampleContext context)
    {
        NativeMethods.GetSystemPowerStatus(out var s);
        HasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255;
        Charging = (s.BatteryFlag & 8) != 0;
        PluggedIn = s.ACLineStatus == 1;
        double level = HasBattery && s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : 100;
        Value = _mode == "estado" ? (Charging ? 1 : 0) : level;
        Max = _mode == "estado" ? 1 : 100;
        Text = _mode == "estado"
            ? Strings.BatteryState(HasBattery, s.ACLineStatus == 1, Charging)
            : level.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>"relogio": data/hora com "formato" (padrão "HH:mm"). Alinha leituras à virada do segundo ou minuto.</summary>
public sealed class ClockMeasure(MeasureSpec spec) : Measure(spec)
{
    private readonly string _format = spec.Get("formato") ?? "HH:mm";
    private readonly Func<DateTime> _now = () => DateTime.Now;

    internal ClockMeasure(MeasureSpec spec, Func<DateTime> now) : this(spec) => _now = now;

    public override bool IsText => true;
    public bool HasSeconds => DateFormatter.UsesSeconds(_format);
    public override TimeSpan DefaultInterval => HasSeconds ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);

    /// <summary>Hora local da última leitura (o relógio analógico usa).</summary>
    public DateTime Time { get; private set; }

    protected override void Read(SampleContext context)
    {
        Time = _now();
        Text = DateFormatter.Format(Time, _format, Strings.Language);
        Value = Time.TimeOfDay.TotalSeconds;
        Max = 86400;
    }

    public override DateTimeOffset NextDue(DateTimeOffset now, TimeSpan interval)
    {
        if (interval != DefaultInterval) return now + interval;
        return AlignToNext(now, HasSeconds ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1));
    }

    /// <summary>Próxima fronteira exata do período (com 15 ms de folga para não cair antes).</summary>
    internal static DateTimeOffset AlignToNext(DateTimeOffset now, TimeSpan period)
    {
        long ticks = now.Ticks - now.Ticks % period.Ticks + period.Ticks;
        return new DateTimeOffset(ticks, now.Offset).AddMilliseconds(15);
    }
}

/// <summary>"conexao": tipo da conexão ativa. Valor 0 = sem rede, 1 = Wi-Fi, 2 = cabo; texto descritivo.</summary>
public sealed class ConnectionMeasure(MeasureSpec spec) : Measure(spec)
{
    public override bool IsText => true;
    public override TimeSpan DefaultInterval => TimeSpan.FromSeconds(5);
    public ConnectionKind Kind { get; private set; }

    protected override void Read(SampleContext context)
    {
        var net = context.Sources.Network;
        net.Sample(context.Tick);
        Kind = net.Connection;
        Value = (int)Kind;
        Max = 2;
        Text = Describe(Kind, Strings.Language);
    }

    public static string Describe(ConnectionKind kind, Strings.Lang lang) => (kind, lang == Strings.Lang.PtBr) switch
    {
        (ConnectionKind.Wifi, true) => "Wi-Fi",
        (ConnectionKind.Wired, true) => "Cabo",
        (_, true) => "Sem conexão",
        (ConnectionKind.Wifi, false) => "Wi-Fi",
        (ConnectionKind.Wired, false) => "Wired",
        _ => "Offline",
    };
}

/// <summary>"volume": volume do alto-falante padrão (%). Texto "mudo" quando silenciado.</summary>
public sealed class VolumeMeasure(MeasureSpec spec) : Measure(spec)
{
    public override TimeSpan DefaultInterval => TimeSpan.FromSeconds(2);
    public bool Muted { get; private set; }
    public bool Available { get; private set; }

    protected override void Read(SampleContext context)
    {
        Available = Audio.TryRead(out float level, out bool muted);
        Muted = Available && muted;
        Value = Available ? Math.Round(level * 100) : 0;
        Max = 100;
        Text = !Available ? "--" : Muted ? (Strings.Language == Strings.Lang.PtBr ? "mudo" : "muted") : Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>"uptime": tempo desde que o Windows ligou.</summary>
public sealed class UptimeMeasure(MeasureSpec spec) : Measure(spec)
{
    public override bool IsText => true;
    public override TimeSpan DefaultInterval => TimeSpan.FromMinutes(1);

    protected override void Read(SampleContext context)
    {
        var up = TimeSpan.FromMilliseconds(NativeMethods.GetTickCount64());
        Value = Math.Floor(up.TotalMinutes);
        Max = Math.Max(Value, 1);
        Text = ValueFormatter.Duration(up);
    }
}

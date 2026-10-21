using AuroraDesk.Core.Native;

namespace AuroraDesk.Core.Measures;

/// <summary>Amostradores de sistema compartilhados entre as medidas.</summary>
public sealed class SystemSources
{
    public CpuSampler Cpu { get; } = new();
    public NetworkSampler Network { get; } = new();
}

/// <summary>Contadores acumulados de um processador lógico (ou do total).</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User)
{
    /// <summary>Uso entre duas amostras, de 0 a 100. Kernel inclui o tempo ocioso.</summary>
    public static double Usage(CpuTimes previous, CpuTimes current)
    {
        ulong idle = current.Idle - previous.Idle;
        ulong total = (current.Kernel - previous.Kernel) + (current.User - previous.User);
        if (total == 0 || current.Idle < previous.Idle) return 0;
        return Math.Clamp(100.0 * (total - Math.Min(idle, total)) / total, 0, 100);
    }
}

public sealed unsafe class CpuSampler
{
    private long _lastTick = -1;
    private CpuTimes _prevTotal;
    private CpuTimes[] _prevCores = [];
    private bool _hasPrevious;

    public double Total { get; private set; }
    public double[] Cores { get; private set; } = [];

    public void Sample(long tick)
    {
        if (tick == _lastTick) return;
        _lastTick = tick;

        NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user);
        var total = new CpuTimes(idle.Value, kernel.Value, user.Value);
        var cores = ReadCores();

        if (_hasPrevious)
        {
            Total = CpuTimes.Usage(_prevTotal, total);
            if (Cores.Length != cores.Length) Cores = new double[cores.Length];
            for (int i = 0; i < cores.Length && i < _prevCores.Length; i++)
                Cores[i] = CpuTimes.Usage(_prevCores[i], cores[i]);
        }

        _prevTotal = total;
        _prevCores = cores;
        _hasPrevious = true;
    }

    private static CpuTimes[] ReadCores()
    {
        // A classe 8 devolve os processadores do grupo atual (até 64).
        const int count = 64;
        int size = sizeof(NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION);
        var buffer = stackalloc NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[count];
        int status = NativeMethods.NtQuerySystemInformation(8, buffer, size * count, out int returned);
        if (status != 0) return [];

        int n = returned / size;
        var result = new CpuTimes[n];
        for (int i = 0; i < n; i++)
            result[i] = new CpuTimes((ulong)buffer[i].IdleTime, (ulong)buffer[i].KernelTime, (ulong)buffer[i].UserTime);
        return result;
    }
}

public sealed unsafe class NetworkSampler
{
    private long _lastTick = -1;
    private ulong _prevIn, _prevOut;
    private long _prevTimestamp;
    private bool _hasPrevious;

    /// <summary>Bytes por segundo recebidos/enviados desde a amostra anterior.</summary>
    public double DownloadRate { get; private set; }
    public double UploadRate { get; private set; }

    /// <summary>Melhor conexão ativa: Wi-Fi, cabo ou nenhuma.</summary>
    public ConnectionKind Connection { get; private set; }

    public void Sample(long tick)
    {
        if (tick == _lastTick) return;
        _lastTick = tick;

        var (inOctets, outOctets, connection) = ReadTotals();
        Connection = connection;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();

        if (_hasPrevious)
        {
            double seconds = (now - _prevTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency;
            DownloadRate = Rate(_prevIn, inOctets, seconds);
            UploadRate = Rate(_prevOut, outOctets, seconds);
        }

        _prevIn = inOctets;
        _prevOut = outOctets;
        _prevTimestamp = now;
        _hasPrevious = true;
    }

    /// <summary>Taxa em bytes/s. Contador que voltou (interface reiniciada) conta como zero.</summary>
    public static double Rate(ulong previous, ulong current, double seconds) =>
        seconds <= 0 || current < previous ? 0 : (current - previous) / seconds;

    private static (ulong In, ulong Out, ConnectionKind Connection) ReadTotals()
    {
        if (NativeMethods.GetIfTable2(out nint table) != 0) return (0, 0, ConnectionKind.None);
        try
        {
            uint count = *(uint*)table;
            var rows = (NativeMethods.MIB_IF_ROW2*)(table + 8);
            ulong totalIn = 0, totalOut = 0;
            var connection = ConnectionKind.None;
            for (uint i = 0; i < count; i++)
            {
                ref var row = ref rows[i];
                bool hardware = (row.InterfaceAndOperStatusFlags & 1) != 0;
                const uint IfOperStatusUp = 1, IfTypeLoopback = 24;
                if (!hardware || row.OperStatus != IfOperStatusUp || row.Type == IfTypeLoopback) continue;
                totalIn += row.InOctets;
                totalOut += row.OutOctets;
                connection = Best(connection, KindOf(row.Type));
            }
            return (totalIn, totalOut, connection);
        }
        finally
        {
            NativeMethods.FreeMibTable(table);
        }
    }

    internal static int RowSize => sizeof(NativeMethods.MIB_IF_ROW2);

    /// <summary>IFTYPE: 71 = Wi-Fi (IEEE 802.11), 6 = Ethernet; o resto conta como cabo (USB, celular...).</summary>
    internal static ConnectionKind KindOf(uint ifType) => ifType == 71 ? ConnectionKind.Wifi : ConnectionKind.Wired;

    /// <summary>Com cabo e Wi-Fi ao mesmo tempo, o Windows prefere o cabo; o ícone segue.</summary>
    internal static ConnectionKind Best(ConnectionKind a, ConnectionKind b) => (ConnectionKind)Math.Max((int)a, (int)b);
}

/// <summary>Tipo de conexão de rede ativa (a ordem define a preferência).</summary>
public enum ConnectionKind { None = 0, Wifi = 1, Wired = 2 }

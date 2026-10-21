using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuroraDesk.Core.Config;

/// <summary>Configuração do app (config.json). Posições de widgets ficam aqui, não no skin.</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "pt-BR";
    public string Theme { get; set; } = "Grafite";
    public bool FollowWindowsTheme { get; set; }
    public bool FollowWindowsAccent { get; set; }
    public bool WidgetsVisible { get; set; } = true;
    public bool FirstRunDone { get; set; }
    public List<WidgetInstance> Widgets { get; set; } = [];
    public DockConfig Dock { get; set; } = new();
    public PerformanceConfig Performance { get; set; } = new();
    public WeatherConfig Weather { get; set; } = new();
}

public sealed class WidgetInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Skin { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;
    public double Scale { get; set; } = 1;
    public double Opacity { get; set; } = 1;
    /// <summary>"desktop" (fixado na área de trabalho), "normal" ou "topo".</summary>
    public string Layer { get; set; } = "desktop";
    public bool ClickThrough { get; set; }
    public bool Locked { get; set; }
    public bool SnapToEdges { get; set; } = true;
    /// <summary>Nome do dispositivo do monitor (\\.\DISPLAY1). Vazio = principal.</summary>
    public string? Monitor { get; set; }
}

public sealed class DockConfig
{
    public bool Enabled { get; set; } = true;
    /// <summary>"inferior", "superior", "esquerda" ou "direita".</summary>
    public string Edge { get; set; } = "inferior";
    /// <summary>true = reserva espaço na tela (AppBar); false = flutuante.</summary>
    public bool ReserveSpace { get; set; }
    public bool AutoHide { get; set; }
    public string? Monitor { get; set; }
    public int IconSize { get; set; } = 44;
    public int Spacing { get; set; } = 6;
    public bool Zoom { get; set; } = true;
    public double ZoomFactor { get; set; } = 1.4;
    public bool ShowLabels { get; set; }
    public bool ShowRunning { get; set; } = true;
    /// <summary>"nenhum", "acrilico" ou "mica".</summary>
    public string Backdrop { get; set; } = "nenhum";
    public List<DockItem> Items { get; set; } = [];
}

public sealed class DockItem
{
    /// <summary>"arquivo" (exe/lnk/qualquer arquivo), "pasta", "app" (UWP via AppUserModelID) ou "separador".</summary>
    public string Kind { get; set; } = "arquivo";
    public string Path { get; set; } = "";
    public string? Arguments { get; set; }
    public string? Name { get; set; }
}

public sealed class PerformanceConfig
{
    public bool PauseOnFullscreen { get; set; } = true;
    public bool PauseOnLock { get; set; } = true;
    /// <summary>Na bateria, multiplica os intervalos por 3.</summary>
    public bool BatterySaver { get; set; }
    public bool TrimWorkingSet { get; set; } = true;
    /// <summary>
    /// Renderização por GPU. Desligada por padrão: com ela o WPF carrega os drivers de vídeo
    /// (NVIDIA/Intel) no processo, o que custa ~100 MB de RAM. Exige reiniciar o app.
    /// </summary>
    public bool HardwareRendering { get; set; }
}

public sealed class WeatherConfig
{
    public bool Enabled { get; set; }
    public double Latitude { get; set; } = -23.55;
    public double Longitude { get; set; } = -46.63;
    public string City { get; set; } = "São Paulo";
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;

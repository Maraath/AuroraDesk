using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuroraDesk.Core.Config;

/// <summary>Configuração do app (config.json). Posições de widgets ficam aqui, não no skin.</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "pt-BR";
    public string Theme { get; set; } = "Grafite";
    /// <summary>Segue o modo claro/escuro do Windows, alternando entre <see cref="DarkTheme"/> e <see cref="LightTheme"/>.</summary>
    public bool FollowWindowsTheme { get; set; }
    public string DarkTheme { get; set; } = "Grafite";
    public string LightTheme { get; set; } = "Neve";
    public bool FollowWindowsAccent { get; set; }
    /// <summary>Fonte global que substitui a do tema (vazio = usar a do tema).</summary>
    public string? FontOverride { get; set; }
    public bool WidgetsVisible { get; set; } = true;
    /// <summary>
    /// Oculta a barra de tarefas do Windows enquanto o app está aberto (o dock a substitui).
    /// Ao fechar, ou se o app cair, a barra volta ao estado original.
    /// </summary>
    public bool HideTaskbar { get; set; } = true;
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
    /// <summary>Fonte deste widget (sobrescreve a do skin e a do tema). Vazio = herda.</summary>
    public string? Font { get; set; }
    /// <summary>Nome do dispositivo do monitor (\\.\DISPLAY1). Vazio = principal.</summary>
    public string? Monitor { get; set; }
}

public sealed class DockConfig
{
    public bool Enabled { get; set; } = true;
    /// <summary>Itens padrão já foram criados (primeiro uso).</summary>
    public bool Initialized { get; set; }
    /// <summary>"inferior", "superior", "esquerda" ou "direita".</summary>
    public string Edge { get; set; } = "inferior";
    /// <summary>true = reserva espaço na tela (AppBar); false = flutuante.</summary>
    public bool ReserveSpace { get; set; }
    /// <summary>
    /// Reservar espaço quando a barra do Windows estiver oculta pelo app (padrão: sim): o dock
    /// assume o papel da barra e as janelas maximizadas param acima dele.
    /// </summary>
    public bool ReserveWhenTaskbarHidden { get; set; } = true;

    /// <summary>Reserva efetiva: com a barra do Windows oculta vale <see cref="ReserveWhenTaskbarHidden"/>.</summary>
    public bool EffectiveReserve(bool taskbarHidden) => taskbarHidden ? ReserveWhenTaskbarHidden : ReserveSpace;

    /// <summary>Muda a opção que está valendo agora (a do modo atual da barra).</summary>
    public void SetReserve(bool taskbarHidden, bool value)
    {
        if (taskbarHidden) ReserveWhenTaskbarHidden = value;
        else ReserveSpace = value;
    }
    public bool AutoHide { get; set; }
    public string? Monitor { get; set; }
    public int IconSize { get; set; } = 44;
    public int Spacing { get; set; } = 6;
    public bool Zoom { get; set; } = true;
    public double ZoomFactor { get; set; } = 1.4;
    public bool ShowLabels { get; set; } = true;
    public bool ShowRunning { get; set; } = true;
    /// <summary>"nenhum", "acrilico" ou "mica".</summary>
    public string Backdrop { get; set; } = "nenhum";
    public List<DockItem> Items { get; set; } = [];

    /// <summary>
    /// Estilo "três ilhas": widgets à esquerda, apps no centro, bandeja e relógio à direita
    /// (só nas bordas inferior e superior). Falso = só a ilha dos apps.
    /// </summary>
    public bool Islands { get; set; } = true;

    /// <summary>Itens da ilha esquerda, na ordem: cpu, ram, disco, rede, clima, bateria.</summary>
    public List<string> LeftItems { get; set; } = ["cpu", "ram", "rede", "clima"];

    /// <summary>Itens da ilha direita, na ordem: aurora, conexao, volume, bateria, relogio.</summary>
    public List<string> RightItems { get; set; } = ["aurora", "conexao", "volume", "bateria", "relogio"];

    public static readonly string[] AllLeftItems = ["cpu", "ram", "disco", "rede", "clima", "bateria"];
    public static readonly string[] AllRightItems = ["aurora", "conexao", "volume", "bateria", "relogio"];
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

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
    public WindowsStyleConfig WindowsStyle { get; set; } = new();
    /// <summary>Campos que esta versão não conhece (de uma versão mais nova): lidos e gravados de volta sem perda.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// "Windows com o tema do Aurora": cores oficiais de personalização e molduras das janelas.
/// Tudo pelo lado de fora (registro do usuário e atributos do DWM), sem entrar em outros programas.
/// </summary>
public sealed class WindowsStyleConfig
{
    /// <summary>Liga a sincronização das cores do Windows com o tema.</summary>
    public bool SyncColors { get; set; }
    public bool DarkMode { get; set; } = true;
    public bool Accent { get; set; } = true;
    public bool AccentOnTitleBars { get; set; } = true;
    /// <summary>
    /// Ao fechar o Aurora, devolver as cores originais do Windows. Padrão: não. As cores são
    /// do próprio Windows e continuam valendo sem o app ("Restaurar original" as devolve).
    /// </summary>
    public bool RestoreOnExit { get; set; }

    /// <summary>Pinta a barra de título/abas e a borda das janelas com as cores do tema.</summary>
    public bool FrameColors { get; set; }
    /// <summary>"todas" ou "explorer" (só o Explorador de Arquivos).</summary>
    public string FrameScope { get; set; } = "todas";
    /// <summary>Campos que esta versão não conhece (de uma versão mais nova): lidos e gravados de volta sem perda.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
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
    /// <summary>Campos que esta versão não conhece (de uma versão mais nova): lidos e gravados de volta sem perda.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
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
    /// <summary>Lupa no início do dock que abre a pesquisa do Aurora (apps, configurações e web).</summary>
    public bool SearchButton { get; set; } = true;
    /// <summary>Buscador da opção "pesquisar na web": "google", "bing" ou "duckduckgo".</summary>
    public string SearchEngine { get; set; } = "google";
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

    /// <summary>
    /// Seta ^ na ilha direita que abre os ícones dos outros apps da bandeja (Discord, Steam...).
    /// Usa o painel nativo do Windows, então clique esquerdo, direito e menus funcionam como sempre.
    /// </summary>
    public bool TrayOverflow { get; set; } = true;

    public static readonly string[] AllLeftItems = ["cpu", "ram", "disco", "rede", "clima", "bateria"];
    public static readonly string[] AllRightItems = ["aurora", "conexao", "volume", "bateria", "relogio"];
    /// <summary>Campos que esta versão não conhece (de uma versão mais nova): lidos e gravados de volta sem perda.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class DockItem
{
    /// <summary>"arquivo" (exe/lnk/qualquer arquivo), "pasta", "app" (UWP via AppUserModelID), "separador" ou "grupo".</summary>
    public string Kind { get; set; } = "arquivo";
    public string Path { get; set; } = "";
    public string? Arguments { get; set; }
    public string? Name { get; set; }
    /// <summary>Atalhos dentro de um grupo (só para Kind = "grupo"; grupos não se aninham).</summary>
    public List<DockItem>? Items { get; set; }

    [JsonIgnore]
    public bool IsGroup => Kind == "grupo";
    /// <summary>Campos que esta versão não conhece (de uma versão mais nova): lidos e gravados de volta sem perda.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Operações dos grupos de atalhos do dock (puras, sobre a lista de itens fixados).</summary>
public static class DockGroups
{
    /// <summary>Cria um grupo vazio (ou com <paramref name="first"/>, tirado do dock) na posição dele.</summary>
    public static DockItem Create(List<DockItem> items, string name, DockItem? first = null)
    {
        var group = new DockItem { Kind = "grupo", Name = name, Items = [] };
        int at = first is null ? items.Count : Math.Max(0, items.IndexOf(first));
        if (first is not null)
        {
            items.Remove(first);
            group.Items.Add(first);
        }
        items.Insert(Math.Min(at, items.Count), group);
        return group;
    }

    /// <summary>Adiciona ao grupo (tirando do dock se estiver lá). Separadores e grupos não entram.</summary>
    public static bool Add(List<DockItem> items, DockItem group, DockItem item)
    {
        if (!group.IsGroup || item.IsGroup || item.Kind == "separador" || ReferenceEquals(group, item)) return false;
        group.Items ??= [];
        if (group.Items.Any(i => SameTarget(i, item))) { items.Remove(item); return false; } // já está
        items.Remove(item);
        group.Items.Add(item);
        return true;
    }

    /// <summary>Tira do grupo; com <paramref name="toDock"/>, o atalho volta ao dock logo depois do grupo.</summary>
    public static void Remove(List<DockItem> items, DockItem group, DockItem item, bool toDock)
    {
        if (group.Items?.Remove(item) != true) return;
        if (toDock) items.Insert(Math.Min(items.IndexOf(group) + 1, items.Count), item);
    }

    /// <summary>Desfaz o grupo: os atalhos voltam ao dock no lugar dele.</summary>
    public static void Ungroup(List<DockItem> items, DockItem group)
    {
        int at = items.IndexOf(group);
        if (at < 0) return;
        items.RemoveAt(at);
        items.InsertRange(at, group.Items ?? []);
    }

    public static IEnumerable<DockItem> Groups(IEnumerable<DockItem> items) => items.Where(i => i.IsGroup);

    private static bool SameTarget(DockItem a, DockItem b) =>
        a.Kind == b.Kind && string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
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
    /// <summary>Campos que esta versão não conhece (de uma versão mais nova): lidos e gravados de volta sem perda.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class WeatherConfig
{
    public bool Enabled { get; set; }
    public double Latitude { get; set; } = -23.55;
    public double Longitude { get; set; } = -46.63;
    public string City { get; set; } = "São Paulo";
    /// <summary>Campos que esta versão não conhece (de uma versão mais nova): lidos e gravados de volta sem perda.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;

using System.Windows.Media.Imaging;
using AuroraDesk.Core.Config;
using AuroraDesk.Core.Dock;

namespace AuroraDesk.Dock;

/// <summary>Um item exibido no dock: fixado (com ou sem janelas abertas) ou só em execução.</summary>
internal sealed class DockEntry
{
    public DockItem? Pinned { get; init; }
    public bool IsSeparator => Pinned?.Kind == "separador";
    public required string Name { get; init; }
    public AppIdentity Identity { get; init; }
    /// <summary>Caminho do shell para o ícone (arquivo, pasta ou shell:AppsFolder\AUMID).</summary>
    public string? IconSource { get; init; }
    public BitmapSource? Icon { get; set; }
    /// <summary>Ícone desenhado com a fonte Segoe Fluent Icons (botões do próprio dock, como a pesquisa).</summary>
    public string? Glyph { get; init; }
    /// <summary>Botão de pesquisa do Windows (Win+S): fixo no início, não é um item fixado.</summary>
    public bool IsSearch { get; init; }
    public List<WindowRecord> Windows { get; } = [];
    public bool IsRunning => Windows.Count > 0;
    public bool IsActive { get; set; }
}

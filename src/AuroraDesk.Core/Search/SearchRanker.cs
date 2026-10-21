using System.Text;

namespace AuroraDesk.Core.Search;

/// <summary>Tipo de resultado da pesquisa do Aurora.</summary>
public enum ResultKind { App, Setting, Web, WindowsSearch }

/// <summary>Um item pesquisável: app do menu Iniciar ou página de configuração.</summary>
public sealed record SearchItem(string Name, string Target, ResultKind Kind, string? Keywords = null);

/// <summary>
/// Ordena itens pelo texto digitado, sem diferenciar maiúsculas nem acentos ("configuracoes"
/// encontra "Configurações"). Ordem: nome igual > começo do nome > começo de uma palavra >
/// iniciais ("vsc" → Visual Studio Code) > contém > palavras-chave. Tudo puro, para testar.
/// </summary>
public static class SearchRanker
{
    public static IReadOnlyList<SearchItem> Rank(string query, IEnumerable<SearchItem> items, int max = 8)
    {
        string q = Fold(query.Trim());
        if (q.Length == 0) return [];
        return items
            .Select(i => (Item: i, Score: Score(q, i)))
            .Where(p => p.Score > 0)
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.Item.Kind)              // empate: apps antes de configurações
            .ThenBy(p => p.Item.Name.Length)       // e o nome mais curto (mais "exato")
            .Take(max)
            .Select(p => p.Item)
            .ToList();
    }

    /// <summary>Pontuação de 0 (não combina) a 100.</summary>
    public static int Score(string foldedQuery, SearchItem item)
    {
        string name = Fold(item.Name);
        if (name == foldedQuery) return 100;
        if (name.StartsWith(foldedQuery, StringComparison.Ordinal)) return 90;

        var words = name.Split([' ', '-', '_', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(foldedQuery, StringComparison.Ordinal))) return 80;

        string initials = new(words.Where(w => w.Length > 0 && char.IsLetterOrDigit(w[0])).Select(w => w[0]).ToArray());
        if (foldedQuery.Length >= 2 && initials.StartsWith(foldedQuery, StringComparison.Ordinal)) return 70;

        // Sem pontuação nem espaços: "wifi" encontra "Wi-Fi", "blocodenotas" encontra "Bloco de Notas".
        string compactName = Compact(name), compactQuery = Compact(foldedQuery);
        if (compactQuery.Length >= 2 && compactName.StartsWith(compactQuery, StringComparison.Ordinal)) return 75;

        if (foldedQuery.Length >= 2 && name.Contains(foldedQuery, StringComparison.Ordinal)) return 60;
        if (compactQuery.Length >= 3 && compactName.Contains(compactQuery, StringComparison.Ordinal)) return 55;

        if (item.Keywords is { } k && foldedQuery.Length >= 2)
        {
            var keywords = Fold(k).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (keywords.Any(w => w.StartsWith(foldedQuery, StringComparison.Ordinal))) return 50;
        }
        return 0;
    }

    private static string Compact(string folded) => new(folded.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Minúsculas e sem acentos (tabela própria: o app roda com InvariantGlobalization).</summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            char l = char.ToLowerInvariant(c);
            sb.Append(l switch
            {
                'á' or 'à' or 'â' or 'ã' or 'ä' or 'å' => 'a',
                'é' or 'è' or 'ê' or 'ë' => 'e',
                'í' or 'ì' or 'î' or 'ï' => 'i',
                'ó' or 'ò' or 'ô' or 'õ' or 'ö' => 'o',
                'ú' or 'ù' or 'û' or 'ü' => 'u',
                'ç' => 'c',
                'ñ' => 'n',
                _ => l,
            });
        }
        return sb.ToString();
    }

    /// <summary>Endereço da pesquisa na web no buscador escolhido.</summary>
    public static string WebUrl(string query, string engine)
    {
        string q = Uri.EscapeDataString(query.Trim());
        return engine.ToLowerInvariant() switch
        {
            "bing" => "https://www.bing.com/search?q=" + q,
            "duckduckgo" => "https://duckduckgo.com/?q=" + q,
            _ => "https://www.google.com/search?q=" + q,
        };
    }
}

/// <summary>Páginas de configuração do Windows mais procuradas (ms-settings:), em português.</summary>
public static class SettingsCatalog
{
    public static readonly IReadOnlyList<SearchItem> All =
    [
        S("Wi-Fi", "ms-settings:network-wifi", "rede internet wireless"),
        S("Rede e Internet", "ms-settings:network-status", "conexao ethernet cabo"),
        S("Bluetooth e dispositivos", "ms-settings:bluetooth", "fone mouse teclado"),
        S("Tela", "ms-settings:display", "monitor resolucao brilho escala"),
        S("Som", "ms-settings:sound", "audio volume microfone alto-falante"),
        S("Notificações", "ms-settings:notifications", "avisos nao perturbe"),
        S("Energia e bateria", "ms-settings:powersleep", "suspender economia"),
        S("Armazenamento", "ms-settings:storagesense", "disco espaco limpeza"),
        S("Personalização", "ms-settings:personalization", "tema papel de parede cores"),
        S("Papel de parede", "ms-settings:personalization-background", "fundo imagem"),
        S("Cores", "ms-settings:colors", "modo escuro claro destaque"),
        S("Aplicativos instalados", "ms-settings:appsfeatures", "programas desinstalar"),
        S("Aplicativos de inicialização", "ms-settings:startupapps", "iniciar com o windows"),
        S("Aplicativos padrão", "ms-settings:defaultapps", "navegador padrao"),
        S("Contas", "ms-settings:yourinfo", "usuario perfil"),
        S("Data e hora", "ms-settings:dateandtime", "relogio fuso horario"),
        S("Idioma e região", "ms-settings:regionlanguage", "teclado idioma"),
        S("Windows Update", "ms-settings:windowsupdate", "atualizacoes atualizar"),
        S("Privacidade e segurança", "ms-settings:privacy", "permissoes"),
        S("Segurança do Windows", "windowsdefender:", "antivirus defender"),
        S("Impressoras e scanners", "ms-settings:printers", "impressora"),
        S("Mouse", "ms-settings:mousetouchpad", "cursor ponteiro"),
        S("Touchpad", "ms-settings:devices-touchpad", "gestos"),
        S("Teclado", "ms-settings:keyboard", "digitacao"),
        S("Sobre o sistema", "ms-settings:about", "especificacoes nome do computador"),
        S("Painel de Controle", "control.exe", "painel"),
        S("Gerenciador de Tarefas", "taskmgr.exe", "processos desempenho"),
    ];

    private static SearchItem S(string name, string target, string keywords) => new(name, target, ResultKind.Setting, keywords);
}

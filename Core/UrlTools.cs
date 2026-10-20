using System.Text;
using System.Text.RegularExpressions;

namespace SneznyjBrowser;

public static class UrlTools
{
    public const string StartHost = "start.sneznyj";

    public static readonly Dictionary<string, (string Name, string Template)> Engines = new()
    {
        ["google"] = ("Google", "https://www.google.com/search?q={0}"),
        ["duckduckgo"] = ("DuckDuckGo", "https://duckduckgo.com/?q={0}"),
        ["yandex"] = ("Яндекс", "https://yandex.ru/search/?text={0}"),
        ["bing"] = ("Bing", "https://www.bing.com/search?q={0}"),
        ["brave"] = ("Brave Search", "https://search.brave.com/search?q={0}"),
    };

    public static string EngineName(string engine) =>
        Engines.TryGetValue(engine, out var e) ? e.Name : Engines["google"].Name;

    public static string SearchTemplate(string engine) =>
        Engines.TryGetValue(engine, out var e) ? e.Template : Engines["google"].Template;

    public static string SearchUrl(string engine, string query) =>
        string.Format(SearchTemplate(engine), Uri.EscapeDataString(query.Trim()));

    public static string StartUrl(string workspaceId) =>
        $"https://{StartHost}/index.html?ws={Uri.EscapeDataString(workspaceId)}&t={DateTime.Now.Ticks}";

    public static bool IsStart(string? url) =>
        url != null && url.StartsWith("https://" + StartHost + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Пустая вкладка или стартовая (не внутренние страницы вроде настроек).</summary>
    public static bool IsNewTab(string? url) =>
        string.IsNullOrEmpty(url) || (IsStart(url) && InternalPage(url) == null);

    /// <summary>Внутренние страницы браузера: адрес sneznyj://settings → https://start.sneznyj/settings.html.</summary>
    public static readonly Dictionary<string, (string Title, string Glyph)> Pages = new()
    {
        ["settings"] = ("Настройки", "\uE713"),
        ["history"] = ("История", "\uE81C"),
        ["downloads"] = ("Загрузки", "\uE896"),
        ["bookmarks"] = ("Закладки", "\uE734"),
        ["reader"] = ("Режим чтения", "\uE736"),
    };

    public static string PageUrl(string page, string? anchor = null) =>
        $"https://{StartHost}/{page}.html" + (string.IsNullOrEmpty(anchor) ? "" : "#" + anchor);

    /// <summary>Имя внутренней страницы по адресу (settings, history…) или null.</summary>
    public static string? InternalPage(string? url)
    {
        if (!IsStart(url)) return null;
        var path = url!.Substring(("https://" + StartHost + "/").Length);
        int cut = path.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) path = path[..cut];
        if (path.EndsWith(".html")) path = path[..^5];
        return Pages.ContainsKey(path) ? path : null;
    }

    public static string? InternalGlyph(string? url) => InternalPage(url) is { } p ? Pages[p].Glyph : null;

    public static bool IsWeb(string? url) =>
        url != null && !IsStart(url) &&
        (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    static readonly Regex Scheme = new(@"^[a-zA-Z][a-zA-Z0-9+.\-]*://", RegexOptions.Compiled);
    static readonly Regex Local = new(@"^(localhost|127\.0\.0\.1|\[::1\]|[\w\-]+\.(localhost|test))(:\d+)?([/?#].*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Ip = new(@"^\d{1,3}(\.\d{1,3}){3}(:\d+)?([/?#].*)?$", RegexOptions.Compiled);
    static readonly Regex Domain = new(@"^[^\s/?#]+\.[a-zA-Zа-яА-ЯёЁ]{2,}\.?(:\d+)?([/?#].*)?$", RegexOptions.Compiled);
    static readonly Regex WinPath = new(@"^[a-zA-Z]:\\", RegexOptions.Compiled);

    /// <summary>Превращает ввод в адресной строке в ссылку: адрес сайта или поиск.</summary>
    public static string ToUrl(string input, string engine)
    {
        var s = input.Trim();
        if (s.Length == 0) return "";
        foreach (var prefix in new[] { "sneznyj://", "browser://", "chrome://", "edge://", "about:" })
        {
            if (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = s[prefix.Length..].Trim('/');
            int hash = rest.IndexOf('#');
            var name = (hash >= 0 ? rest[..hash] : rest).ToLowerInvariant();
            if (name == "newtab") return "";
            if (Pages.ContainsKey(name)) return PageUrl(name, hash >= 0 ? rest[(hash + 1)..] : null);
        }
        if (Scheme.IsMatch(s)) return s;
        if (s.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return s;
        if (WinPath.IsMatch(s)) { try { return new Uri(s).AbsoluteUri; } catch { } }
        if (s.Contains(' ')) return SearchUrl(engine, s);
        if (Local.IsMatch(s) || Ip.IsMatch(s)) return "http://" + s;
        if (Domain.IsMatch(s)) return "https://" + s;
        return SearchUrl(engine, s);
    }

    public static bool LooksLikeAddress(string input)
    {
        var s = input.Trim();
        return s.Length > 0 && !s.Contains(' ') &&
               (Scheme.IsMatch(s) || Local.IsMatch(s) || Ip.IsMatch(s) || Domain.IsMatch(s) || WinPath.IsMatch(s) ||
                s.StartsWith("about:", StringComparison.OrdinalIgnoreCase));
    }

    public static string Host(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try { return new Uri(url).Host.ToLowerInvariant(); } catch { return ""; }
    }

    public static bool IsLocalHost(string host) =>
        host is "localhost" or "127.0.0.1" or "[::1]" || host.EndsWith(".localhost") || host.EndsWith(".test") ||
        host.EndsWith(".local") || System.Net.IPAddress.TryParse(host.Trim('[', ']'), out _) || !host.Contains('.');

    public static string SiteKey(string host) => host.StartsWith("www.") ? host[4..] : host;

    public static string Display(string? url)
    {
        if (!IsStart(url)) return url ?? "";
        if (InternalPage(url) is not { } page) return "";
        int hash = url!.IndexOf('#');
        return "sneznyj://" + page + (hash >= 0 ? url[hash..] : "");
    }
}

/// <summary>Поиск по словам и с учётом неправильной раскладки (vtnhbrf → метрика).</summary>
public static class TextMatch
{
    const string En = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
    const string Ru = "йцукенгшщзхъфывапролджэячсмитьбюё";

    public static string SwapLayout(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
        {
            int i = En.IndexOf(ch);
            if (i >= 0) { sb.Append(Ru[i]); continue; }
            i = Ru.IndexOf(ch);
            sb.Append(i >= 0 ? En[i] : ch);
        }
        return sb.ToString();
    }

    public static bool All(string text, string query)
    {
        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    public static Func<string, bool> Matcher(string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return _ => true;
        var swapped = SwapLayout(q);
        return text => All(text, q) || All(text, swapped);
    }
}

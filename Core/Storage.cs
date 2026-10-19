using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace SneznyjBrowser;

/// <summary>Все данные браузера лежат только на этом компьютере, в папке пользователя.</summary>
public static class AppPaths
{
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SneznyjBrowser");

    /// <summary>Своя папка данных через SNEZNYJ_BROWSER_HOME — для тестов и второго профиля.</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("SNEZNYJ_BROWSER_HOME") is { Length: > 0 } home ? Path.GetFullPath(home) : DefaultRoot;

    /// <summary>Имя для мьютекса и канала: у каждой папки данных своя копия браузера.</summary>
    public static string InstanceKey { get; } = Environment.UserName +
        (string.Equals(Root, DefaultRoot, StringComparison.OrdinalIgnoreCase) ? ""
            : "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Root.ToLowerInvariant())))[..12]);

    public static string Profile => Path.Combine(Root, "Profile");
    public static string Start => Path.Combine(Root, "start");
    public static string Fav => Path.Combine(Start, "fav");
    public static string SessionFile => Path.Combine(Root, "session.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string HistoryFile => Path.Combine(Root, "history.json");
    public static string CustomBlocklist => Path.Combine(Root, "blocklist.txt");
    public static string UpdateStateFile => Path.Combine(Root, "update.json");
    public static string Updates => Path.Combine(Root, "updates");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Start);
        Directory.CreateDirectory(Fav);
    }
}

public static class Json
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch
        {
            try { File.Copy(path, path + ".broken", true); } catch { }
        }
        return new T();
    }

    public static void Save<T>(string path, T data)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        File.Move(tmp, path, true);
    }
}

public sealed class PanelAppData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class SettingsData
{
    /// <summary>google, duckduckgo, yandex, bing, brave</summary>
    public string SearchEngine { get; set; } = "google";
    public bool AdBlock { get; set; } = true;
    /// <summary>none, basic, balanced, strict</summary>
    public string TrackingPrevention { get; set; } = "balanced";
    /// <summary>Пароли хранятся локально, зашифрованными средствами Windows.</summary>
    public bool SavePasswords { get; set; } = true;
    /// <summary>Автозаполнение форм (адреса, карты) по умолчанию выключено.</summary>
    public bool Autofill { get; set; }
    public bool SleepingTabs { get; set; } = true;
    public int SleepAfterMinutes { get; set; } = 10;
    public List<string>? AdBlockAllowSites { get; set; }
    public List<PanelAppData>? PanelApps { get; set; }
    /// <summary>Раз в сутки проверять, не вышла ли новая версия браузера.</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Свой адрес файла версии (для проверки обновлений на тестовом сервере).</summary>
    public string? UpdateUrl { get; set; }

    public void Normalize()
    {
        if (!UrlTools.Engines.ContainsKey(SearchEngine)) SearchEngine = "google";
        if (SleepAfterMinutes < 1) SleepAfterMinutes = 10;
        AdBlockAllowSites ??= new()
        {
            // Кабинеты аналитики сами грузят свои счётчики — им блокировщик мешает.
            "metrika.yandex.ru", "direct.yandex.ru", "analytics.google.com",
            "tagmanager.google.com", "tagassistant.google.com", "ads.google.com", "facebook.com",
        };
        if (PanelApps == null || PanelApps.Count == 0) PanelApps = Defaults.PanelApps(SearchEngine);
    }

    public bool IsAllowed(string site)
    {
        if (string.IsNullOrEmpty(site) || AdBlockAllowSites == null) return false;
        foreach (var s in AdBlockAllowSites)
            if (site.Equals(s, StringComparison.OrdinalIgnoreCase) || site.EndsWith("." + s, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public CoreWebView2TrackingPreventionLevel TrackingLevel() => TrackingPrevention switch
    {
        "none" => CoreWebView2TrackingPreventionLevel.None,
        "basic" => CoreWebView2TrackingPreventionLevel.Basic,
        "strict" => CoreWebView2TrackingPreventionLevel.Strict,
        _ => CoreWebView2TrackingPreventionLevel.Balanced,
    };
}

public sealed class TabData
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string? WorkspaceId { get; set; }
}

public sealed class WorkspaceData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "";
    public string Accent { get; set; } = "";
    public int ActiveTab { get; set; }
    public List<TabData>? Tabs { get; set; }
    public List<QuickLink>? Links { get; set; }
}

public sealed class SessionData
{
    public string? ActiveWorkspace { get; set; }
    public bool PanelOpen { get; set; }
    public string? PanelApp { get; set; }
    public double PanelWidth { get; set; } = 400;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Maximized { get; set; }
    public List<WorkspaceData>? Workspaces { get; set; }
    public List<TabData>? Closed { get; set; }
}

/// <summary>Набор пространств для первого запуска.</summary>
public sealed record WorkspaceTemplate(string Id, string Title, string Description, string Glyph, Func<List<WorkspaceData>> Create);

public static class Defaults
{
    static QuickLink L(string title, string url) => new() { Title = title, Url = url };

    static WorkspaceData W(string id, string name, string glyph, string accent, params QuickLink[] links) => new()
    {
        Id = id, Name = name, Glyph = glyph, Accent = accent, Links = links.ToList(),
    };

    /// <summary>Шаблоны первого запуска: без личных ссылок, всё можно поменять потом.</summary>
    public static readonly WorkspaceTemplate[] Templates =
    {
        new("simple", "Всё в одном", "Одно пространство с популярными сайтами", "\uE80F", () => new()
        {
            W("main", "Главное", "\uE80F", "#7CC4FF",
                L("Яндекс", "https://ya.ru"),
                L("Google", "https://www.google.com"),
                L("YouTube", "https://www.youtube.com"),
                L("ВКонтакте", "https://vk.com"),
                L("Telegram", "https://web.telegram.org/a/"),
                L("Яндекс Почта", "https://mail.yandex.ru"),
                L("Gmail", "https://mail.google.com"),
                L("Погода", "https://yandex.ru/pogoda"),
                L("Карты", "https://yandex.ru/maps"),
                L("Ozon", "https://www.ozon.ru"),
                L("Wildberries", "https://www.wildberries.ru"),
                L("Википедия", "https://ru.wikipedia.org")),
        }),
        new("spaces", "Дом, работа, отдых", "Три пространства: у каждого свои вкладки и плитки", "\uE8A9", () => new()
        {
            W("main", "Главное", "\uE80F", "#7CC4FF",
                L("Яндекс", "https://ya.ru"),
                L("Google", "https://www.google.com"),
                L("Погода", "https://yandex.ru/pogoda"),
                L("Карты", "https://yandex.ru/maps"),
                L("Переводчик", "https://translate.yandex.ru"),
                L("Википедия", "https://ru.wikipedia.org")),
            W("work", "Работа", "\uE821", "#6EE7B7",
                L("Яндекс Почта", "https://mail.yandex.ru"),
                L("Gmail", "https://mail.google.com"),
                L("Google Диск", "https://drive.google.com"),
                L("Яндекс Диск", "https://disk.yandex.ru"),
                L("Документы", "https://docs.google.com"),
                L("Календарь", "https://calendar.google.com")),
            W("fun", "Отдых", "\uEB51", "#F9A8D4",
                L("YouTube", "https://www.youtube.com"),
                L("ВКонтакте", "https://vk.com"),
                L("Telegram", "https://web.telegram.org/a/"),
                L("Кинопоиск", "https://www.kinopoisk.ru"),
                L("Яндекс Музыка", "https://music.yandex.ru"),
                L("Pinterest", "https://www.pinterest.com"),
                L("Ozon", "https://www.ozon.ru"),
                L("Wildberries", "https://www.wildberries.ru")),
        }),
        new("blank", "С чистого листа", "Пустая стартовая: добавите свои сайты сами", "\uE710", () => new()
        {
            W("main", "Главное", "\uE80F", "#7CC4FF"),
        }),
    };

    public static List<WorkspaceData> Workspaces(string? templateId = null)
    {
        var t = Templates.FirstOrDefault(t => t.Id == templateId) ?? Templates[0];
        var list = t.Create();
        list[0].Tabs = new() { new TabData() };
        return list;
    }

    /// <summary>Боковая панель: мессенджеры и почта. Почта — у того же, кто выбран поиском.</summary>
    public static List<PanelAppData> PanelApps(string searchEngine = "google") => new()
    {
        new() { Id = "telegram", Name = "Telegram", Glyph = "\uE8BD", Url = "https://web.telegram.org/a/" },
        new() { Id = "whatsapp", Name = "WhatsApp", Glyph = "\uE717", Url = "https://web.whatsapp.com/" },
        new() { Id = "vk", Name = "ВКонтакте", Glyph = "\uE8F2", Url = "https://vk.com/im" },
        searchEngine == "yandex"
            ? new() { Id = "mail", Name = "Почта", Glyph = "\uE715", Url = "https://mail.yandex.ru/" }
            : new() { Id = "mail", Name = "Почта", Glyph = "\uE715", Url = "https://mail.google.com/" },
    };

    public static readonly (string Glyph, string Accent)[] Looks =
    {
        ("\uE774", "#7CC4FF"), ("\uE9D2", "#6EE7B7"), ("\uE7FC", "#FBBF24"), ("\uEB51", "#F9A8D4"),
        ("\uE80F", "#C4B5FD"), ("\uE8B7", "#93C5FD"), ("\uE734", "#FCA5A5"), ("\uE8D6", "#5EEAD4"),
        ("\uE719", "#FDBA74"), ("\uE945", "#FDE68A"), ("\uE77B", "#A5B4FC"), ("\uE7BE", "#86EFAC"),
    };
}

using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SneznyjBrowser;

/// <summary>Собирает локальную стартовую страницу (https://start.sneznyj/) из шаблона и ссылок пространств.</summary>
public static class StartPage
{
    static string? _template;

    public static void Write(IEnumerable<Workspace> workspaces, SettingsData settings)
    {
        _template ??= ReadTemplate();
        var data = new
        {
            search = UrlTools.SearchTemplate(settings.SearchEngine),
            engine = UrlTools.EngineName(settings.SearchEngine),
            bg = settings.StartBackground,
            image = settings.StartImage is { Length: > 0 } img && File.Exists(Path.Combine(AppPaths.Start, img)) ? img : null,
            snow = settings.StartSnow,
            clock = settings.StartClock,
            workspaces = WorkspacesData(workspaces),
        };
        // Обычный сериализатор экранирует < > & — данные безопасно вставлять внутрь <script>.
        var json = JsonSerializer.Serialize(data);
        var html = _template.Replace("/*__DATA__*/null", json);
        WriteIfChanged(Path.Combine(AppPaths.Start, "index.html"), html);
        Favicons.Dirty = false;
    }

    /// <summary>Плитки всех пространств — для страницы и для обновления уже открытых стартовых.</summary>
    public static IEnumerable<object> WorkspacesData(IEnumerable<Workspace> workspaces) =>
        workspaces.Select(w => new
        {
            id = w.Id,
            name = w.Name,
            accent = w.Accent,
            links = w.Links.Select(l => LinkData(l.Title, l.Url)).ToList(),
        }).ToList();

    public static object LinkData(string title, string url)
    {
        var host = UrlTools.Host(url);
        var fav = host.Length > 0 && File.Exists(Favicons.PathFor(host)) ? "fav/" + Favicons.FileName(host) : null;
        return new { title, url, host = UrlTools.SiteKey(host), fav, logo = SiteLogos.RelUrl(host) };
    }

    /// <summary>
    /// Пишет файл стартовой, только если он изменился, и целиком (через временный файл), чтобы движок
    /// никогда не застал его пустым или наполовину записанным.
    /// </summary>
    public static bool WriteIfChanged(string path, string text)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                return false;
        }
        catch { }
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            try
            {
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Файл прямо сейчас читает движок — пишем поверх, как раньше.
                File.WriteAllBytes(path, bytes);
                return true;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("запись " + Path.GetFileName(path), ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>Файл стартовой по адресу https://start.sneznyj/… (только внутри папки стартовой).</summary>
    public static string? LocalFile(string url)
    {
        if (!UrlTools.IsStart(url)) return null;
        string rel;
        try { rel = Uri.UnescapeDataString(new Uri(url).AbsolutePath.TrimStart('/')); } catch { return null; }
        if (rel.Length == 0) rel = "index.html";
        var root = Path.GetFullPath(AppPaths.Start) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
    }

    public static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream",
    };

    /// <summary>Подсказки «Популярные сайты» в окне добавления плитки (пока история ещё пустая).</summary>
    public static readonly (string Title, string Url)[] Popular =
    {
        ("YouTube", "https://www.youtube.com/"),
        ("Яндекс", "https://ya.ru/"),
        ("Google", "https://www.google.com/"),
        ("ВКонтакте", "https://vk.com/"),
        ("Telegram", "https://web.telegram.org/a/"),
        ("WhatsApp", "https://web.whatsapp.com/"),
        ("Gmail", "https://mail.google.com/"),
        ("Яндекс Почта", "https://mail.yandex.ru/"),
        ("Википедия", "https://ru.wikipedia.org/"),
        ("Кинопоиск", "https://www.kinopoisk.ru/"),
        ("Ozon", "https://www.ozon.ru/"),
        ("Wildberries", "https://www.wildberries.ru/"),
        ("Авито", "https://www.avito.ru/"),
        ("Дзен", "https://dzen.ru/"),
        ("Переводчик", "https://translate.yandex.ru/"),
        ("Карты", "https://yandex.ru/maps/"),
        ("Claude", "https://claude.ai/"),
        ("GitHub", "https://github.com/"),
    };

    static string ReadTemplate()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("start.html");
        if (s == null) return "<!doctype html><meta charset=utf-8><title>Новая вкладка</title>";
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }
}

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
            workspaces = workspaces.Select(w => new
            {
                id = w.Id,
                name = w.Name,
                accent = w.Accent,
                links = w.Links.Select(l =>
                {
                    var host = UrlTools.Host(l.Url);
                    var fav = host.Length > 0 && File.Exists(Favicons.PathFor(host)) ? "fav/" + Favicons.FileName(host) : null;
                    return new { title = l.Title, url = l.Url, host = UrlTools.SiteKey(host), fav };
                }),
            }),
        };
        // Обычный сериализатор экранирует < > & — данные безопасно вставлять внутрь <script>.
        var json = JsonSerializer.Serialize(data);
        var html = _template.Replace("/*__DATA__*/null", json);
        try
        {
            File.WriteAllText(Path.Combine(AppPaths.Start, "index.html"), html, new UTF8Encoding(false));
            Favicons.Dirty = false;
        }
        catch { }
    }

    static string ReadTemplate()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("start.html");
        if (s == null) return "<!doctype html><meta charset=utf-8><title>Новая вкладка</title>";
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }
}

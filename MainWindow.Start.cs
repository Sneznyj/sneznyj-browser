using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace SneznyjBrowser;

/// <summary>Стартовая (Табло): плитки сайтов, подсказки для них, крупные значки и запасной путь открытия страницы.</summary>
public partial class MainWindow
{
    readonly Dictionary<BrowserTab, int> _startRetries = new();

    /// <summary>Открытые стартовые (не внутренние страницы).</summary>
    IEnumerable<CoreWebView2> StartCores() =>
        _workspaces.SelectMany(w => w.Tabs)
            .Where(t => UrlTools.IsStart(t.Url) && UrlTools.InternalPage(t.Url) == null)
            .Select(t => t.View?.CoreWebView2)
            .Where(c => c != null)!;

    object LinksPayload() => new { type = "links", workspaces = StartPage.WorkspacesData(_workspaces) };

    /// <summary>Плитки поменялись: пересобрать стартовую и сразу показать изменения во всех открытых.</summary>
    void StartLinksChanged()
    {
        StartPage.Write(_workspaces, _settings);
        InternalPages.Broadcast(StartCores(), LinksPayload());
        QueueSave();
    }

    /// <summary>Подтянуть крупные значки для плиток (с самих сайтов, в фоне); плитки обновляются по мере загрузки.</summary>
    async void FetchStartLogos(IEnumerable<string> urls)
    {
        try
        {
            var hosts = SiteLogos.Missing(urls);
            if (hosts.Count > 0) await SiteLogos.FetchAsync(hosts, () => Dispatcher.BeginInvoke(QueueStartRefresh));
        }
        catch (Exception ex) { CrashLog.Write("значки плиток", ex); }
    }

    System.Windows.Threading.DispatcherTimer? _startRefresh;

    /// <summary>Несколько значков пришли почти разом — стартовую пересобираем один раз.</summary>
    void QueueStartRefresh()
    {
        if (_startRefresh == null)
        {
            _startRefresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _startRefresh.Tick += (_, _) => { _startRefresh.Stop(); StartLinksChanged(); };
        }
        _startRefresh.Stop();
        _startRefresh.Start();
    }

    /// <summary>После запуска — значки для плиток, у которых их ещё нет.</summary>
    void ScheduleStartLogos() =>
        _ = Task.Delay(TimeSpan.FromSeconds(6)).ContinueWith(_ =>
            Dispatcher.BeginInvoke(() => FetchStartLogos(_workspaces.SelectMany(w => w.Links).Select(l => l.Url).ToList())));

    /// <summary>Что предложить в окне «Добавить сайт»: часто открываемые сайты из истории и популярные.</summary>
    object SuggestPayload(string wsId)
    {
        var ws = _workspaces.FirstOrDefault(w => w.Id == wsId) ?? _current;
        var taken = new HashSet<string>((ws?.Links ?? new()).Select(l => UrlTools.SiteKey(UrlTools.Host(l.Url))), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frequent = new List<object>();
        foreach (var e in _history.Ranked())
        {
            if (frequent.Count >= 12) break;
            if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)) continue;
            var host = uri.Host.ToLowerInvariant();
            var key = UrlTools.SiteKey(host);
            if (host.Length == 0 || UrlTools.IsLocalHost(host) || IsServiceHost(host) || taken.Contains(key) || !seen.Add(key)) continue;
            var root = $"{uri.Scheme}://{uri.Authority}/";
            var title = _history.All.FirstOrDefault(h => h.Url == root || h.Url == root.TrimEnd('/'))?.Title;
            frequent.Add(StartPage.LinkData(NiceTitle(title, key), root));
        }
        var popular = StartPage.Popular
            .Where(p => UrlTools.SiteKey(UrlTools.Host(p.Url)) is var k && !taken.Contains(k) && !seen.Contains(k))
            .Select(p => StartPage.LinkData(p.Title, p.Url))
            .ToList();
        return new { type = "suggest", ws = ws?.Id, frequent, popular };
    }

    /// <summary>Страницы входа и служебные адреса — плитками их не предлагаем.</summary>
    static bool IsServiceHost(string host) =>
        host.StartsWith("accounts.") || host.StartsWith("login.") || host.StartsWith("auth.") || host.StartsWith("oauth.") ||
        host.StartsWith("sso.") || host.StartsWith("passport.") || host.StartsWith("id.");

    /// <summary>«Яндекс — быстрый поиск в интернете» → «Яндекс»; без заголовка — адрес сайта.</summary>
    static string NiceTitle(string? title, string fallback)
    {
        var t = (title ?? "").Trim();
        foreach (var sep in new[] { " — ", " – ", " - ", " | ", " · ", ": " })
            if (t.IndexOf(sep, StringComparison.Ordinal) is > 1 and var i) t = t[..i];
        t = t.Trim();
        if (t.Length == 0 || t.Length > 32) t = fallback;
        return t;
    }

    // ───────────────────────── как открываются свои страницы ─────────────────────────

    /// <summary>
    /// Файлы https://start.sneznyj/ отдаёт сама программа. Раньше их читал с диска движок (виртуальный хост),
    /// но после обновления браузера он мог весь сеанс получать отказ (ERR_ACCESS_DENIED) — стартовая не открывалась.
    /// </summary>
    void ServeStartFiles(CoreWebView2 core)
    {
        string? mainUrl = null;   // адрес, который сейчас открывается во вкладке целиком (не во фрейме)
        core.NavigationStarting += (_, e) => mainUrl = e.Uri;
        core.AddWebResourceRequestedFilter($"https://{UrlTools.StartHost}/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, e) => ServeStartFile(core, e, mainUrl);
    }

    void ServeStartFile(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e, string? mainUrl)
    {
        if (_env == null || !UrlTools.IsStart(e.Request.Uri)) return;
        // Свои страницы открываются только целиком во вкладке, а их картинки и скрипты берут только они сами:
        // чужой сайт не встроит их во фрейм и не достанет оттуда файлы.
        bool allowed = e.ResourceContext == CoreWebView2WebResourceContext.Document
            ? string.Equals(e.Request.Uri, mainUrl, StringComparison.OrdinalIgnoreCase)
            : UrlTools.IsStart(core.Source);
        if (!allowed)
        {
            e.Response = _env.CreateWebResourceResponse(null, 403, "Forbidden", "");
            return;
        }
        var path = StartPage.LocalFile(e.Request.Uri);
        if (path == null)
        {
            e.Response = _env.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        try
        {
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
            }
            e.Response = _env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK",
                $"Content-Type: {StartPage.MimeType(path)}\r\nCache-Control: no-store\r\nX-Frame-Options: DENY");
        }
        catch (Exception ex)
        {
            CrashLog.Write("стартовая: " + Path.GetFileName(path), ex);
            e.Response = _env.CreateWebResourceResponse(null, 500, "Error", "");
        }
    }

    /// <summary>Своя страница всё же не открылась — пробуем ещё раз (до двух раз) и пишем в журнал.</summary>
    bool StartPageCompleted(CoreWebView2 core, BrowserTab tab, CoreWebView2NavigationCompletedEventArgs e)
    {
        var url = core.Source;
        if (e.IsSuccess) { _startRetries.Remove(tab); return false; }
        if (!UrlTools.IsStart(url) || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return false;
        int n = _startRetries.GetValueOrDefault(tab);
        CrashLog.Note($"своя страница не открылась ({e.WebErrorStatus}, код {e.HttpStatusCode}), попытка {n + 1}: {url}");
        if (n >= 2) return false;
        _startRetries[tab] = n + 1;
        _ = Task.Delay(n == 0 ? 250 : 700).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
        {
            if (tab.View?.CoreWebView2 != core || !UrlTools.IsStart(core.Source)) return;
            try { core.Navigate(core.Source); } catch { }
        }));
        return true;
    }
}

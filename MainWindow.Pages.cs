using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace SneznyjBrowser;

/// <summary>Внутренние страницы браузера (sneznyj://settings, history, downloads, bookmarks) лежат в папке стартовой.</summary>
public static class InternalPages
{
    static readonly string[] Files = { "settings.html", "history.html", "downloads.html", "bookmarks.html", "reader.html", "ui.css", "ui.js" };

    public static void WriteAll()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var f in Files)
        {
            try
            {
                using var s = asm.GetManifestResourceStream("pages/" + f);
                if (s == null) continue;
                using var r = new StreamReader(s, Encoding.UTF8);
                File.WriteAllText(Path.Combine(AppPaths.Start, f), r.ReadToEnd(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    public static void Post(CoreWebView2 core, object payload)
    {
        try { core.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json.Compact)); } catch { }
    }

    public static void Broadcast(IEnumerable<CoreWebView2> cores, object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json.Compact);
        foreach (var c in cores)
            try { c.PostWebMessageAsJson(json); } catch { }
    }
}

public partial class MainWindow
{
    /// <summary>Открытые страницы браузера: стартовые и внутренние (или только одна из них).</summary>
    IEnumerable<CoreWebView2> AllPageCores(string? page = null) =>
        _workspaces.SelectMany(w => w.Tabs)
            .Where(t => UrlTools.IsStart(t.Url) && (page == null || UrlTools.InternalPage(t.Url) == page))
            .Select(t => t.View?.CoreWebView2)
            .Where(c => c != null)!;

    void BroadcastSettings() => InternalPages.Broadcast(AllPageCores("settings"), SettingsPayload());

    void OnPageMessage(CoreWebView2 core, BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Команды принимаются только от своих страниц: https://start.sneznyj/… (сайты сюда писать не могут).
        if (!UrlTools.IsStart(e.Source)) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var r = doc.RootElement;
            string Str(string name) => r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            var cmd = Str("cmd");
            switch (cmd)
            {
                case "removeLink":
                case "addLink":
                case "moveLink":
                    OnStartLinks(cmd, r);
                    break;
                case "init":
                    if (PayloadFor(Str("page"), r) is { } payload) InternalPages.Post(core, payload);
                    break;
                case "set":
                    if (r.TryGetProperty("value", out var value)) ApplySetting(Str("key"), value.Clone());
                    break;
                case "history":
                    InternalPages.Post(core, HistoryPayload(Str("q")));
                    break;
                case "historyDelete":
                    if (r.TryGetProperty("urls", out var urls))
                        foreach (var u in urls.EnumerateArray()) _history.Remove(u.GetString() ?? "");
                    _history.Save();
                    InternalPages.Post(core, HistoryPayload(Str("q")));
                    break;
                case "historyClear":
                    ClearHistoryAndCache();
                    InternalPages.Post(core, HistoryPayload(""));
                    break;
                case "open":
                    OpenFromPage(tab, Str("url"), Str("mode"));
                    break;
                case "download":
                    DownloadAction(Str("id"), Str("action"));
                    break;
                case "downloadsClear":
                    _downloads.ClearFinished();
                    break;
                case "openDownloadsFolder":
                    OpenFolder(EffectiveDownloadFolder());
                    break;
                case "bookmarkAdd":
                {
                    var url = UrlTools.ToUrl(Str("url"), _settings.SearchEngine);
                    if (!UrlTools.IsWeb(url)) break;
                    var title = Str("title").Trim();
                    _bookmarks.Add(title.Length > 0 ? title : UrlTools.SiteKey(UrlTools.Host(url)), url, Str("folder") is { Length: > 0 } f ? f : BookmarkStore.BarId);
                    break;
                }
                case "bookmarkFolder":
                    _bookmarks.AddFolder(Str("title").Trim() is { Length: > 0 } ft ? ft : "Новая папка", Str("parent") is { Length: > 0 } pf ? pf : BookmarkStore.BarId);
                    break;
                case "bookmarkUpdate":
                    if (_bookmarks.FindById(Str("id")) is { } node)
                    {
                        if (Str("title").Trim() is { Length: > 0 } t) node.Title = t;
                        if (!node.IsFolder && UrlTools.ToUrl(Str("url"), _settings.SearchEngine) is var nu && UrlTools.IsWeb(nu)) node.Url = nu;
                        _bookmarks.Notify();
                    }
                    break;
                case "bookmarkDelete":
                    if (_bookmarks.FindById(Str("id")) is { } del) _bookmarks.Remove(del);
                    break;
                case "bookmarkMove":
                    if (_bookmarks.FindById(Str("id")) is { } mv)
                        _bookmarks.Move(mv, Str("folder"), r.TryGetProperty("index", out var idx) && idx.ValueKind == JsonValueKind.Number ? idx.GetInt32() : -1);
                    break;
                case "import":
                    ImportBookmarks(Str("source"));
                    break;
                case "clearData":
                    ClearData(r.TryGetProperty("kinds", out var kinds) ? kinds.EnumerateArray().Select(k => k.GetString() ?? "").ToList() : new());
                    break;
                case "checkUpdates":
                    CheckForUpdates(manual: true);
                    break;
                case "openDataFolder":
                    OpenFolder(AppPaths.Root);
                    break;
                case "openBlocklist":
                    try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppPaths.CustomBlocklist}\"") { UseShellExecute = true }); } catch { }
                    break;
                case "pickStartImage":
                    PickStartImage();
                    break;
                case "pickDownloadFolder":
                    PickDownloadFolder();
                    break;
                case "panelAppAdd":
                    AddPanelApp(Str("name"), Str("url"));
                    break;
                case "panelAppRemove":
                    RemovePanelApp(Str("id"));
                    break;
                case "allowSiteRemove":
                    _settings.AdBlockAllowSites?.RemoveAll(s => s.Equals(Str("site"), StringComparison.OrdinalIgnoreCase));
                    SaveSettings();
                    BroadcastSettings();
                    UpdateShield();
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    object? PayloadFor(string page, JsonElement r) => page switch
    {
        "settings" => SettingsPayload(),
        "history" => HistoryPayload(""),
        "downloads" => DownloadsPayload(),
        "bookmarks" => BookmarksPayload(),
        "reader" => ReaderPayload(r.TryGetProperty("id", out var rid) ? rid.GetString() ?? "" : ""),
        _ => null,
    };

    object SettingsPayload()
    {
        var s = _settings;
        return new
        {
            type = "settings",
            settings = new
            {
                theme = s.Theme, accent = s.Accent ?? "", tabsPosition = s.TabsPosition, leftPanel = s.LeftPanel,
                animations = s.Animations, bookmarksBar = s.BookmarksBar, sitesScheme = s.SitesScheme,
                startBackground = s.StartBackground, startSnow = s.StartSnow, startClock = s.StartClock, onStartup = s.OnStartup,
                searchEngine = s.SearchEngine, searchSuggestions = s.SearchSuggestions, httpsFirst = s.HttpsFirst,
                adBlock = s.AdBlock, trackingPrevention = s.TrackingPrevention, savePasswords = s.SavePasswords, autofill = s.Autofill,
                sleepingTabs = s.SleepingTabs, sleepAfterMinutes = s.SleepAfterMinutes, askDownloadFolder = s.AskDownloadFolder,
                downloadFolder = EffectiveDownloadFolder(), gestures = s.Gestures, gestureTrail = s.GestureTrail,
                openNearCurrent = s.OpenNearCurrent, checkUpdates = s.CheckUpdates,
            },
            themes = Themes.All.Select(t => new { id = t.Id, name = t.Name, isDark = t.IsDark, bg = t.Bg, toolbar = t.Toolbar, surface = t.Surface, accent = t.Accent, text = t.Text, muted = t.Muted, line = t.Line }),
            accents = Themes.Accents.Select(a => new { id = a.Id, name = a.Name, color = Themes.Current.IsDark ? a.Dark : a.Light }),
            engines = UrlTools.Engines.Select(e => new { id = e.Key, name = e.Value.Name }),
            panelApps = s.PanelApps!.Select(a => new { id = a.Id, name = a.Name, url = a.Url }),
            allowSites = s.AdBlockAllowSites ?? new(),
            blockedDomains = _blocker.Count,
            version = Updater.CurrentText,
            dataFolder = AppPaths.Root,
            startImage = _settings.StartImage,
            systemLight = Themes.SystemIsLight(),
            gestures = GestureNames.Select(g => new { dirs = g.Key, name = g.Value }),
        };
    }

    object HistoryPayload(string q)
    {
        var items = q.Trim().Length == 0
            ? _history.All.OrderByDescending(h => h.Last).Take(3000)
            : _history.All.Where(h => TextMatch.Matcher(q)(h.Title + " " + h.Url)).OrderByDescending(h => h.Last).Take(1000);
        return new
        {
            type = "history",
            q,
            total = _history.All.Count,
            items = items.Select(h => new { url = h.Url, title = h.Title, last = h.Last.ToString("o"), visits = h.Visits, fav = BookmarkStore.FavPath(h.Url) }),
        };
    }

    object DownloadsPayload() => new
    {
        type = "downloads",
        folder = EffectiveDownloadFolder(),
        items = _downloads.Items.Select(d => d.ToPageData()),
    };

    object BookmarksPayload() => new
    {
        type = "bookmarks",
        tree = _bookmarks.ToPageData(),
        sources = BookmarkStore.Sources.Select(s => new { id = s.Id, name = s.Name, found = BookmarkStore.FindSourceFile(s.Id) != null }),
        bar = _settings.BookmarksBar,
    };

    void OnStartLinks(string cmd, JsonElement root)
    {
        var wsId = root.TryGetProperty("ws", out var wsProp) ? wsProp.GetString() : null;
        var ws = _workspaces.FirstOrDefault(x => x.Id == wsId);
        if (ws == null) return;
        var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        if (cmd == "removeLink")
        {
            ws.Links.RemoveAll(l => l.Url == url);
        }
        else if (cmd == "addLink")
        {
            url = UrlTools.ToUrl(url, _settings.SearchEngine);
            if (!UrlTools.IsWeb(url)) return;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) title = UrlTools.SiteKey(UrlTools.Host(url));
            ws.Links.Add(new QuickLink { Title = title.Trim(), Url = url });
        }
        else if (cmd == "moveLink")
        {
            int from = root.TryGetProperty("from", out var f) ? f.GetInt32() : -1;
            int to = root.TryGetProperty("to", out var tt) ? tt.GetInt32() : -1;
            if (from < 0 || from >= ws.Links.Count || to < 0 || to >= ws.Links.Count) return;
            var link = ws.Links[from];
            ws.Links.RemoveAt(from);
            ws.Links.Insert(to, link);
        }
        StartPage.Write(_workspaces, _settings);
        QueueSave();
    }

    void OpenFromPage(BrowserTab tab, string url, string mode)
    {
        if (!(UrlTools.IsWeb(url) || UrlTools.InternalPage(url) != null)) return;
        switch (mode)
        {
            case "new": ActivateTab(NewTab(tab.Workspace, url, null, tab.Workspace.Tabs.IndexOf(tab) + 1)); break;
            case "background": NewTab(tab.Workspace, url, null, tab.Workspace.Tabs.IndexOf(tab) + 1); ShowToast("Открыто в фоновой вкладке"); break;
            default: Navigate(tab, url); break;
        }
    }

    static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    // ───────────────────────── настройки ─────────────────────────

    void SetSetting(string key, object value) => ApplySetting(key, JsonSerializer.SerializeToElement(value));

    /// <summary>Применить настройку сразу, без перезапуска, и сохранить.</summary>
    void ApplySetting(string key, JsonElement v)
    {
        bool B() => v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true");
        string S() => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
        int I() => v.ValueKind == JsonValueKind.Number ? v.GetInt32() : int.TryParse(S(), out var n) ? n : 0;
        var s = _settings;
        switch (key)
        {
            case "theme": s.Theme = S(); s.Normalize(); Themes.Apply(s); break;
            case "accent": s.Accent = S().Length > 0 ? S() : null; s.Normalize(); Themes.Apply(s); break;
            case "tabsPosition": s.TabsPosition = S(); s.Normalize(); ApplyLayout(); break;
            case "leftPanel": s.LeftPanel = S(); s.Normalize(); ApplyLayout(); break;
            case "animations": s.Animations = B(); break;
            case "bookmarksBar": s.BookmarksBar = B(); ApplyLayout(); RefreshBookmarkBar(); break;
            case "sitesScheme": s.SitesScheme = S(); s.Normalize(); foreach (var c in AllCores()) ApplyColorScheme(c); break;
            case "startBackground": s.StartBackground = S(); StartPage.Write(_workspaces, s); break;
            case "startSnow": s.StartSnow = B(); StartPage.Write(_workspaces, s); break;
            case "startClock": s.StartClock = B(); StartPage.Write(_workspaces, s); break;
            case "onStartup": s.OnStartup = S(); s.Normalize(); break;
            case "searchEngine":
                if (!UrlTools.Engines.ContainsKey(S())) return;
                s.SearchEngine = S();
                StartPage.Write(_workspaces, s);
                ShowToast($"Поиск по умолчанию: {UrlTools.EngineName(S())}");
                break;
            case "searchSuggestions": s.SearchSuggestions = B(); break;
            case "httpsFirst": s.HttpsFirst = B(); break;
            case "openNearCurrent": s.OpenNearCurrent = B(); break;
            case "checkUpdates": s.CheckUpdates = B(); break;
            case "adBlock":
                s.AdBlock = B();
                UpdateShield();
                ShowToast(s.AdBlock ? "Блокировщик включён" : "Блокировщик выключен");
                break;
            case "trackingPrevention":
                if (S() is not ("none" or "basic" or "balanced" or "strict")) return;
                s.TrackingPrevention = S();
                foreach (var c in AllCores()) try { c.Profile.PreferredTrackingPreventionLevel = s.TrackingLevel(); } catch { }
                break;
            case "savePasswords":
                s.SavePasswords = B();
                foreach (var t in _workspaces.SelectMany(w => w.Tabs).Where(t => !t.IsPrivate))
                    if (t.View?.CoreWebView2 is { } c) c.Settings.IsPasswordAutosaveEnabled = s.SavePasswords;
                break;
            case "autofill":
                s.Autofill = B();
                foreach (var c in AllCores()) c.Settings.IsGeneralAutofillEnabled = s.Autofill;
                break;
            case "sleepingTabs": s.SleepingTabs = B(); break;
            case "sleepAfterMinutes": s.SleepAfterMinutes = Math.Clamp(I(), 1, 240); break;
            case "askDownloadFolder": s.AskDownloadFolder = B(); break;
            case "downloadFolder": s.DownloadFolder = S().Length > 0 && Directory.Exists(S()) ? S() : null; break;
            case "gestures": s.Gestures = B(); if (s.Gestures) StartGestures(); else StopGestures(); break;
            case "gestureTrail": s.GestureTrail = B(); break;
            default: return;
        }
        SaveSettings();
        BroadcastSettings();
    }

    async void ClearData(List<string> kinds)
    {
        if (kinds.Count == 0) return;
        var flags = (CoreWebView2BrowsingDataKinds)0;
        if (kinds.Contains("history"))
        {
            _history.Clear();
            _closed.Clear();
            flags |= CoreWebView2BrowsingDataKinds.BrowsingHistory;
        }
        if (kinds.Contains("cache")) flags |= CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage;
        if (kinds.Contains("cookies")) flags |= CoreWebView2BrowsingDataKinds.AllSite;
        if (kinds.Contains("passwords")) flags |= CoreWebView2BrowsingDataKinds.PasswordAutosave;
        if (kinds.Contains("autofill")) flags |= CoreWebView2BrowsingDataKinds.GeneralAutofill;
        if (kinds.Contains("downloads"))
        {
            _downloads.ClearFinished();
            flags |= CoreWebView2BrowsingDataKinds.DownloadHistory;
        }
        if (flags != 0 && AnyNormalCore() is { } core)
        {
            try { await core.Profile.ClearBrowsingDataAsync(flags); } catch { }
        }
        SaveSession();
        ShowToast("Готово: выбранные данные удалены");
        InternalPages.Broadcast(AllPageCores("settings"), new { type = "cleared" });
    }

    void PickStartImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Картинка для фона стартовой",
            Filter = "Картинки|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            foreach (var old in Directory.GetFiles(AppPaths.Start, "bg-*")) try { File.Delete(old); } catch { }
            var name = $"bg-{DateTime.Now:yyyyMMddHHmmss}{ext}";
            File.Copy(dlg.FileName, Path.Combine(AppPaths.Start, name), true);
            _settings.StartImage = name;
            _settings.StartBackground = "image";
            SaveSettings();
            StartPage.Write(_workspaces, _settings);
            BroadcastSettings();
            InternalPages.Broadcast(AllPageCores(), new { type = "startImage", image = name });
        }
        catch (Exception ex)
        {
            ShowToast("Не получилось взять картинку: " + ex.Message);
        }
    }

    void PickDownloadFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Куда сохранять загрузки", InitialDirectory = EffectiveDownloadFolder() };
        if (dlg.ShowDialog(this) != true) return;
        _settings.DownloadFolder = dlg.FolderName;
        SaveSettings();
        BroadcastSettings();
    }

    void AddPanelApp(string name, string url)
    {
        url = UrlTools.ToUrl(url, _settings.SearchEngine);
        if (!UrlTools.IsWeb(url)) { ShowToast("Нужен адрес сайта, например web.telegram.org"); return; }
        name = name.Trim().Length > 0 ? name.Trim() : UrlTools.SiteKey(UrlTools.Host(url));
        var data = new PanelAppData { Id = "app" + Guid.NewGuid().ToString("N")[..6], Name = name, Glyph = "", Url = url };
        _settings.PanelApps!.Add(data);
        _panelApps.Add(new PanelApp(data));
        SaveSettings();
        BroadcastSettings();
        ShowToast($"«{name}» добавлен в боковую панель");
    }

    void RemovePanelApp(string id)
    {
        var app = _panelApps.FirstOrDefault(a => a.Id == id);
        if (app == null) return;
        if (_panelApps.Count == 1) { ShowToast("В боковой панели должно остаться хотя бы одно приложение"); return; }
        if (_panelApp == app) ClosePanel();
        if (app.View != null)
        {
            PanelHost.Children.Remove(app.View);
            try { app.View.Dispose(); } catch { }
        }
        if (_panelApp == app) _panelApp = null;
        _panelApps.Remove(app);
        _settings.PanelApps!.RemoveAll(a => a.Id == id);
        SaveSettings();
        BroadcastSettings();
    }
}

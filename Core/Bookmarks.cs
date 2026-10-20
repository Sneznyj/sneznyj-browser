using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace SneznyjBrowser;

/// <summary>Закладка или папка закладок.</summary>
public sealed class BookmarkNode : Observable
{
    string _title = "";
    string? _url;
    ImageSource? _icon;
    bool _iconLoaded;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

    public string Title
    {
        get => _title;
        set { if (Set(ref _title, value)) Raise(nameof(Tip)); }
    }

    public string? Url
    {
        get => _url;
        set
        {
            if (!Set(ref _url, value)) return;
            _iconLoaded = false;
            Raise(nameof(Icon));
            Raise(nameof(HasIcon));
            Raise(nameof(Tip));
        }
    }

    /// <summary>У папки есть Children (может быть пустым), у ссылки — null.</summary>
    public List<BookmarkNode>? Children { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;

    [JsonIgnore] public bool IsFolder => Children != null;

    [JsonIgnore]
    public ImageSource? Icon
    {
        get
        {
            if (_iconLoaded || IsFolder) return _icon;
            _iconLoaded = true;
            _icon = Favicons.Load(UrlTools.Host(_url));
            return _icon;
        }
    }

    [JsonIgnore] public bool HasIcon => Icon != null;
    [JsonIgnore] public string Tip => IsFolder ? $"{_title} — папка, {Children!.Count} шт." : $"{_title}\n{_url}";

    public void RefreshIcon()
    {
        _iconLoaded = false;
        Raise(nameof(Icon));
        Raise(nameof(HasIcon));
    }
}

public sealed class BookmarkData
{
    public List<BookmarkNode> Bar { get; set; } = new();
    public List<BookmarkNode> Other { get; set; } = new();
}

/// <summary>Закладки: панель закладок и «Другие закладки», с папками. Хранятся только на этом компьютере.</summary>
public sealed class BookmarkStore
{
    public const string BarId = "bar", OtherId = "other";

    readonly string _path;
    BookmarkData _data;

    /// <summary>Что показано на панели закладок (повторяет корень панели).</summary>
    public ObservableCollection<BookmarkNode> BarItems { get; } = new();
    public event Action? Changed;

    public BookmarkStore(string path)
    {
        _path = path;
        _data = Json.Load<BookmarkData>(path);
        _data.Bar ??= new();
        _data.Other ??= new();
        RefreshBar();
    }

    public List<BookmarkNode> Bar => _data.Bar;
    public List<BookmarkNode> Other => _data.Other;
    public bool IsEmpty => _data.Bar.Count == 0 && _data.Other.Count == 0;

    void RefreshBar()
    {
        BarItems.Clear();
        foreach (var n in _data.Bar) BarItems.Add(n);
    }

    public void Notify()
    {
        RefreshBar();
        Save();
        Changed?.Invoke();
    }

    public void Save()
    {
        try { Json.Save(_path, _data); } catch { }
    }

    static IEnumerable<(BookmarkNode Node, List<BookmarkNode> List)> Walk(List<BookmarkNode> list)
    {
        foreach (var n in list)
        {
            yield return (n, list);
            if (n.Children != null)
                foreach (var x in Walk(n.Children)) yield return x;
        }
    }

    IEnumerable<(BookmarkNode Node, List<BookmarkNode> List)> All() => Walk(_data.Bar).Concat(Walk(_data.Other));

    public IEnumerable<BookmarkNode> AllLinks() => All().Select(x => x.Node).Where(n => !n.IsFolder);

    public BookmarkNode? FindByUrl(string? url) =>
        string.IsNullOrEmpty(url) ? null : All().Select(x => x.Node).FirstOrDefault(n => !n.IsFolder && n.Url == url);

    public BookmarkNode? FindById(string? id) =>
        string.IsNullOrEmpty(id) ? null : All().Select(x => x.Node).FirstOrDefault(n => n.Id == id);

    public List<BookmarkNode>? ListOf(string folderId) => folderId switch
    {
        BarId => _data.Bar,
        OtherId => _data.Other,
        _ => FindById(folderId)?.Children,
    };

    /// <summary>Папка, в которой лежит закладка: bar, other или id папки.</summary>
    public string FolderOf(BookmarkNode node)
    {
        if (_data.Bar.Contains(node)) return BarId;
        if (_data.Other.Contains(node)) return OtherId;
        foreach (var (n, _) in All())
            if (n.Children != null && n.Children.Contains(node)) return n.Id;
        return BarId;
    }

    /// <summary>Все папки для выбора: «Панель закладок», «Другие закладки» и вложенные с отступом.</summary>
    public List<(string Id, string Name)> Folders()
    {
        var result = new List<(string, string)> { (BarId, "Панель закладок") };
        void Add(List<BookmarkNode> list, int depth)
        {
            foreach (var n in list.Where(n => n.IsFolder))
            {
                result.Add((n.Id, new string(' ', depth * 3) + n.Title));
                Add(n.Children!, depth + 1);
            }
        }
        Add(_data.Bar, 1);
        result.Add((OtherId, "Другие закладки"));
        Add(_data.Other, 1);
        return result;
    }

    public BookmarkNode Add(string title, string url, string folderId = BarId)
    {
        var node = new BookmarkNode { Title = title, Url = url };
        (ListOf(folderId) ?? _data.Bar).Add(node);
        Notify();
        return node;
    }

    public BookmarkNode AddFolder(string title, string parentId = BarId)
    {
        var node = new BookmarkNode { Title = title, Children = new() };
        (ListOf(parentId) ?? _data.Bar).Add(node);
        Notify();
        return node;
    }

    public void Remove(BookmarkNode node)
    {
        foreach (var (n, list) in All().ToList())
            if (n == node) { list.Remove(n); break; }
        Notify();
    }

    public void Move(BookmarkNode node, string folderId, int index = -1)
    {
        var target = ListOf(folderId);
        if (target == null || (node.Children != null && (node.Id == folderId || Walk(node.Children).Any(x => x.Node.Id == folderId))))
            return;
        foreach (var (n, list) in All().ToList())
            if (n == node) { list.Remove(n); break; }
        if (index < 0 || index > target.Count) target.Add(node); else target.Insert(index, node);
        Notify();
    }

    // ───────────── перенос из Яндекс Браузера, Chrome и Edge ─────────────

    public static readonly (string Id, string Name, string Folder)[] Sources =
    {
        ("yandex", "Яндекс Браузер", @"Yandex\YandexBrowser\User Data"),
        ("chrome", "Google Chrome", @"Google\Chrome\User Data"),
        ("edge", "Microsoft Edge", @"Microsoft\Edge\User Data"),
    };

    /// <summary>Файл закладок браузера: берём профиль, где закладки менялись последними.</summary>
    public static string? FindSourceFile(string sourceId)
    {
        var src = Sources.FirstOrDefault(s => s.Id == sourceId);
        if (src.Id == null) return null;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), src.Folder);
        try
        {
            if (!Directory.Exists(root)) return null;
            return Directory.GetDirectories(root)
                .Select(d => Path.Combine(d, "Bookmarks"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Переносит закладки из файла Bookmarks (формат Chromium). Возвращает, сколько ссылок добавлено.</summary>
    public int ImportChromium(string file, string sourceName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var roots = doc.RootElement.GetProperty("roots");
        var existing = new HashSet<string>(AllLinks().Select(n => n.Url ?? ""));
        int added = 0;

        List<BookmarkNode> Convert(JsonElement folder)
        {
            var list = new List<BookmarkNode>();
            if (!folder.TryGetProperty("children", out var children)) return list;
            foreach (var c in children.EnumerateArray())
            {
                var type = c.TryGetProperty("type", out var t) ? t.GetString() : "";
                var name = c.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                if (type == "url")
                {
                    var url = c.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    if (!UrlTools.IsWeb(url) || !existing.Add(url)) continue;
                    list.Add(new BookmarkNode { Title = string.IsNullOrWhiteSpace(name) ? UrlTools.SiteKey(UrlTools.Host(url)) : name, Url = url });
                    added++;
                }
                else if (type == "folder")
                {
                    var sub = Convert(c);
                    if (sub.Count > 0) list.Add(new BookmarkNode { Title = name, Children = sub });
                }
            }
            return list;
        }

        var bar = roots.TryGetProperty("bookmark_bar", out var b) ? Convert(b) : new();
        var other = roots.TryGetProperty("other", out var o) ? Convert(o) : new();
        if (roots.TryGetProperty("synced", out var s)) other.AddRange(Convert(s));
        var collections = roots.TryGetProperty("collections", out var col) ? Convert(col) : new();
        if (collections.Count > 0) other.Add(new BookmarkNode { Title = "Коллекции", Children = collections });

        if (_data.Bar.Count == 0) _data.Bar.AddRange(bar);
        else if (bar.Count > 0) _data.Bar.Add(new BookmarkNode { Title = $"Из {sourceName}", Children = bar });
        if (other.Count > 0)
        {
            if (_data.Other.Count == 0) _data.Other.AddRange(other);
            else _data.Other.Add(new BookmarkNode { Title = $"Другие из {sourceName}", Children = other });
        }
        Notify();
        return added;
    }

    /// <summary>Закладки в виде, удобном для страницы sneznyj://bookmarks.</summary>
    public object ToPageData()
    {
        object Node(BookmarkNode n) => n.IsFolder
            ? new { id = n.Id, title = n.Title, children = n.Children!.Select(Node).ToList() }
            : new { id = n.Id, title = n.Title, url = n.Url, fav = FavPath(n.Url) };
        return new
        {
            bar = _data.Bar.Select(Node).ToList(),
            other = _data.Other.Select(Node).ToList(),
        };
    }

    public static string? FavPath(string? url)
    {
        var host = UrlTools.Host(url);
        return host.Length > 0 && File.Exists(Favicons.PathFor(host)) ? "fav/" + Favicons.FileName(host) : null;
    }
}

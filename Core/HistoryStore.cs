namespace SneznyjBrowser;

public sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Last { get; set; }
    public int Visits { get; set; }
}

/// <summary>История посещений — только локально, приватные вкладки сюда не пишутся.</summary>
public sealed class HistoryStore
{
    const int Max = 5000;
    readonly string _path;
    readonly List<HistoryEntry> _items;
    readonly Dictionary<string, HistoryEntry> _byUrl;
    bool _dirty;

    public HistoryStore(string path)
    {
        _path = path;
        _items = Json.Load<List<HistoryEntry>>(path);
        _byUrl = new Dictionary<string, HistoryEntry>();
        foreach (var e in _items) _byUrl.TryAdd(e.Url, e);
    }

    public void Add(string url, string title)
    {
        if (!UrlTools.IsWeb(url)) return;
        if (_byUrl.TryGetValue(url, out var e))
        {
            e.Visits++;
            e.Last = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(title)) e.Title = title;
        }
        else
        {
            e = new HistoryEntry { Url = url, Title = title ?? "", Last = DateTime.Now, Visits = 1 };
            _items.Add(e);
            _byUrl[url] = e;
            if (_items.Count > Max)
            {
                foreach (var old in _items.OrderBy(i => i.Last).Take(500).ToList())
                {
                    _items.Remove(old);
                    _byUrl.Remove(old.Url);
                }
            }
        }
        _dirty = true;
    }

    public void UpdateTitle(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        if (_byUrl.TryGetValue(url, out var e) && e.Title != title)
        {
            e.Title = title;
            _dirty = true;
        }
    }

    public IEnumerable<HistoryEntry> Search(Func<string, bool> match, int count) =>
        _items.Where(e => match(e.Title + " " + e.Url))
              .OrderByDescending(Score)
              .Take(count);

    public IEnumerable<HistoryEntry> Recent(int count) =>
        _items.OrderByDescending(e => e.Last).Take(count);

    static double Score(HistoryEntry e) =>
        Math.Log(1 + e.Visits) * 3 + 10.0 / (1 + (DateTime.Now - e.Last).TotalDays);

    public IReadOnlyList<HistoryEntry> All => _items;

    public void Remove(string url)
    {
        if (!_byUrl.Remove(url, out var e)) return;
        _items.Remove(e);
        _dirty = true;
    }

    /// <summary>Сайты, которые чаще всего открывают (для подсказки адреса прямо в строке).</summary>
    public IEnumerable<HistoryEntry> Ranked() => _items.OrderByDescending(Score);

    public void Clear()
    {
        _items.Clear();
        _byUrl.Clear();
        _dirty = true;
        Save();
    }

    public void Save()
    {
        if (!_dirty) return;
        try { Json.Save(_path, _items); _dirty = false; } catch { }
    }
}

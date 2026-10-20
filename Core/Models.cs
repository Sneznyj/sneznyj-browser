using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace SneznyjBrowser;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BrowserTab : Observable
{
    string _title = "Новая вкладка";
    string _url = "";
    ImageSource? _favicon;
    bool _isLoading, _isAudible, _isMuted, _isActive, _isPinned;
    int _blocked;
    TabGroup? _group;

    public string Title { get => _title; set { if (Set(ref _title, value)) Raise(nameof(Tip)); } }

    public string Url
    {
        get => _url;
        set { if (Set(ref _url, value)) { Raise(nameof(FallbackGlyph)); Raise(nameof(Tip)); } }
    }

    /// <summary>Подсказка при наведении на вкладку: название и адрес.</summary>
    public string Tip => UrlTools.IsNewTab(_url) ? _title : $"{_title}\n{UrlTools.Display(_url)}";

    /// <summary>Значок, пока у сайта нет своего: шестерёнка для настроек, часы для истории и т. п.</summary>
    public string FallbackGlyph => IsPrivate ? "\uED1A" : UrlTools.InternalGlyph(_url) ?? "\uE774";

    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }

    public TabGroup? Group
    {
        get => _group;
        set { if (Set(ref _group, value)) Raise(nameof(InGroup)); }
    }

    public bool InGroup => _group != null;

    /// <summary>На странице есть статья — можно открыть режим чтения.</summary>
    public bool CanRead { get; set; }

    public ImageSource? Favicon
    {
        get => _favicon;
        set { if (Set(ref _favicon, value)) Raise(nameof(HasFavicon)); }
    }

    public bool HasFavicon => _favicon != null;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }
    public int Blocked { get => _blocked; set => Set(ref _blocked, value); }

    public bool IsAudible
    {
        get => _isAudible;
        set { if (Set(ref _isAudible, value)) Raise(nameof(ShowAudio)); }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set { if (Set(ref _isMuted, value)) Raise(nameof(ShowAudio)); }
    }

    public bool ShowAudio => _isAudible || _isMuted;
    public bool IsPrivate { get; init; }
    public Workspace Workspace { get; set; } = null!;
    public WebView2? View { get; set; }
    public string? PendingUrl { get; set; }
    /// <summary>Адрес http://, который сейчас пробуем открыть по https.</summary>
    public string? HttpsUpgradeFrom { get; set; }
    public DateTime HiddenSince { get; set; } = DateTime.Now;
}

/// <summary>Группа вкладок: название, цвет, свёрнута или нет.</summary>
public sealed class TabGroup : Observable
{
    string _name = "", _color = "blue";
    bool _collapsed;
    int _count;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get => _name; set { if (Set(ref _name, value)) Raise(nameof(Label)); } }

    public string Color
    {
        get => _color;
        set { if (Set(ref _color, value)) Raise(nameof(Brush)); }
    }

    public bool Collapsed { get => _collapsed; set { if (Set(ref _collapsed, value)) Raise(nameof(Label)); } }
    public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(Label)); } }
    public Brush Brush => Themes.GroupBrush(_color);

    /// <summary>Подпись на «ярлычке» группы: название, у свёрнутой ещё и число вкладок.</summary>
    public string Label => _collapsed ? (_name.Length > 0 ? $"{_name} · {_count}" : $"{_count}") : _name;

    public void RefreshBrush() => Raise(nameof(Brush));

    public static TabGroup FromData(TabGroupData d) => new()
    {
        Id = string.IsNullOrEmpty(d.Id) ? Guid.NewGuid().ToString("N")[..8] : d.Id,
        Name = d.Name ?? "", Color = d.Color ?? "blue", Collapsed = d.Collapsed,
    };

    public TabGroupData ToData() => new() { Id = Id, Name = Name, Color = Color, Collapsed = Collapsed };
}

/// <summary>Последний элемент полосы вкладок — кнопка «+».</summary>
public sealed class StripEnd
{
    public static readonly StripEnd Instance = new();
}

public sealed class QuickLink
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class Workspace : Observable
{
    string _name = "";
    string _glyph = "";
    string _accent = "#7CC4FF";
    bool _isCurrent;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Glyph { get => _glyph; set => Set(ref _glyph, value); }

    public string Accent
    {
        get => _accent;
        set { if (Set(ref _accent, value)) Raise(nameof(AccentBrush)); }
    }

    /// <summary>Цвет значка пространства: пастельный на тёмной теме, насыщеннее на светлой — чтобы было видно.</summary>
    public Brush AccentBrush
    {
        get
        {
            try
            {
                var c = (System.Windows.Media.Color)ColorConverter.ConvertFromString(_accent);
                if (!Themes.Current.IsDark) c = System.Windows.Media.Color.FromRgb((byte)(c.R * 0.62), (byte)(c.G * 0.62), (byte)(c.B * 0.62));
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
            catch { return Brushes.LightSkyBlue; }
        }
    }

    public void RefreshBrush() => Raise(nameof(AccentBrush));

    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    public ObservableCollection<BrowserTab> Tabs { get; } = new();
    /// <summary>То, что видно в полосе вкладок: ярлычки групп и вкладки (без вкладок свёрнутых групп), в конце «+».</summary>
    public ObservableCollection<object> Strip { get; } = new();
    public List<TabGroup> Groups { get; } = new();
    public List<QuickLink> Links { get; set; } = new();
    public BrowserTab? ActiveTab { get; set; }

    int _count;
    public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(CountText)); } }
    public string CountText => _count == 0 ? "" : _count.ToString();

    /// <summary>Пересобирает Strip по Tabs, не пересоздавая элементы: вкладки не мигают и не теряют наведение.</summary>
    public void RefreshStrip()
    {
        var target = new List<object>(Tabs.Count + Groups.Count + 1);
        TabGroup? cur = null;
        foreach (var g in Groups) g.Count = 0;
        foreach (var t in Tabs)
        {
            if (t.Group != null) t.Group.Count++;
            if (t.Group != cur)
            {
                cur = t.Group;
                if (cur != null) target.Add(cur);
            }
            if (cur == null || !cur.Collapsed) target.Add(t);
        }
        target.Add(StripEnd.Instance);
        Groups.RemoveAll(g => g.Count == 0);
        Count = Tabs.Count;

        for (int i = 0; i < target.Count; i++)
        {
            if (i < Strip.Count && ReferenceEquals(Strip[i], target[i])) continue;
            int found = -1;
            for (int j = i + 1; j < Strip.Count; j++)
                if (ReferenceEquals(Strip[j], target[i])) { found = j; break; }
            if (found >= 0) Strip.Move(found, i);
            else Strip.Insert(i, target[i]);
        }
        while (Strip.Count > target.Count) Strip.RemoveAt(Strip.Count - 1);
    }

    public static Workspace FromData(WorkspaceData d)
    {
        var ws = new Workspace
        {
            Id = string.IsNullOrWhiteSpace(d.Id) ? Guid.NewGuid().ToString("N")[..8] : d.Id,
            Name = d.Name,
            Glyph = string.IsNullOrEmpty(d.Glyph) ? "" : d.Glyph,
            Accent = string.IsNullOrEmpty(d.Accent) ? "#7CC4FF" : d.Accent,
            Links = d.Links ?? new(),
        };
        foreach (var g in d.Groups ?? new()) ws.Groups.Add(TabGroup.FromData(g));
        var loaded = new List<BrowserTab>();
        foreach (var t in d.Tabs ?? new())
        {
            var url = t.Url ?? "";
            loaded.Add(new BrowserTab
            {
                Workspace = ws,
                Url = url,
                Title = string.IsNullOrWhiteSpace(t.Title) ? "Новая вкладка" : t.Title,
                Favicon = UrlTools.IsWeb(url) ? Favicons.Load(UrlTools.Host(url)) : null,
                IsPinned = t.Pinned,
                Group = t.Pinned ? null : ws.Groups.FirstOrDefault(g => g.Id == t.Group),
            });
        }
        // Закреплённые всегда в начале, вкладки одной группы — рядом.
        var ordered = loaded.Where(t => t.IsPinned).Concat(loaded.Where(t => !t.IsPinned)).ToList();
        var added = new HashSet<BrowserTab>();
        foreach (var t in ordered)
        {
            if (added.Contains(t)) continue;
            foreach (var x in t.Group == null ? new List<BrowserTab> { t } : ordered.Where(x => x.Group == t.Group).ToList())
            {
                added.Add(x);
                ws.Tabs.Add(x);
            }
        }
        if (d.ActiveTab >= 0 && d.ActiveTab < loaded.Count) ws.ActiveTab = loaded[d.ActiveTab];
        ws.RefreshStrip();
        return ws;
    }

    public WorkspaceData ToData()
    {
        var saved = Tabs.Where(t => !t.IsPrivate).ToList();
        return new WorkspaceData
        {
            Id = Id,
            Name = Name,
            Glyph = Glyph,
            Accent = Accent,
            Links = Links,
            ActiveTab = ActiveTab != null ? Math.Max(0, saved.IndexOf(ActiveTab)) : 0,
            Tabs = saved.Select(t => new TabData
            {
                Url = UrlTools.IsNewTab(t.Url) ? "" : t.Url, Title = t.Title, Pinned = t.IsPinned, Group = t.Group?.Id,
            }).ToList(),
            Groups = Groups.Where(g => saved.Any(t => t.Group == g)).Select(g => g.ToData()).ToList(),
        };
    }
}

public sealed class PanelApp : Observable
{
    bool _isCurrent;

    public PanelApp(PanelAppData d)
    {
        Id = d.Id;
        Name = d.Name;
        Glyph = d.Glyph;
        Url = d.Url;
    }

    public string Id { get; }
    public string Name { get; }
    public string Glyph { get; }
    public string Url { get; }
    public WebView2? View { get; set; }
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }

    bool _isShown;
    /// <summary>Открыто в боковой панели прямо сейчас (для подсветки значка в левой панели).</summary>
    public bool IsShown { get => _isShown; set => Set(ref _isShown, value); }
}

public sealed class PaletteItem
{
    public string Glyph { get; init; } = "";
    public ImageSource? Icon { get; init; }
    public bool HasIcon => Icon != null;
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public bool HasSubtitle => Subtitle.Length > 0;
    public string Kind { get; init; } = "";
    public string Hint { get; init; } = "";
    public bool HasHint => Hint.Length > 0;
    public string Keywords { get; init; } = "";
    /// <summary>Показывать в палитре сразу, пока ничего не введено.</summary>
    public bool Featured { get; init; }
    public Action Run { get; init; } = () => { };
}

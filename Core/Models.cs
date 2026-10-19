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
    bool _isLoading, _isAudible, _isMuted;
    int _blocked;

    public string Title { get => _title; set => Set(ref _title, value); }
    public string Url { get => _url; set => Set(ref _url, value); }

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
    public DateTime HiddenSince { get; set; } = DateTime.Now;
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

    public Brush AccentBrush
    {
        get
        {
            try { return new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(_accent)); }
            catch { return Brushes.LightSkyBlue; }
        }
    }

    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    public ObservableCollection<BrowserTab> Tabs { get; } = new();
    public List<QuickLink> Links { get; set; } = new();
    public BrowserTab? ActiveTab { get; set; }

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
        foreach (var t in d.Tabs ?? new())
        {
            var url = t.Url ?? "";
            ws.Tabs.Add(new BrowserTab
            {
                Workspace = ws,
                Url = url,
                Title = string.IsNullOrWhiteSpace(t.Title) ? "Новая вкладка" : t.Title,
                Favicon = url.Length == 0 ? null : Favicons.Load(UrlTools.Host(url)),
            });
        }
        if (d.ActiveTab >= 0 && d.ActiveTab < ws.Tabs.Count) ws.ActiveTab = ws.Tabs[d.ActiveTab];
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
            Tabs = saved.Select(t => new TabData { Url = UrlTools.IsStart(t.Url) ? "" : t.Url, Title = t.Title }).ToList(),
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

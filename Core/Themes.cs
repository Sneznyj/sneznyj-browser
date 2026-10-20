using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SneznyjBrowser;

/// <summary>Цвета одной темы. Все кисти интерфейса берутся отсюда через DynamicResource.</summary>
public sealed class Palette
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsDark { get; init; } = true;
    public string Bg { get; init; } = "";            // полоса вкладок, левая панель
    public string Toolbar { get; init; } = "";       // строка адреса и активная вкладка
    public string PageBg { get; init; } = "";        // фон под страницей, пока она грузится
    public string Popup { get; init; } = "";         // меню, палитра, окна
    public string PopupBorder { get; init; } = "";
    public string Surface { get; init; } = "";       // адресная строка, поля ввода
    public string Hover { get; init; } = "";
    public string Selected { get; init; } = "";
    public string SelectedBorder { get; init; } = "";
    public string Line { get; init; } = "";
    public string Text { get; init; } = "";
    public string SoftText { get; init; } = "";
    public string Muted { get; init; } = "";
    public string Accent { get; init; } = "";
    public string AccentText { get; init; } = "";
    public string Good { get; init; } = "";
    public string Warn { get; init; } = "";
    public string Private { get; init; } = "";
    public string PrivateBg { get; init; } = "";
    public string Danger { get; init; } = "";
    public string ScrollThumb { get; init; } = "";
    public string Notice { get; init; } = "";
    public string NoticeBorder { get; init; } = "";
    /// <summary>Свечение и фон стартовой страницы.</summary>
    public string Glow { get; init; } = "";
    /// <summary>Небольшие картинки-превью в настройках: фон, панель, акцент.</summary>
    public string[] Preview => new[] { Bg, Toolbar, Surface, Accent, Text };
}

public sealed record AccentChoice(string Id, string Name, string Dark, string Light);

public sealed record GroupColor(string Id, string Name, string Dark, string Light);

public static class Themes
{
    public static readonly Palette[] All =
    {
        new()
        {
            Id = "snow", Name = "Снежная ночь", IsDark = true,
            Bg = "#0B1016", Toolbar = "#141B24", PageBg = "#0B0F14", Popup = "#18212C", PopupBorder = "#2A3747",
            Surface = "#1E2834", Hover = "#1C2633", Selected = "#22344A", SelectedBorder = "#33506E", Line = "#243040",
            Text = "#E6EDF5", SoftText = "#C3CEDB", Muted = "#8696A9", Accent = "#7CC4FF", AccentText = "#0E1319",
            Good = "#6EE7B7", Warn = "#FBBF24", Private = "#C4A3FF", PrivateBg = "#2A2140", Danger = "#E5484D",
            ScrollThumb = "#2E3B4C", Notice = "#16273A", NoticeBorder = "#2F5578", Glow = "#1A2C43",
        },
        new()
        {
            Id = "graphite", Name = "Графит", IsDark = true,
            Bg = "#141414", Toolbar = "#202020", PageBg = "#111111", Popup = "#262626", PopupBorder = "#3A3A3A",
            Surface = "#2B2B2B", Hover = "#2A2A2A", Selected = "#343A44", SelectedBorder = "#4A5566", Line = "#313131",
            Text = "#EDEDED", SoftText = "#D0D0D0", Muted = "#9A9A9A", Accent = "#8AB4F8", AccentText = "#111111",
            Good = "#81C995", Warn = "#FDD663", Private = "#C58AF9", PrivateBg = "#2E2438", Danger = "#F28B82",
            ScrollThumb = "#444444", Notice = "#232B36", NoticeBorder = "#3E4E63", Glow = "#2A2A2A",
        },
        new()
        {
            Id = "midnight", Name = "Полночь", IsDark = true,
            Bg = "#0D0C1A", Toolbar = "#17162A", PageBg = "#0B0A16", Popup = "#1C1B33", PopupBorder = "#2F2D52",
            Surface = "#232142", Hover = "#201E3A", Selected = "#2D2A5A", SelectedBorder = "#4A4590", Line = "#28264A",
            Text = "#ECEBFF", SoftText = "#CFCCF2", Muted = "#9591C2", Accent = "#A78BFA", AccentText = "#0D0C1A",
            Good = "#6EE7B7", Warn = "#FBBF24", Private = "#F0ABFC", PrivateBg = "#35204A", Danger = "#F87171",
            ScrollThumb = "#37346A", Notice = "#1F1C40", NoticeBorder = "#433D85", Glow = "#2A2160",
        },
        new()
        {
            Id = "forest", Name = "Лес", IsDark = true,
            Bg = "#0B1310", Toolbar = "#121F19", PageBg = "#0A110E", Popup = "#16251E", PopupBorder = "#26402F",
            Surface = "#1A2D23", Hover = "#182A20", Selected = "#1F3B2D", SelectedBorder = "#2F6048", Line = "#21362B",
            Text = "#E6F2EC", SoftText = "#C6DBD0", Muted = "#86A395", Accent = "#6EE7B7", AccentText = "#0B1310",
            Good = "#6EE7B7", Warn = "#FBBF24", Private = "#C4A3FF", PrivateBg = "#2A2140", Danger = "#F87171",
            ScrollThumb = "#2C4638", Notice = "#15301F", NoticeBorder = "#2C6142", Glow = "#173A2A",
        },
        new()
        {
            Id = "ember", Name = "Камин", IsDark = true,
            Bg = "#15100D", Toolbar = "#211915", PageBg = "#120E0B", Popup = "#271E19", PopupBorder = "#3D2F26",
            Surface = "#2E231D", Hover = "#2A201B", Selected = "#3D2B21", SelectedBorder = "#6A4630", Line = "#33271F",
            Text = "#F5ECE6", SoftText = "#E0D2C8", Muted = "#A8968A", Accent = "#FDBA74", AccentText = "#15100D",
            Good = "#86EFAC", Warn = "#FDE68A", Private = "#D8B4FE", PrivateBg = "#33213F", Danger = "#F87171",
            ScrollThumb = "#4A3A30", Notice = "#2E2018", NoticeBorder = "#6A4630", Glow = "#3A2418",
        },
        new()
        {
            Id = "light", Name = "Светлая", IsDark = false,
            Bg = "#E8ECF1", Toolbar = "#FFFFFF", PageBg = "#FFFFFF", Popup = "#FFFFFF", PopupBorder = "#D3DAE3",
            Surface = "#EEF1F5", Hover = "#DCE2EA", Selected = "#D4E4F7", SelectedBorder = "#A9C8EE", Line = "#D5DCE4",
            Text = "#16202B", SoftText = "#2F3B48", Muted = "#66737F", Accent = "#2E7CD6", AccentText = "#FFFFFF",
            Good = "#178A61", Warn = "#B7700B", Private = "#7C4DDB", PrivateBg = "#ECE5FB", Danger = "#D93B3B",
            ScrollThumb = "#C2CBD5", Notice = "#EAF3FD", NoticeBorder = "#B9D3F0", Glow = "#DCEBFB",
        },
        new()
        {
            Id = "sand", Name = "Песок", IsDark = false,
            Bg = "#EEE7DE", Toolbar = "#FBF8F4", PageBg = "#FFFFFF", Popup = "#FFFDFA", PopupBorder = "#DCD1C3",
            Surface = "#F2EDE6", Hover = "#E5DCD0", Selected = "#F1DEC6", SelectedBorder = "#DDB993", Line = "#DED4C7",
            Text = "#2A2420", SoftText = "#463C35", Muted = "#7A6E63", Accent = "#C0651B", AccentText = "#FFFFFF",
            Good = "#2F8A57", Warn = "#A86A07", Private = "#7C4DDB", PrivateBg = "#EFE6F7", Danger = "#C9392F",
            ScrollThumb = "#D2C6B7", Notice = "#FBEFE2", NoticeBorder = "#E6C9A8", Glow = "#F6E3CC",
        },
        new()
        {
            Id = "mint", Name = "Мята", IsDark = false,
            Bg = "#E1EFE8", Toolbar = "#F8FCFA", PageBg = "#FFFFFF", Popup = "#FFFFFF", PopupBorder = "#C9DFD3",
            Surface = "#EAF4EF", Hover = "#D3E7DC", Selected = "#CBEBDB", SelectedBorder = "#93D0B2", Line = "#CFE2D8",
            Text = "#14251D", SoftText = "#2C4238", Muted = "#5E776B", Accent = "#12896A", AccentText = "#FFFFFF",
            Good = "#12896A", Warn = "#A86A07", Private = "#7C4DDB", PrivateBg = "#EDE6F8", Danger = "#C9392F",
            ScrollThumb = "#BBD3C6", Notice = "#E3F5EC", NoticeBorder = "#A8D8BF", Glow = "#CDEEDD",
        },
        new()
        {
            Id = "lavender", Name = "Лаванда", IsDark = false,
            Bg = "#EBE7F5", Toolbar = "#FCFBFF", PageBg = "#FFFFFF", Popup = "#FFFFFF", PopupBorder = "#D6CFEA",
            Surface = "#F0EDF8", Hover = "#DFD9F0", Selected = "#DDD3F8", SelectedBorder = "#B7A5EE", Line = "#DDD7EC",
            Text = "#1F1A2E", SoftText = "#3B3450", Muted = "#6D6587", Accent = "#7350D6", AccentText = "#FFFFFF",
            Good = "#178A61", Warn = "#A86A07", Private = "#B4387F", PrivateBg = "#F7E4F0", Danger = "#C9392F",
            ScrollThumb = "#CCC4E2", Notice = "#EEEAFB", NoticeBorder = "#C7BAF1", Glow = "#E2DAFB",
        },
    };

    public static readonly AccentChoice[] Accents =
    {
        new("blue", "Синий", "#7CC4FF", "#2E7CD6"),
        new("purple", "Фиолетовый", "#C4B5FD", "#7350D6"),
        new("pink", "Розовый", "#F9A8D4", "#C2417F"),
        new("red", "Красный", "#FCA5A5", "#D23B3B"),
        new("orange", "Оранжевый", "#FDBA74", "#C0651B"),
        new("yellow", "Жёлтый", "#FDE68A", "#9C7406"),
        new("green", "Зелёный", "#86EFAC", "#1E8E4E"),
        new("teal", "Бирюзовый", "#5EEAD4", "#0F8B83"),
    };

    public static readonly GroupColor[] GroupColors =
    {
        new("blue", "Синий", "#8AB4F8", "#1A73E8"),
        new("red", "Красный", "#F28B82", "#D93025"),
        new("yellow", "Жёлтый", "#FDD663", "#E8A200"),
        new("green", "Зелёный", "#81C995", "#1E8E3E"),
        new("pink", "Розовый", "#FF8BCB", "#D01884"),
        new("purple", "Фиолетовый", "#C58AF9", "#9334E6"),
        new("cyan", "Голубой", "#78D9EC", "#007B83"),
        new("orange", "Оранжевый", "#FCAD70", "#E8710A"),
        new("grey", "Серый", "#BDC1C6", "#5F6368"),
    };

    /// <summary>Тема, которая сейчас на экране (уже с учётом системной и своего акцента).</summary>
    public static Palette Current { get; private set; } = All[0];

    /// <summary>Вызывается после смены темы — чтобы перекрасить то, что рисуется не через кисти (окно, страницы).</summary>
    public static event Action? Changed;

    static bool _listening;
    static string _themeId = "snow", _accentId = "";

    public static bool SystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    public static Palette Resolve(string themeId, string accentId)
    {
        var id = themeId == "system" ? (SystemIsLight() ? "light" : "snow") : themeId;
        var p = All.FirstOrDefault(t => t.Id == id) ?? All[0];
        var a = Accents.FirstOrDefault(x => x.Id == accentId);
        if (a == null) return p;
        return new Palette
        {
            Id = p.Id, Name = p.Name, IsDark = p.IsDark, Bg = p.Bg, Toolbar = p.Toolbar, PageBg = p.PageBg,
            Popup = p.Popup, PopupBorder = p.PopupBorder, Surface = p.Surface, Hover = p.Hover, Selected = p.Selected,
            SelectedBorder = p.SelectedBorder, Line = p.Line, Text = p.Text, SoftText = p.SoftText, Muted = p.Muted,
            Accent = p.IsDark ? a.Dark : a.Light, AccentText = p.IsDark ? p.Bg : "#FFFFFF",
            Good = p.Good, Warn = p.Warn, Private = p.Private, PrivateBg = p.PrivateBg, Danger = p.Danger,
            ScrollThumb = p.ScrollThumb, Notice = p.Notice, NoticeBorder = p.NoticeBorder, Glow = p.Glow,
        };
    }

    public static void Apply(SettingsData settings)
    {
        _themeId = settings.Theme;
        _accentId = settings.Accent ?? "";
        var p = Resolve(_themeId, _accentId);
        Current = p;
        var r = Application.Current.Resources;
        void B(string key, string hex) => r[key] = Brush(hex);
        B("BgBrush", p.Bg);
        B("ToolbarBrush", p.Toolbar);
        B("PageBgBrush", p.PageBg);
        B("PopupBrush", p.Popup);
        B("PopupBorderBrush", p.PopupBorder);
        B("SurfaceBrush", p.Surface);
        B("HoverBrush", p.Hover);
        B("SelectedBrush", p.Selected);
        B("SelectedBorderBrush", p.SelectedBorder);
        B("LineBrush", p.Line);
        B("TextBrush", p.Text);
        B("SoftTextBrush", p.SoftText);
        B("MutedBrush", p.Muted);
        B("AccentBrush", p.Accent);
        B("AccentTextBrush", p.AccentText);
        B("GoodBrush", p.Good);
        B("WarnBrush", p.Warn);
        B("PrivateBrush", p.Private);
        B("PrivateBgBrush", p.PrivateBg);
        B("DangerBrush", p.Danger);
        B("ScrollThumbBrush", p.ScrollThumb);
        B("NoticeBrush", p.Notice);
        B("NoticeBorderBrush", p.NoticeBorder);
        r["AccentSoftBrush"] = Brush(p.Accent, 0.16);
        B("GroupTextBrush", p.IsDark ? "#15181C" : "#FFFFFF");
        r["ShadowOpacity"] = p.IsDark ? 0.55 : 0.18;
        WriteCss();

        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category != UserPreferenceCategory.General || _themeId != "system") return;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    var next = Resolve(_themeId, _accentId);
                    if (next.Id == Current.Id) return;
                    Apply(new SettingsData { Theme = _themeId, Accent = _accentId });
                });
            };
        }
        Changed?.Invoke();
    }

    public static SolidColorBrush Brush(string hex, double opacity = 1)
    {
        var b = new SolidColorBrush(ToColor(hex)) { Opacity = opacity };
        b.Freeze();
        return b;
    }

    public static Color ToColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
    }

    public static System.Drawing.Color ToDrawing(string hex)
    {
        var c = ToColor(hex);
        return System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);
    }

    public static Brush GroupBrush(string colorId)
    {
        var g = GroupColors.FirstOrDefault(c => c.Id == colorId) ?? GroupColors[0];
        return Brush(Current.IsDark ? g.Dark : g.Light);
    }

    public static string GroupHex(string colorId)
    {
        var g = GroupColors.FirstOrDefault(c => c.Id == colorId) ?? GroupColors[0];
        return Current.IsDark ? g.Dark : g.Light;
    }

    /// <summary>Переменные CSS для стартовой и внутренних страниц (настройки, история, загрузки, закладки).</summary>
    public static Dictionary<string, string> CssVars(Palette? palette = null)
    {
        var p = palette ?? Current;
        return new()
        {
            ["--bg"] = p.Bg, ["--toolbar"] = p.Toolbar, ["--page"] = p.PageBg, ["--popup"] = p.Popup,
            ["--popup-border"] = p.PopupBorder, ["--surface"] = p.Surface, ["--hover"] = p.Hover,
            ["--selected"] = p.Selected, ["--selected-border"] = p.SelectedBorder, ["--line"] = p.Line,
            ["--text"] = p.Text, ["--soft"] = p.SoftText, ["--muted"] = p.Muted, ["--accent"] = p.Accent,
            ["--accent-text"] = p.AccentText, ["--good"] = p.Good, ["--warn"] = p.Warn, ["--private"] = p.Private,
            ["--danger"] = p.Danger, ["--glow"] = p.Glow, ["--thumb"] = p.ScrollThumb,
        };
    }

    public static void WriteCss()
    {
        var p = Current;
        var sb = new StringBuilder();
        sb.Append(":root{color-scheme:").Append(p.IsDark ? "dark" : "light").Append(';');
        foreach (var (k, v) in CssVars(p)) sb.Append(k).Append(':').Append(v).Append(';');
        sb.Append("}\n");
        try
        {
            Directory.CreateDirectory(AppPaths.Start);
            File.WriteAllText(Path.Combine(AppPaths.Start, "theme.css"), sb.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }
}

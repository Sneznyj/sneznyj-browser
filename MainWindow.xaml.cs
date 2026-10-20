using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SneznyjBrowser;

/// <summary>
/// Главное окно. Код разложен по частям:
/// здесь — запуск, пространства, вкладки и движок страниц;
/// Layout — левая панель и темы; Tabs — полоса вкладок, группы, перетаскивание;
/// Menu — главное меню и команды; Pages — внутренние страницы (настройки, история…);
/// Bookmarks, Downloads, Address — закладки, загрузки, подсказки адресной строки; Gestures — жесты мыши.
/// </summary>
public partial class MainWindow : Window
{
    readonly string[] _startupArgs;
    readonly SettingsData _settings;
    readonly SessionData _session;
    readonly AdBlocker _blocker;
    readonly HistoryStore _history;
    readonly BookmarkStore _bookmarks;
    readonly DownloadStore _downloads;
    readonly Dictionary<string, double> _zoom;
    readonly ObservableCollection<Workspace> _workspaces = new();
    readonly ObservableCollection<PanelApp> _panelApps = new();
    readonly List<TabData> _closed;
    readonly HashSet<string> _httpAllowed = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer _saveTimer;
    readonly DispatcherTimer _sleepTimer;
    readonly DispatcherTimer _updateTimer;
    readonly Updater _updater;
    UpdateManifest? _update;
    bool _updating;

    CoreWebView2Environment? _env;
    Workspace? _current;
    PanelApp? _panelApp;
    bool _panelOpen;
    bool _ready;
    bool _isFullscreen, _manualFullscreen;
    Rect _preFullscreenBounds;
    WindowState _preFullscreenState;
    NoticeWindow? _notice;

    public MainWindow(string[] args)
    {
        _startupArgs = args;
        InitializeComponent();

        AppPaths.Ensure();
        _settings = Json.Load<SettingsData>(AppPaths.SettingsFile);
        _settings.Normalize();
        try { Json.Save(AppPaths.SettingsFile, _settings); } catch { }
        _session = Json.Load<SessionData>(AppPaths.SessionFile);
        _closed = _session.Closed ?? new();
        _blocker = new AdBlocker();
        _history = new HistoryStore(AppPaths.HistoryFile);
        _bookmarks = new BookmarkStore(AppPaths.BookmarksFile);
        _downloads = new DownloadStore(AppPaths.DownloadsFile);
        _zoom = Json.Load<Dictionary<string, double>>(AppPaths.ZoomFile);
        _updater = new Updater(_settings);

        LogoPath.Data = Snowflake.Create(20);
        RailLogo.Data = Snowflake.Create(18);
        var saved = _session.Workspaces is { Count: > 0 } ? _session.Workspaces : Defaults.Workspaces();
        foreach (var w in saved) _workspaces.Add(Workspace.FromData(w));
        if (_settings.OnStartup == "start")
        {
            // «Начинать со стартовой»: остаются только закреплённые вкладки.
            foreach (var ws in _workspaces)
            {
                foreach (var t in ws.Tabs.Where(t => !t.IsPinned).ToList()) ws.Tabs.Remove(t);
                ws.ActiveTab = null;
                ws.RefreshStrip();
            }
        }
        foreach (var p in _settings.PanelApps!) _panelApps.Add(new PanelApp(p));
        WorkspaceList.ItemsSource = _workspaces;
        WorkspaceRows.ItemsSource = _workspaces;
        RailWorkspaces.ItemsSource = _workspaces;
        PanelAppList.ItemsSource = _panelApps;
        RailApps.ItemsSource = _panelApps;
        DockApps.ItemsSource = _panelApps;
        RowApps.ItemsSource = _panelApps;
        BookmarkBarItems.ItemsSource = _bookmarks.BarItems;
        RestorePlacement();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSession(); };
        _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _sleepTimer.Tick += (_, _) => SleepHiddenTabs();
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _updateTimer.Tick += (_, _) => OnUpdateTimer();
        InitLayout();

        SourceInitialized += (_, _) => Native.StyleWindow(this);
        StateChanged += (_, _) => OnWindowStateChanged();
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnKey;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKey));
        Themes.Changed += OnThemeChanged;
        _bookmarks.Changed += OnBookmarksChanged;
        _downloads.Changed += OnDownloadsChanged;
    }

    // ───────────────────────── запуск и сохранение ─────────────────────────

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions { Language = "ru-RU" };
            _env = await CoreWebView2Environment.CreateAsync(null, AppPaths.Profile, options);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Не найден движок Microsoft Edge WebView2.\nУстановите «WebView2 Runtime» с сайта Microsoft и запустите браузер снова.",
                "Снежный браузер", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        _notice = new NoticeWindow(this);
        _notice.SetAnchor(PageRectDip);
        _notice.UpdateNowBtn.Click += UpdateNow_Click;
        _notice.UpdateLaterBtn.Click += UpdateLater_Click;
        LocationChanged += (_, _) => OnWindowMoved();
        SizeChanged += (_, _) => OnWindowMoved();

        InternalPages.WriteAll();
        StartPage.Write(_workspaces, _settings);
        _ready = true;
        CreatePanelOverlay();
        ApplyLayout();
        OnWindowStateChanged();
        SwitchWorkspace(_workspaces.FirstOrDefault(w => w.Id == _session.ActiveWorkspace) ?? _workspaces[0]);
        if (_settings.OnStartup == "start" && _current?.ActiveTab is { } first && !UrlTools.IsNewTab(first.Url)) OpenNewTab();
        OpenFromOutside(_startupArgs);
        if (_session.PanelOpen)
            OpenPanel(_panelApps.FirstOrDefault(a => a.Id == _session.PanelApp) ?? _panelApps.FirstOrDefault());
        _sleepTimer.Start();
        if (_startupArgs.Contains("--updated")) ShowToast($"Браузер обновлён до версии {Updater.CurrentText}");
        StartUpdates();
        StartGestures();
        RefreshBookmarkBar();
        OnDownloadsChanged();
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSession();
        _history.Save();
        _downloads.Save();
        StopGestures();
    }

    void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void SaveSession()
    {
        if (!_ready) return;
        Rect bounds = _isFullscreen ? _preFullscreenBounds
            : WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _session.Left = bounds.Left;
            _session.Top = bounds.Top;
            _session.Width = bounds.Width;
            _session.Height = bounds.Height;
        }
        _session.Maximized = _isFullscreen ? _preFullscreenState == WindowState.Maximized : WindowState == WindowState.Maximized;
        _session.ActiveWorkspace = _current?.Id;
        _session.PanelOpen = _panelOpen;
        _session.PanelApp = _panelApp?.Id;
        _session.Workspaces = _workspaces.Select(w => w.ToData()).ToList();
        _session.Closed = _closed.TakeLast(25).ToList();
        try { Json.Save(AppPaths.SessionFile, _session); } catch { }
        _history.Save();
    }

    void SaveSettings()
    {
        try { Json.Save(AppPaths.SettingsFile, _settings); } catch { }
    }

    void RestorePlacement()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (_session.Width is double w && _session.Height is double h && w >= MinWidth && h >= MinHeight)
        {
            var left = _session.Left ?? 80;
            var top = _session.Top ?? 60;
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Width = w;
            Height = h;
            if (screen.IntersectsWith(new Rect(left + 40, top + 10, Math.Max(1, w - 80), 30)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
        if (_session.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>Ссылки, пришедшие при запуске или из второй копии программы.</summary>
    public void OpenFromOutside(string[] args)
    {
        if (!_ready || _current == null) return;
        BrowserTab? last = null;
        bool activate = true;
        foreach (var a in args)
        {
            // Команды для ярлыков и скриптов: --workspace=analytics, --panel=telegram, --panel=off, --no-activate
            if (a.StartsWith("--workspace=") && _workspaces.FirstOrDefault(w => w.Id == a[12..]) is { } ws) { SwitchWorkspace(ws); continue; }
            if (a == "--panel=off") { ClosePanel(); continue; }
            if (a.StartsWith("--panel=") && _panelApps.FirstOrDefault(p => p.Id == a[8..]) is { } app) { OpenPanel(app); continue; }
            if (a == "--no-activate") { activate = false; continue; }
            if (a == "--check-updates") { CheckForUpdates(manual: true); continue; }
            if (a.StartsWith("--cmd=")) { RunTestCommand(a[6..]); continue; }
            if (a.StartsWith("--")) continue;
            var url = File.Exists(a) ? new Uri(Path.GetFullPath(a)).AbsoluteUri : UrlTools.ToUrl(a, _settings.SearchEngine);
            if (url.Length > 0) last = NewTab(_current, url);
        }
        if (last != null) ActivateTab(last);
        if (!activate) return;
        if (WindowState == WindowState.Minimized) WindowState = _session.Maximized ? WindowState.Maximized : WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    // ───────────────────────── пространства ─────────────────────────

    void SwitchWorkspace(Workspace ws, BrowserTab? focus = null)
    {
        if (!_ready) return;
        if (_current != null && _current != ws)
        {
            _current.IsCurrent = false;
            if (_current.ActiveTab != null) HideView(_current.ActiveTab);
        }
        _current = ws;
        ws.IsCurrent = true;
        ws.RefreshStrip();
        TopTabs.ItemsSource = ws.Strip;
        SideTabs.ItemsSource = ws.Strip;
        RailTabs.ItemsSource = ws.Strip;
        var tab = focus ?? ws.ActiveTab ?? ws.Tabs.FirstOrDefault() ?? NewTab(ws, "");
        ActivateTab(tab);
        if (UrlTools.IsNewTab(tab.Url)) FocusAddress();
    }

    void UpdateWorkspaceHeader()
    {
        if (_current == null) return;
        WsTitle.Text = _current.Name.ToUpperInvariant();
        int n = _current.Tabs.Count;
        WsCount.Text = $"{n} " + Plural(n, "вкладка", "вкладки", "вкладок");
    }

    static string Plural(int n, string one, string few, string many) =>
        (n % 10 == 1 && n % 100 != 11) ? one : (n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14) ? few : many;

    static Workspace? WorkspaceOf(object sender) => (sender as FrameworkElement)?.DataContext as Workspace;

    void Workspace_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is { } ws && ws != _current) SwitchWorkspace(ws);
    }

    void AddWorkspace_Click(object sender, RoutedEventArgs e) => AddWorkspace();

    void AddWorkspace()
    {
        var name = InputDialog.Ask(this, "Новое пространство", "Например: Работа, Учёба, Музыка", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var look = Defaults.Looks[_workspaces.Count % Defaults.Looks.Length];
        var ws = new Workspace { Name = name.Trim(), Glyph = look.Glyph, Accent = look.Accent };
        _workspaces.Add(ws);
        StartPage.Write(_workspaces, _settings);
        SwitchWorkspace(ws);
        QueueSave();
    }

    void WsRename_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is not { } ws) return;
        var name = InputDialog.Ask(this, "Переименовать пространство", "", ws.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        ws.Name = name.Trim();
        UpdateWorkspaceHeader();
        StartPage.Write(_workspaces, _settings);
        QueueSave();
    }

    void WsLook_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is not { } ws) return;
        int i = Array.FindIndex(Defaults.Looks, l => l.Glyph == ws.Glyph);
        var next = Defaults.Looks[(i + 1) % Defaults.Looks.Length];
        ws.Glyph = next.Glyph;
        ws.Accent = next.Accent;
        StartPage.Write(_workspaces, _settings);
        QueueSave();
    }

    void WsDelete_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is not { } ws) return;
        if (_workspaces.Count == 1) { ShowToast("Последнее пространство удалить нельзя"); return; }
        if (!InputDialog.Confirm(this, $"Удалить «{ws.Name}»?",
                $"Закроются его вкладки ({ws.Tabs.Count}) и ссылки стартовой. Вернуть не получится.", "Удалить"))
            return;
        foreach (var t in ws.Tabs.ToList()) DisposeView(t);
        ws.Tabs.Clear();
        int index = _workspaces.IndexOf(ws);
        _workspaces.Remove(ws);
        if (_current == ws)
        {
            _current = null;
            SwitchWorkspace(_workspaces[Math.Min(index, _workspaces.Count - 1)]);
        }
        StartPage.Write(_workspaces, _settings);
        QueueSave();
    }

    // ───────────────────────── вкладки ─────────────────────────

    BrowserTab NewTab(Workspace ws, string url, string? title = null, int? index = null, bool isPrivate = false, TabGroup? group = null)
    {
        var tab = new BrowserTab { Workspace = ws, Url = url, IsPrivate = isPrivate };
        tab.Title = title ?? (UrlTools.IsNewTab(url)
            ? (isPrivate ? "Приватная вкладка" : "Новая вкладка")
            : UrlTools.InternalPage(url) is { } page ? UrlTools.Pages[page].Title : UrlTools.SiteKey(UrlTools.Host(url)));
        if (!isPrivate && UrlTools.IsWeb(url)) tab.Favicon = Favicons.Load(UrlTools.Host(url));
        int pinned = ws.Tabs.Count(t => t.IsPinned);
        if (index is int i && i >= 0 && i <= ws.Tabs.Count) ws.Tabs.Insert(Math.Max(i, pinned), tab);
        else ws.Tabs.Add(tab);
        // Вкладка, открытая из группы, остаётся в этой группе (как в Chrome и Яндексе).
        if (group != null && ws.Groups.Contains(group)) tab.Group = group;
        FixGroupAfterMove(tab);
        ws.RefreshStrip();
        if (ws == _current) UpdateWorkspaceHeader();
        QueueSave();
        return tab;
    }

    void OpenNewTab(bool isPrivate = false)
    {
        if (_current == null) return;
        ActivateTab(NewTab(_current, "", null, null, isPrivate));
        FocusAddress();
    }

    /// <summary>Ссылка из палитры или стартовой: в текущей вкладке, если она пустая, иначе в новой.</summary>
    void OpenSmart(string url)
    {
        if (_current == null) return;
        var tab = _current.ActiveTab;
        if (tab != null && UrlTools.IsNewTab(tab.Url)) Navigate(tab, url);
        else ActivateTab(NewTab(_current, url, null, tab != null ? _current.Tabs.IndexOf(tab) + 1 : null, tab?.IsPrivate == true, tab?.Group));
    }

    /// <summary>Внутренняя страница (настройки, история…): если уже открыта — перейти к ней.</summary>
    void OpenInternal(string page, string? anchor = null)
    {
        if (_current == null) return;
        var url = UrlTools.PageUrl(page, anchor);
        var existing = _current.Tabs.FirstOrDefault(t => UrlTools.InternalPage(t.Url) == page);
        if (existing != null)
        {
            ActivateTab(existing);
            if (anchor != null) Navigate(existing, url);
            return;
        }
        OpenSmart(url);
    }

    void ActivateTab(BrowserTab tab, bool createView = true)
    {
        if (!_ready) return;
        if (tab.Workspace != _current) { SwitchWorkspace(tab.Workspace, tab); return; }
        var ws = tab.Workspace;
        if (tab.Group is { Collapsed: true } g)
        {
            g.Collapsed = false;
            ws.RefreshStrip();
        }
        var prev = ws.ActiveTab;
        ws.ActiveTab = tab;
        if (prev != null && prev != tab)
        {
            HideView(prev);
            prev.IsActive = false;
        }
        foreach (var t in ws.Tabs) if (t != tab && t.IsActive) t.IsActive = false;
        tab.IsActive = true;
        if (prev != tab)
        {
            HideSuggestions();
            SetAddressText(UrlTools.Display(tab.Url));
            if (FindBar.Visibility == Visibility.Visible) { StopFind(); RunFind(); }
        }
        if (tab.View != null) ShowView(tab);
        else if (createView) _ = EnsureView(tab, navigate: true);

        BringTabIntoView(tab);
        UpdateWorkspaceHeader();
        UpdateChrome();
        QueueSave();
    }

    void CloseTab(BrowserTab tab, bool keepView = false)
    {
        var ws = tab.Workspace;
        int index = ws.Tabs.IndexOf(tab);
        if (index < 0) return;
        if (!keepView && !tab.IsPrivate && (UrlTools.IsWeb(tab.Url) || UrlTools.InternalPage(tab.Url) != null))
        {
            _closed.Add(new TabData { Url = tab.Url, Title = tab.Title, WorkspaceId = ws.Id, Pinned = tab.IsPinned });
            if (_closed.Count > 40) _closed.RemoveAt(0);
        }
        bool wasActive = ws.ActiveTab == tab;
        ws.Tabs.RemoveAt(index);
        tab.IsActive = false;
        if (keepView && tab.View != null)
        {
            // Страница отдаёт файл: вкладку убираем, а сам движок живёт, пока файл не скачается.
            tab.View.Visibility = Visibility.Collapsed;
            _downloadViews.Add(tab.View);
            tab.View = null;
        }
        else DisposeView(tab);
        ws.RefreshStrip();
        if (wasActive)
        {
            ws.ActiveTab = null;
            if (ws == _current)
            {
                // Как в Яндексе: следующей становится соседняя вкладка той же группы, иначе — справа.
                BrowserTab? next = null;
                if (tab.Group != null)
                    next = ws.Tabs.Skip(Math.Min(index, ws.Tabs.Count)).FirstOrDefault(t => t.Group == tab.Group)
                           ?? ws.Tabs.Take(index).LastOrDefault(t => t.Group == tab.Group);
                next ??= ws.Tabs.Count > 0 ? ws.Tabs[Math.Min(index, ws.Tabs.Count - 1)] : NewTab(ws, "");
                ActivateTab(next);
                if (UrlTools.IsNewTab(next.Url)) FocusAddress();
            }
        }
        if (ws == _current) UpdateWorkspaceHeader();
        QueueSave();
    }

    void CloseActiveTab()
    {
        if (_current?.ActiveTab is { } t) CloseTab(t);
    }

    void ReopenClosedTab()
    {
        if (_closed.Count == 0) { ShowToast("Закрытых вкладок нет"); return; }
        var data = _closed[^1];
        _closed.RemoveAt(_closed.Count - 1);
        var ws = _workspaces.FirstOrDefault(w => w.Id == data.WorkspaceId) ?? _current!;
        var tab = NewTab(ws, data.Url, data.Title, data.Pinned ? 0 : null);
        if (data.Pinned) { tab.IsPinned = true; ws.RefreshStrip(); }
        ActivateTab(tab);
    }

    void DuplicateTab(BrowserTab tab)
    {
        var ws = tab.Workspace;
        var copy = NewTab(ws, tab.Url, tab.Title, ws.Tabs.IndexOf(tab) + 1, tab.IsPrivate, tab.Group);
        if (tab.IsPinned) { copy.IsPinned = true; ws.RefreshStrip(); }
        ActivateTab(copy);
    }

    void MoveTab(BrowserTab tab, Workspace target)
    {
        if (tab.Workspace == target) return;
        var source = tab.Workspace;
        bool wasActive = source.ActiveTab == tab;
        int index = source.Tabs.IndexOf(tab);
        source.Tabs.Remove(tab);
        tab.Group = null;
        tab.IsActive = false;
        source.RefreshStrip();
        if (wasActive)
        {
            source.ActiveTab = null;
            HideView(tab);
            if (source == _current)
                ActivateTab(source.Tabs.Count > 0 ? source.Tabs[Math.Min(index, source.Tabs.Count - 1)] : NewTab(source, ""));
        }
        tab.Workspace = target;
        if (tab.IsPinned) target.Tabs.Insert(target.Tabs.Count(t => t.IsPinned), tab);
        else target.Tabs.Add(tab);
        target.RefreshStrip();
        UpdateWorkspaceHeader();
        ShowToast($"Вкладка перенесена в «{target.Name}»");
        QueueSave();
    }

    void CycleTab(int step)
    {
        if (_current?.ActiveTab is not { } tab) return;
        var visible = _current.Strip.OfType<BrowserTab>().ToList();
        if (visible.Count < 2) return;
        int i = (visible.IndexOf(tab) + step + visible.Count) % visible.Count;
        ActivateTab(visible[i]);
    }

    void SelectTabByNumber(int n)
    {
        if (_current == null || _current.Tabs.Count == 0) return;
        ActivateTab(n >= 8 ? _current.Tabs[^1] : _current.Tabs[Math.Min(n, _current.Tabs.Count - 1)]);
    }

    void SelectWorkspaceByNumber(int n)
    {
        if (n < _workspaces.Count) SwitchWorkspace(_workspaces[n]);
    }

    void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTab tab) CloseTab(tab);
        e.Handled = true;
    }

    void TabMute_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTab tab) ToggleMute(tab);
        e.Handled = true;
    }

    void ToggleMute(BrowserTab tab)
    {
        if (tab.View?.CoreWebView2 is { } core) core.IsMuted = !core.IsMuted;
    }

    void NewTab_Click(object sender, RoutedEventArgs e) => OpenNewTab();

    // ───────────────────────── движок страниц ─────────────────────────

    async Task EnsureView(BrowserTab tab, bool navigate)
    {
        if (tab.View != null || _env == null) return;
        var view = new WebView2 { DefaultBackgroundColor = Themes.ToDrawing(Themes.Current.PageBg) };
        tab.View = view;
        view.Visibility = tab == _current?.ActiveTab ? Visibility.Visible : Visibility.Collapsed;
        PageHost.Children.Add(view);
        try
        {
            if (tab.IsPrivate)
            {
                var options = _env.CreateCoreWebView2ControllerOptions();
                options.IsInPrivateModeEnabled = true;
                options.ProfileName = "Private";
                await view.EnsureCoreWebView2Async(_env, options);
            }
            else
            {
                await view.EnsureCoreWebView2Async(_env);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return;
        }
        if (tab.View != view || view.CoreWebView2 == null) return;
        Configure(view.CoreWebView2, tab, null);
        view.ZoomFactorChanged += (_, _) => OnZoomChanged(tab);
        if (navigate) Navigate(tab, tab.Url);
        if (tab == _current?.ActiveTab) UpdateChrome();
    }

    void Configure(CoreWebView2 core, BrowserTab? tab, PanelApp? app)
    {
        var s = core.Settings;
        s.AreHostObjectsAllowed = false;          // страницы не достают до программы
        s.IsWebMessageEnabled = tab != null;      // сообщения принимаются только от своих страниц (start.sneznyj)
        s.AreDevToolsEnabled = true;
        s.IsStatusBarEnabled = true;
        s.IsReputationCheckingRequired = true;    // SmartScreen: защита от фишинга и вирусов
        s.IsGeneralAutofillEnabled = _settings.Autofill;
        s.IsPasswordAutosaveEnabled = _settings.SavePasswords && tab?.IsPrivate != true;
        try { core.Profile.PreferredTrackingPreventionLevel = _settings.TrackingLevel(); } catch { }
        ApplyColorScheme(core);

        core.SetVirtualHostNameToFolderMapping(UrlTools.StartHost, AppPaths.Start, CoreWebView2HostResourceAccessKind.Deny);
        foreach (var pattern in _blocker.Patterns)
            core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, e) => OnBlockable(core, e, tab);
        core.NewWindowRequested += (_, e) => OnNewWindow(e, tab);
        core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);
        core.DownloadStarting += (_, e) => OnDownloadStarting(core, e);
        core.ContextMenuRequested += (_, e) => OnContextMenu(core, e, tab);
        if (tab == null) return;

        core.NavigationStarting += (_, e) =>
        {
            if (TryUpgradeToHttps(core, tab, e)) return;
            if (!e.IsRedirected) tab.Blocked = 0;
            tab.PendingUrl = e.Uri;
            tab.IsLoading = true;
            if (tab == _current?.ActiveTab) UpdateChrome();
        };
        core.NavigationCompleted += (_, e) =>
        {
            tab.IsLoading = false;
            tab.PendingUrl = null;
            if (HttpsFallback(core, tab, e)) return;
            if (e.IsSuccess && !tab.IsPrivate) _history.Add(core.Source, core.DocumentTitle);
            if (e.IsSuccess) CheckReadable(tab, core);
            if (tab == _current?.ActiveTab) UpdateChrome();
        };
        core.SourceChanged += (_, _) =>
        {
            tab.Url = core.Source;
            if (UrlTools.IsNewTab(tab.Url))
            {
                tab.Title = tab.IsPrivate ? "Приватная вкладка" : "Новая вкладка";
                tab.Favicon = null;
            }
            else if (UrlTools.IsStart(tab.Url)) tab.Favicon = null;
            ApplySiteZoom(tab);
            if (tab == _current?.ActiveTab) UpdateChrome();
            QueueSave();
        };
        core.DocumentTitleChanged += (_, _) =>
        {
            if (UrlTools.IsNewTab(core.Source)) return;
            var title = core.DocumentTitle;
            tab.Title = string.IsNullOrWhiteSpace(title) ? UrlTools.SiteKey(UrlTools.Host(core.Source)) : title;
            if (!tab.IsPrivate) _history.UpdateTitle(core.Source, tab.Title);
            if (tab == _current?.ActiveTab) UpdateTitle();
            QueueSave();
        };
        core.HistoryChanged += (_, _) => { if (tab == _current?.ActiveTab) UpdateNavButtons(); };
        core.FaviconChanged += async (_, _) =>
        {
            var url = core.Source;
            if (UrlTools.IsStart(url)) { tab.Favicon = null; return; }
            try
            {
                var img = await Favicons.FromCore(core, UrlTools.Host(url), save: !tab.IsPrivate);
                if (img != null) tab.Favicon = img;
            }
            catch { }
        };
        core.WindowCloseRequested += (_, _) => CloseTab(tab);
        core.IsDocumentPlayingAudioChanged += (_, _) => tab.IsAudible = core.IsDocumentPlayingAudio;
        core.IsMutedChanged += (_, _) => tab.IsMuted = core.IsMuted;
        core.WebMessageReceived += (_, e) => OnPageMessage(core, tab, e);
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                tab.IsLoading = false;
                tab.Title = "Страница упала — нажмите F5";
            }
        };
    }

    void ApplyColorScheme(CoreWebView2 core)
    {
        try
        {
            core.Profile.PreferredColorScheme = _settings.SitesScheme switch
            {
                "light" => CoreWebView2PreferredColorScheme.Light,
                "dark" => CoreWebView2PreferredColorScheme.Dark,
                _ => Themes.Current.IsDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light,
            };
        }
        catch { }
    }

    /// <summary>«Сначала HTTPS»: ссылку http:// пробуем открыть защищённо, а если сайт не умеет — открываем как есть.</summary>
    bool TryUpgradeToHttps(CoreWebView2 core, BrowserTab tab, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!_settings.HttpsFirst) return false;
        if (!e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return false;
        if (e.NavigationKind != CoreWebView2NavigationKind.NewDocument) return false;
        var host = UrlTools.Host(e.Uri);
        if (host.Length == 0 || _httpAllowed.Contains(host) || UrlTools.IsLocalHost(host)) return false;
        if (tab.HttpsUpgradeFrom != null && string.Equals(UrlTools.Host(tab.HttpsUpgradeFrom), host, StringComparison.OrdinalIgnoreCase))
        {
            // Сайт сам вернул нас на http — значит, https у него нет.
            _httpAllowed.Add(host);
            tab.HttpsUpgradeFrom = null;
            return false;
        }
        e.Cancel = true;
        tab.HttpsUpgradeFrom = e.Uri;
        var secure = "https://" + e.Uri[7..];
        Dispatcher.BeginInvoke(() => { try { core.Navigate(secure); } catch { } });
        return true;
    }

    bool HttpsFallback(CoreWebView2 core, BrowserTab tab, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (tab.HttpsUpgradeFrom is not { } original) return false;
        if (e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            if (e.IsSuccess) tab.HttpsUpgradeFrom = null;
            return false;
        }
        tab.HttpsUpgradeFrom = null;
        _httpAllowed.Add(UrlTools.Host(original));
        try { core.Navigate(original); } catch { }
        if (tab == _current?.ActiveTab) ShowToast("У сайта нет защищённого соединения — открыт обычный http. Не вводите там пароли.");
        return true;
    }

    void OnBlockable(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e, BrowserTab? tab)
    {
        if (!_settings.AdBlock || _env == null) return;
        var site = UrlTools.SiteKey(UrlTools.Host(tab?.PendingUrl ?? core.Source));
        if (_settings.IsAllowed(site)) return;
        // Если человек сам открыл такой адрес — не мешаем.
        if (e.ResourceContext == CoreWebView2WebResourceContext.Document && tab?.PendingUrl != null &&
            string.Equals(e.Request.Uri, tab.PendingUrl, StringComparison.OrdinalIgnoreCase))
            return;
        e.Response = _env.CreateWebResourceResponse(null, 403, "Blocked", "");
        if (tab == null) return;
        tab.Blocked++;
        if (tab == _current?.ActiveTab) UpdateShield();
    }

    async void OnNewWindow(CoreWebView2NewWindowRequestedEventArgs e, BrowserTab? opener)
    {
        if (!e.IsUserInitiated)
        {
            e.Handled = true;
            ShowToast("Заблокировано всплывающее окно");
            return;
        }
        var ws = opener?.Workspace ?? _current!;
        int index = opener != null && _settings.OpenNearCurrent ? NextIndexAfter(opener) : ws.Tabs.Count;
        bool isPrivate = opener?.IsPrivate == true;

        // Ctrl+клик — открыть в фоне, страница загрузится при переходе на вкладку.
        if (Native.IsKeyDown(0x11))
        {
            e.Handled = true;
            NewTab(ws, e.Uri, null, index, isPrivate, opener?.Group);
            ShowToast("Открыто в фоновой вкладке");
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var tab = NewTab(ws, e.Uri, null, index, isPrivate, opener?.Group);
            ActivateTab(tab, createView: false);
            await EnsureView(tab, navigate: false);
            if (tab.View?.CoreWebView2 is { } core) e.NewWindow = core;   // окно-«ребёнок» знает родителя: входы через Google и т. п. работают
            else tab.Url = e.Uri;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>Куда вставить вкладку, открытую из opener: после неё и уже открытых из неё же соседей группы.</summary>
    int NextIndexAfter(BrowserTab opener)
    {
        var tabs = opener.Workspace.Tabs;
        int i = tabs.IndexOf(opener) + 1;
        if (opener.Group != null)
            while (i < tabs.Count && tabs[i].Group == opener.Group) i++;
        return i;
    }

    void Navigate(BrowserTab tab, string url)
    {
        if (UrlTools.IsNewTab(url))
        {
            if (Favicons.Dirty) StartPage.Write(_workspaces, _settings);
            url = UrlTools.StartUrl(tab.Workspace.Id);
        }
        var core = tab.View?.CoreWebView2;
        if (core == null)
        {
            tab.Url = url;
            if (tab.View == null && tab == _current?.ActiveTab) _ = EnsureView(tab, navigate: true);
            return;
        }
        try { core.Navigate(url); }
        catch (ArgumentException) { core.Navigate(UrlTools.SearchUrl(_settings.SearchEngine, url)); }
    }

    void HideView(BrowserTab tab)
    {
        tab.HiddenSince = DateTime.Now;
        if (tab.View == null) return;
        tab.View.Visibility = Visibility.Collapsed;
        if (tab.View.CoreWebView2 is { } core)
            try { core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; } catch { }
    }

    void ShowView(BrowserTab tab)
    {
        if (tab.View == null) return;
        tab.View.Visibility = Visibility.Visible;
        if (tab.View.CoreWebView2 is { } core)
        {
            try
            {
                if (core.IsSuspended) core.Resume();
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch { }
        }
    }

    void DisposeView(BrowserTab tab)
    {
        if (tab.View == null) return;
        var view = tab.View;
        tab.View = null;
        PageHost.Children.Remove(view);
        try { view.Dispose(); } catch { }
    }

    /// <summary>«Спящие вкладки»: давно скрытые страницы замораживаются и не тратят память и процессор.</summary>
    void SleepHiddenTabs()
    {
        if (!_settings.SleepingTabs) return;
        foreach (var ws in _workspaces)
        foreach (var tab in ws.Tabs)
        {
            if (tab == _current?.ActiveTab || tab.View?.CoreWebView2 is not { } core) continue;
            if (tab.View.Visibility == Visibility.Visible || core.IsSuspended || core.IsDocumentPlayingAudio) continue;
            if ((DateTime.Now - tab.HiddenSince).TotalMinutes < _settings.SleepAfterMinutes) continue;
            try { _ = core.TrySuspendAsync(); } catch { }
        }
    }

    CoreWebView2? ActiveCore => _current?.ActiveTab?.View?.CoreWebView2;

    IEnumerable<CoreWebView2> AllCores() =>
        _workspaces.SelectMany(w => w.Tabs).Select(t => t.View?.CoreWebView2)
            .Concat(_panelApps.Select(a => a.View?.CoreWebView2))
            .Where(c => c != null)!;

    CoreWebView2? AnyNormalCore() =>
        _workspaces.SelectMany(w => w.Tabs).FirstOrDefault(t => !t.IsPrivate && t.View?.CoreWebView2 != null)?.View?.CoreWebView2
        ?? _panelApps.FirstOrDefault(a => a.View?.CoreWebView2 != null)?.View?.CoreWebView2;

    // ───────────────────────── верхняя панель ─────────────────────────

    void UpdateChrome()
    {
        var tab = _current?.ActiveTab;
        if (tab == null) return;
        if (!AddressBox.IsKeyboardFocusWithin) SetAddressText(UrlTools.Display(tab.Url));
        UpdatePlaceholder();

        var url = tab.Url ?? "";
        if (UrlTools.IsStart(url))
        {
            SecurityIcon.Text = UrlTools.InternalPage(url) != null ? "" : "";
            SecurityIcon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            SecurityIcon.ToolTip = UrlTools.InternalPage(url) != null ? "Страница браузера" : null;
            if (UrlTools.InternalPage(url) is { } p) SecurityIcon.Text = UrlTools.Pages[p].Glyph;
        }
        else if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            SecurityIcon.Text = "";
            SecurityIcon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            SecurityIcon.ToolTip = "Соединение защищено (HTTPS)";
        }
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            SecurityIcon.Text = "";
            SecurityIcon.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            SecurityIcon.ToolTip = "Соединение не защищено: не вводите здесь пароли и данные карт";
        }
        else
        {
            SecurityIcon.Text = UrlTools.IsNewTab(url) ? "" : "";
            SecurityIcon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            SecurityIcon.ToolTip = null;
        }

        ReloadBtn.Content = tab.IsLoading ? "" : "";
        ReloadBtn.ToolTip = tab.IsLoading ? "Остановить (Esc)" : "Обновить (F5)";
        LoadBar.Visibility = tab.IsLoading ? Visibility.Visible : Visibility.Hidden;
        PrivateBadge.Visibility = tab.IsPrivate ? Visibility.Visible : Visibility.Collapsed;
        if (!AddressBox.IsKeyboardFocusWithin)
        {
            if (tab.IsPrivate) AddressBorder.SetResourceReference(Border.BorderBrushProperty, "PrivateBrush");
            else AddressBorder.BorderBrush = Brushes.Transparent;
        }
        UpdateTitle();
        UpdateNavButtons();
        UpdateShield();
        UpdateStar();
        UpdateZoomChip();
        UpdateReaderButton();
    }

    void UpdateTitle()
    {
        var tab = _current?.ActiveTab;
        if (tab == null) return;
        Title = (UrlTools.IsNewTab(tab.Url) ? "Новая вкладка" : tab.Title) + " — Снежный браузер";
    }

    void UpdateNavButtons()
    {
        var core = ActiveCore;
        BackBtn.IsEnabled = core?.CanGoBack == true;
        ForwardBtn.IsEnabled = core?.CanGoForward == true;
    }

    void UpdateShield()
    {
        var tab = _current?.ActiveTab;
        var site = UrlTools.SiteKey(UrlTools.Host(tab?.Url));
        if (!_settings.AdBlock)
        {
            ShieldIcon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            ShieldCount.Text = "выкл";
            ShieldBtn.ToolTip = "Блокировщик рекламы выключен везде. Нажмите, чтобы включить";
        }
        else if (UrlTools.IsWeb(tab?.Url) && _settings.IsAllowed(site))
        {
            ShieldIcon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            ShieldCount.Text = "пауза";
            ShieldBtn.ToolTip = $"На {site} блокировщик отключён. Нажмите, чтобы включить";
        }
        else
        {
            int n = tab?.Blocked ?? 0;
            ShieldIcon.SetResourceReference(TextBlock.ForegroundProperty, "GoodBrush");
            ShieldCount.Text = n.ToString();
            ShieldBtn.ToolTip = UrlTools.IsWeb(tab?.Url)
                ? $"Заблокировано рекламы и трекеров: {n}.\nНажмите, чтобы отключить блокировщик на {site}"
                : $"Блокировщик включён: {_blocker.Count} рекламных и следящих доменов";
        }
    }

    void UpdatePlaceholder() =>
        AddressPlaceholder.Visibility = AddressBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void FocusAddress()
    {
        Dispatcher.BeginInvoke(() =>
        {
            AddressBox.Focus();
            Keyboard.Focus(AddressBox);
            AddressBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    void FocusPage()
    {
        Dispatcher.BeginInvoke(() => _current?.ActiveTab?.View?.Focus(), DispatcherPriority.Input);
    }

    void Back_Click(object sender, RoutedEventArgs e) => GoBack();
    void Forward_Click(object sender, RoutedEventArgs e) => GoForward();

    void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_current?.ActiveTab is { IsLoading: true }) ActiveCore?.Stop();
        else Reload();
    }

    void GoBack() { if (ActiveCore is { CanGoBack: true } c) c.GoBack(); }
    void GoForward() { if (ActiveCore is { CanGoForward: true } c) c.GoForward(); }

    void Reload() => ReloadTab(_current?.ActiveTab);

    void ReloadTab(BrowserTab? tab)
    {
        if (tab == null) return;
        if (tab.View?.CoreWebView2 is { } core) core.Reload();
        else Navigate(tab, tab.Url);
    }

    void Shield_Click(object sender, RoutedEventArgs e)
    {
        var tab = _current?.ActiveTab;
        if (!_settings.AdBlock)
        {
            _settings.AdBlock = true;
            SaveSettings();
            ShowToast("Блокировщик включён");
        }
        else if (UrlTools.IsWeb(tab?.Url))
        {
            var site = UrlTools.SiteKey(UrlTools.Host(tab!.Url));
            if (_settings.IsAllowed(site))
            {
                _settings.AdBlockAllowSites!.RemoveAll(s => site.Equals(s, StringComparison.OrdinalIgnoreCase) ||
                                                            site.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));
                ShowToast($"Блокировщик снова работает на {site}");
            }
            else
            {
                _settings.AdBlockAllowSites!.Add(site);
                ShowToast($"На {site} блокировщик отключён");
            }
            SaveSettings();
        }
        else return;
        Reload();
        UpdateShield();
    }

    void AddToStart(BrowserTab tab)
    {
        if (!UrlTools.IsWeb(tab.Url) || tab.IsPrivate) { ShowToast("Эту страницу нельзя добавить на стартовую"); return; }
        var ws = tab.Workspace;
        if (ws.Links.Any(l => l.Url == tab.Url)) { ShowToast($"Уже есть на стартовой «{ws.Name}»"); return; }
        var title = tab.Title.Length > 40 ? tab.Title[..40].TrimEnd() + "…" : tab.Title;
        ws.Links.Add(new QuickLink { Title = title, Url = tab.Url });
        StartPage.Write(_workspaces, _settings);
        QueueSave();
        ShowToast($"Добавлено на стартовую «{ws.Name}»");
    }

    void ShowToast(string text, string? actionText = null, Action? action = null) =>
        _notice?.ShowToast(text, actionText, action);

    /// <summary>Где страница на экране (единицы WPF) — для подсказок и окон поверх неё.</summary>
    Rect PageRectDip()
    {
        if (!IsLoaded || PresentationSource.FromVisual(PageBorder) == null) return Rect.Empty;
        var px = Native.ScreenRectPx(PageBorder);
        var tl = Native.PxToDip(this, px.TopLeft);
        var br = Native.PxToDip(this, px.BottomRight);
        return new Rect(tl, br);
    }

    // ───────────────────────── боковая панель приложений ─────────────────────────

    void PanelToggle_Click(object sender, RoutedEventArgs e) => TogglePanel();
    void PanelClose_Click(object sender, RoutedEventArgs e) => ClosePanel();

    void PanelApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PanelApp app) return;
        bool inPanelHeader = sender is DependencyObject d && PanelAppList.IsAncestorOf(d);
        if (!inPanelHeader && _panelOpen && _panelApp == app) ClosePanel();   // повторный клик в левой панели — скрыть
        else OpenPanel(app);
    }

    void PanelToTab_Click(object sender, RoutedEventArgs e)
    {
        var url = _panelApp?.View?.CoreWebView2?.Source ?? _panelApp?.Url;
        if (!string.IsNullOrEmpty(url)) OpenSmart(url);
    }

    void PanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _session.PanelWidth = PanelCol.ActualWidth;
        QueueSave();
    }

    void TogglePanel()
    {
        if (_panelOpen) ClosePanel();
        else OpenPanel(_panelApp ?? _panelApps.FirstOrDefault());
    }

    async void OpenPanel(PanelApp? app)
    {
        if (app == null || _env == null) return;
        _panelOpen = true;
        _panelApp = app;
        if (!_isFullscreen) ShowPanelColumns(true);
        foreach (var a in _panelApps)
        {
            a.IsCurrent = a == app;
            a.IsShown = a == app;
            if (a.View != null) a.View.Visibility = a == app ? Visibility.Visible : Visibility.Collapsed;
        }
        QueueSave();
        if (app.View != null) return;

        var view = new WebView2 { DefaultBackgroundColor = Themes.ToDrawing(Themes.Current.PageBg) };
        app.View = view;
        PanelHost.Children.Add(view);
        try { await view.EnsureCoreWebView2Async(_env); }
        catch (Exception ex) { Debug.WriteLine(ex); return; }
        Configure(view.CoreWebView2, null, app);
        view.CoreWebView2.Navigate(app.Url);
    }

    void ClosePanel()
    {
        _panelOpen = false;
        foreach (var a in _panelApps) a.IsShown = false;
        ShowPanelColumns(false);
        QueueSave();
    }

    void ShowPanelColumns(bool show)
    {
        PanelRoot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PanelSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SplitterCol.Width = new GridLength(show ? 6 : 0);
        PanelCol.MinWidth = show ? 280 : 0;
        PanelCol.Width = new GridLength(show ? Math.Clamp(_session.PanelWidth, 280, 900) : 0);
        foreach (var a in _panelApps)
            if (a.View != null) a.View.Visibility = show && a == _panelApp ? Visibility.Visible : Visibility.Collapsed;
    }

    // ───────────────────────── обновления ─────────────────────────

    void StartUpdates()
    {
        // Сборку из папки разработки не обновляем, если не задан свой адрес (тестовый сервер).
        if (!Updater.IsInstalled && string.IsNullOrWhiteSpace(_settings.UpdateUrl)) return;
        if (_updater.Pending() is { } pending) ShowUpdate(pending);
        _updateTimer.Start();
    }

    void OnUpdateTimer()
    {
        // Первая проверка через 15 секунд после запуска, дальше раз в час смотрим, не прошли ли сутки.
        if (_updateTimer.Interval < TimeSpan.FromHours(1) && !_updating) Updater.CleanDownloads();
        _updateTimer.Interval = TimeSpan.FromHours(1);
        if (_updater.CheckDue) CheckForUpdates(manual: false);
    }

    async void CheckForUpdates(bool manual)
    {
        if (_updating) return;
        try
        {
            if (await _updater.CheckAsync() is { } m) ShowUpdate(m);
            else if (manual) ShowToast($"У вас последняя версия: {Updater.CurrentText}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            if (manual) ShowToast("Не удалось проверить обновления: " + ex.Message);
        }
    }

    void ShowUpdate(UpdateManifest m)
    {
        if (_updating || _notice == null) return;
        _update = m;
        var n = _notice;
        n.UpdateHeading.Text = $"Новая версия {m.Version}";
        n.UpdateNotes.Text = string.IsNullOrWhiteSpace(m.Notes) ? $"Сейчас у вас {Updater.CurrentText}." : m.Notes;
        n.UpdateNotes.ToolTip = m.Notes.Length > 0 ? m.Notes : null;
        n.UpdateProgress.Visibility = Visibility.Collapsed;
        n.UpdateButtons.Visibility = Visibility.Visible;
        n.UpdateNowBtn.Content = "Обновить";
        n.UpdateCard.Visibility = Visibility.Visible;
        n.Refresh();
    }

    void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        _updater.Postpone();
        if (_notice == null) return;
        _notice.UpdateCard.Visibility = Visibility.Collapsed;
        _notice.Refresh();
        ShowToast("Напомню завтра. Обновиться сразу — меню → «О браузере и обновления»");
    }

    async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } m || _updating || _notice == null) return;
        var n = _notice;
        _updating = true;
        n.UpdateButtons.Visibility = Visibility.Collapsed;
        n.UpdateProgress.Visibility = Visibility.Visible;
        n.UpdateTrack.Visibility = n.UpdateFill.Visibility = Visibility.Visible;
        n.UpdateStatus.Margin = new Thickness(0, 10, 0, 0);
        n.UpdateFill.Width = 0;
        double mb = m.Size / 1048576.0;
        n.UpdateStatus.Text = $"Скачиваю… 0% из {mb:0} МБ";
        var progress = new Progress<double>(p =>
        {
            n.UpdateFill.Width = n.UpdateTrack.ActualWidth * Math.Clamp(p, 0, 1);
            n.UpdateStatus.Text = $"Скачиваю… {p * 100:0}% из {mb:0} МБ";
        });
        try
        {
            var path = await _updater.DownloadAsync(m, progress);
            n.UpdateFill.Width = n.UpdateTrack.ActualWidth;
            n.UpdateStatus.Text = "Подпись и контрольная сумма сходятся. Перезапускаю браузер, вкладки сохранятся…";
            await Task.Delay(900);
            SaveSession();
            Updater.RunInstaller(path, noActivate: !ShowActivated);
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _updating = false;
            n.UpdateTrack.Visibility = n.UpdateFill.Visibility = Visibility.Collapsed;
            n.UpdateStatus.Margin = new Thickness(0);
            n.UpdateStatus.Text = "Не получилось: " + ex.Message;
            n.UpdateButtons.Visibility = Visibility.Visible;
            n.UpdateNowBtn.Content = "Повторить";
        }
    }

    // ───────────────────────── окно и полноэкранный режим ─────────────────────────

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void OnWindowStateChanged()
    {
        MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        MaxBtn.ToolTip = WindowState == WindowState.Maximized ? "Свернуть в окно" : "Развернуть";
        CollapsePanelOverlay(immediate: true);
        _notice?.Refresh();
        if (_isFullscreen) return;
        RootGrid.Margin = WindowState == WindowState.Maximized ? Native.MaximizedOverhang(this) : new Thickness(0);
    }

    void OnWindowMoved()
    {
        CollapsePanelOverlay(immediate: true);
        _notice?.Reposition();
        UpdateGestureTarget();
    }

    void SetFullscreen(bool on, bool manual = false)
    {
        if (on == _isFullscreen) return;
        _isFullscreen = on;
        _manualFullscreen = on && manual;
        if (on)
        {
            _preFullscreenState = WindowState;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            _preFullscreenBounds = new Rect(Left, Top, Width, Height);
            CollapsePanelOverlay(immediate: true);
            LeftHost.Visibility = Visibility.Collapsed;
            TabRow.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            BookmarksBarRow.Visibility = Visibility.Collapsed;
            FindBar.Visibility = Visibility.Collapsed;
            CaptionButtons.Visibility = Visibility.Collapsed;
            PageBorder.BorderThickness = new Thickness(0);
            RootGrid.Margin = new Thickness(0);
            ShowPanelColumns(false);
            ApplyWindowChrome();
            Native.StyleWindow(this, rounded: false);
            var r = Native.MonitorBounds(this);
            Left = r.Left;
            Top = r.Top;
            Width = r.Width;
            Height = r.Height;
            Activate();
        }
        else
        {
            LeftHost.Visibility = Visibility.Visible;
            Toolbar.Visibility = Visibility.Visible;
            CaptionButtons.Visibility = Visibility.Visible;
            PageBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            Native.StyleWindow(this);
            Left = _preFullscreenBounds.Left;
            Top = _preFullscreenBounds.Top;
            Width = _preFullscreenBounds.Width;
            Height = _preFullscreenBounds.Height;
            if (_preFullscreenState == WindowState.Maximized) WindowState = WindowState.Maximized;
            ApplyLayout();
            OnWindowStateChanged();
            if (_panelOpen) ShowPanelColumns(true);
            if (ActiveCore is { ContainsFullScreenElement: true } core)
                _ = core.ExecuteScriptAsync("document.exitFullscreen && document.exitFullscreen()");
        }
    }

    void ApplyWindowChrome()
    {
        bool top = _settings.TabsPosition == "top";
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = _isFullscreen ? 0 : top ? 40 : 46,
            ResizeBorderThickness = new Thickness(_isFullscreen ? 0 : 6),
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0),
        });
    }

    // ───────────────────────── клавиши ─────────────────────────

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        bool alt = mods.HasFlag(ModifierKeys.Alt);
        bool handled = true;

        if (ctrl && !alt)
        {
            switch (key)
            {
                case Key.T when shift: ReopenClosedTab(); break;
                case Key.T: OpenNewTab(); break;
                case Key.N when shift: OpenNewTab(isPrivate: true); break;
                case Key.N: OpenNewTab(); break;
                case Key.W:
                case Key.F4: CloseActiveTab(); break;
                case Key.L: FocusAddress(); break;
                case Key.K: OpenPalette(); break;
                case Key.D: ShowBookmarkPopup(); break;
                case Key.B when shift: TogglePanel(); break;
                case Key.O when shift: OpenInternal("bookmarks"); break;
                case Key.H: OpenInternal("history"); break;
                case Key.J: OpenInternal("downloads"); break;
                case Key.Delete when shift: OpenInternal("settings", "privacy"); break;
                case Key.S when shift: TakeScreenshot(fullPage: false); break;
                case Key.F: StartFind(); break;
                case Key.P: PrintPage(); break;
                case Key.S: SavePageAs(); break;
                case Key.OemPlus or Key.Add: ZoomStep(+1); break;
                case Key.OemMinus or Key.Subtract: ZoomStep(-1); break;
                case Key.D0 or Key.NumPad0: SetZoom(1.0); break;
                case Key.Tab: CycleTab(shift ? -1 : 1); break;
                case Key.PageDown: CycleTab(1); break;
                case Key.PageUp: CycleTab(-1); break;
                case Key.R: Reload(); break;
                case >= Key.D1 and <= Key.D9 when !shift: SelectTabByNumber(key - Key.D1); break;
                case Key.OemComma: OpenInternal("settings"); break;
                default: handled = false; break;
            }
        }
        else if (alt && !ctrl && !shift)
        {
            switch (key)
            {
                case Key.D: FocusAddress(); break;
                case Key.Left: GoBack(); break;
                case Key.Right: GoForward(); break;
                case Key.Home: if (_current?.ActiveTab is { } t) Navigate(t, ""); break;
                case Key.F or Key.E: ShowMainMenu(); break;
                case >= Key.D1 and <= Key.D9: SelectWorkspaceByNumber(key - Key.D1); break;
                default: handled = false; break;
            }
        }
        else if (!ctrl && !alt)
        {
            switch (key)
            {
                case Key.F5: Reload(); break;
                case Key.F6: FocusAddress(); break;
                case Key.F11: SetFullscreen(!_isFullscreen, manual: true); break;
                case Key.F12: ActiveCore?.OpenDevToolsWindow(); break;
                case Key.Escape when _manualFullscreen: SetFullscreen(false); break;
                case Key.Escape when _current?.ActiveTab is { IsLoading: true } && e.OriginalSource is not TextBox:
                    ActiveCore?.Stop(); break;
                default: handled = false; break;
            }
        }
        else handled = false;

        if (handled) e.Handled = true;
    }

    /// <summary>Проверка перетаскивания без мыши: dragsim:откуда:куда (номера в полосе вкладок) — курсор «едет» шагами по 8 пикселей.</summary>
    void DragSimulation(string cmd)
    {
        var parts = cmd.Split(':');
        if (parts.Length < 3 || _current == null || !int.TryParse(parts[1], out int from) || !int.TryParse(parts[2], out int to)) return;
        var host = _settings.TabsPosition == "top" ? TopTabs : SideTabs;
        if (from >= _current.Strip.Count || to >= _current.Strip.Count || _current.Strip[from] is not BrowserTab tab) return;
        bool horizontal = host == TopTabs;
        Point Center(int i)
        {
            var c = (FrameworkElement)host.ItemContainerGenerator.ContainerFromIndex(i);
            var r = c.TransformToAncestor(host).TransformBounds(new Rect(c.RenderSize));
            return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
        }
        var a = Center(from);
        var b = Center(to);
        b = horizontal ? new Point(b.X + (to > from ? 12 : -12), b.Y) : new Point(b.X, b.Y + (to > from ? 6 : -6));
        _dragHost = host;
        _dragTab = tab;
        _dragging = true;
        int steps = (int)Math.Max(1, (horizontal ? Math.Abs(b.X - a.X) : Math.Abs(b.Y - a.Y)) / 8);
        for (int s = 1; s <= steps; s++)
        {
            var p = new Point(a.X + (b.X - a.X) * s / steps, a.Y + (b.Y - a.Y) * s / steps);
            DragTo(host, p, horizontal);
            host.UpdateLayout();
        }
        EndDrag();
        QueueSave();
    }

    /// <summary>Команды для проверки без мыши и клавиатуры: SneznyjBrowser.exe --cmd=expand-panel --no-activate.</summary>
    void RunTestCommand(string cmd)
    {
        switch (cmd)
        {
            case "expand-panel": _holdExpanded = true; ExpandPanelOverlay(); break;
            case "collapse-panel": _holdExpanded = false; CollapsePanelOverlay(immediate: false); break;
            case "menu": ShowMainMenu(); break;
            case "palette": OpenPalette(); break;
            case "bookmark": ShowBookmarkPopup(); break;
            case "downloads-popup": ShowDownloadsPopup(); break;
            case "group-active": if (_current?.ActiveTab is { } t) NewGroup(t, ask: false); break;
            case "pin-active": if (_current?.ActiveTab is { } p) TogglePin(p); break;
            case "toast": ShowToast("Проверка подсказки", "Открыть", () => { }); break;
            case "screenshot": TakeScreenshot(fullPage: false); break;
            case "tabmenu":
                if (_current?.ActiveTab is { } m)
                {
                    var host = _settings.TabsPosition == "top" ? TopTabs : SideTabs;
                    var c = host.ItemContainerGenerator.ContainerFromItem(m) as UIElement;
                    ShowTabMenu(m, host, c);
                }
                break;
            case "import-yandex": ImportBookmarks("yandex"); break;
            case "newtab": OpenNewTab(); break;
            case "reader": OpenReader(); break;
            case var ds when ds.StartsWith("dragsim:"): DragSimulation(ds); break;
            case "find": StartFind(); break;
            case var f when f.StartsWith("findtext:"): FindBar.Visibility = Visibility.Visible; FindBox.Text = f[9..]; RunFind(); break;
            case "close-others": if (_current?.ActiveTab is { } keep) foreach (var other in _current.Tabs.Where(x => x != keep).ToList()) CloseTab(other); break;
            case "collapse-groups": foreach (var g in _current?.Groups.ToList() ?? new()) if (!g.Collapsed) ToggleGroup(g); break;
            default:
                if (cmd.StartsWith("set:") && cmd.IndexOf('=') is int eq and > 4)
                    ApplySetting(cmd[4..eq], JsonDocument.Parse(JsonSerializer.Serialize(cmd[(eq + 1)..])).RootElement);
                else if (cmd.StartsWith("open:")) OpenInternal(cmd[5..]);
                else if (cmd.StartsWith("suggest:")) ShowSuggestionsFor(cmd[8..]);
                break;
        }
    }
}

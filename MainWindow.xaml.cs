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

public partial class MainWindow : Window
{
    readonly string[] _startupArgs;
    readonly SettingsData _settings;
    readonly SessionData _session;
    readonly AdBlocker _blocker;
    readonly HistoryStore _history;
    readonly ObservableCollection<Workspace> _workspaces = new();
    readonly ObservableCollection<PanelApp> _panelApps = new();
    readonly List<TabData> _closed;
    readonly DispatcherTimer _saveTimer;
    readonly DispatcherTimer _sleepTimer;
    readonly DispatcherTimer _toastTimer;
    readonly DispatcherTimer _updateTimer;
    readonly Updater _updater;
    UpdateManifest? _update;
    bool _updating;

    CoreWebView2Environment? _env;
    Workspace? _current;
    PanelApp? _panelApp;
    bool _panelOpen;
    bool _syncingSelection;
    bool _ready;
    bool _isFullscreen, _manualFullscreen;
    Rect _preFullscreenBounds;
    WindowState _preFullscreenState;

    static readonly System.Drawing.Color WebBackground = System.Drawing.Color.FromArgb(255, 11, 15, 20);

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
        _updater = new Updater(_settings);

        LogoPath.Data = Snowflake.Create(20);
        var saved = _session.Workspaces is { Count: > 0 } ? _session.Workspaces : Defaults.Workspaces();
        foreach (var w in saved) _workspaces.Add(Workspace.FromData(w));
        foreach (var p in _settings.PanelApps!) _panelApps.Add(new PanelApp(p));
        WorkspaceList.ItemsSource = _workspaces;
        PanelAppList.ItemsSource = _panelApps;
        RestorePlacement();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSession(); };
        _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _sleepTimer.Tick += (_, _) => SleepHiddenTabs();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _updateTimer.Tick += (_, _) => OnUpdateTimer();

        SourceInitialized += (_, _) => Native.StyleWindow(this);
        StateChanged += (_, _) => OnWindowStateChanged();
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnKey;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKey));
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

        StartPage.Write(_workspaces, _settings);
        _ready = true;
        OnWindowStateChanged();
        SwitchWorkspace(_workspaces.FirstOrDefault(w => w.Id == _session.ActiveWorkspace) ?? _workspaces[0]);
        OpenFromOutside(_startupArgs);
        if (_session.PanelOpen)
            OpenPanel(_panelApps.FirstOrDefault(a => a.Id == _session.PanelApp) ?? _panelApps.FirstOrDefault());
        _sleepTimer.Start();
        if (_startupArgs.Contains("--updated")) ShowToast($"Браузер обновлён до версии {Updater.CurrentText}");
        StartUpdates();
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSession();
        _history.Save();
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
        _syncingSelection = true;
        TabList.ItemsSource = ws.Tabs;
        _syncingSelection = false;
        var tab = focus ?? ws.ActiveTab ?? ws.Tabs.FirstOrDefault() ?? NewTab(ws, "");
        ActivateTab(tab);
        if (UrlTools.IsBlankOrStart(tab.Url)) FocusAddress();
    }

    void UpdateWorkspaceHeader()
    {
        if (_current == null) return;
        WsTitle.Text = _current.Name.ToUpperInvariant();
        int n = _current.Tabs.Count;
        WsCount.Text = n == 1 ? "1 вкладка" : n is >= 2 and <= 4 ? $"{n} вкладки" : $"{n} вкладок";
    }

    Workspace? WorkspaceOf(object sender) => (sender as FrameworkElement)?.DataContext as Workspace;

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

    BrowserTab NewTab(Workspace ws, string url, string? title = null, int? index = null, bool isPrivate = false)
    {
        var tab = new BrowserTab { Workspace = ws, Url = url, IsPrivate = isPrivate };
        tab.Title = title ?? (UrlTools.IsBlankOrStart(url)
            ? (isPrivate ? "Приватная вкладка" : "Новая вкладка")
            : UrlTools.SiteKey(UrlTools.Host(url)));
        if (!isPrivate && !UrlTools.IsBlankOrStart(url)) tab.Favicon = Favicons.Load(UrlTools.Host(url));
        if (index is int i && i >= 0 && i <= ws.Tabs.Count) ws.Tabs.Insert(i, tab);
        else ws.Tabs.Add(tab);
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
        if (tab != null && UrlTools.IsBlankOrStart(tab.Url)) Navigate(tab, url);
        else ActivateTab(NewTab(_current, url, null, tab != null ? _current.Tabs.IndexOf(tab) + 1 : null, tab?.IsPrivate == true));
    }

    void ActivateTab(BrowserTab tab, bool createView = true)
    {
        if (!_ready) return;
        if (tab.Workspace != _current) { SwitchWorkspace(tab.Workspace, tab); return; }
        var ws = tab.Workspace;
        var prev = ws.ActiveTab;
        ws.ActiveTab = tab;
        if (prev != null && prev != tab) HideView(prev);
        if (tab.View != null) ShowView(tab);
        else if (createView) _ = EnsureView(tab, navigate: true);

        _syncingSelection = true;
        TabList.SelectedItem = tab;
        TabList.ScrollIntoView(tab);
        _syncingSelection = false;

        UpdateWorkspaceHeader();
        UpdateChrome();
        QueueSave();
    }

    void CloseTab(BrowserTab tab)
    {
        var ws = tab.Workspace;
        int index = ws.Tabs.IndexOf(tab);
        if (index < 0) return;
        if (!tab.IsPrivate && UrlTools.IsWeb(tab.Url))
        {
            _closed.Add(new TabData { Url = tab.Url, Title = tab.Title, WorkspaceId = ws.Id });
            if (_closed.Count > 40) _closed.RemoveAt(0);
        }
        bool wasActive = ws.ActiveTab == tab;
        ws.Tabs.RemoveAt(index);
        DisposeView(tab);
        if (wasActive)
        {
            ws.ActiveTab = null;
            if (ws == _current)
            {
                var next = ws.Tabs.Count > 0 ? ws.Tabs[Math.Min(index, ws.Tabs.Count - 1)] : NewTab(ws, "");
                ActivateTab(next);
                if (UrlTools.IsBlankOrStart(next.Url)) FocusAddress();
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
        ActivateTab(NewTab(ws, data.Url, data.Title));
    }

    void DuplicateTab(BrowserTab tab)
    {
        var ws = tab.Workspace;
        ActivateTab(NewTab(ws, tab.Url, tab.Title, ws.Tabs.IndexOf(tab) + 1, tab.IsPrivate));
    }

    void MoveTab(BrowserTab tab, Workspace target)
    {
        if (tab.Workspace == target) return;
        var source = tab.Workspace;
        bool wasActive = source.ActiveTab == tab;
        int index = source.Tabs.IndexOf(tab);
        source.Tabs.Remove(tab);
        if (wasActive)
        {
            source.ActiveTab = null;
            HideView(tab);
            if (source == _current)
                ActivateTab(source.Tabs.Count > 0 ? source.Tabs[Math.Min(index, source.Tabs.Count - 1)] : NewTab(source, ""));
        }
        tab.Workspace = target;
        target.Tabs.Add(tab);
        UpdateWorkspaceHeader();
        ShowToast($"Вкладка перенесена в «{target.Name}»");
        QueueSave();
    }

    void CycleTab(int step)
    {
        if (_current?.ActiveTab is not { } tab || _current.Tabs.Count < 2) return;
        int i = (_current.Tabs.IndexOf(tab) + step + _current.Tabs.Count) % _current.Tabs.Count;
        ActivateTab(_current.Tabs[i]);
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

    void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || TabList.SelectedItem is not BrowserTab tab || tab == _current?.ActiveTab) return;
        ActivateTab(tab);
        if (UrlTools.IsBlankOrStart(tab.Url)) FocusAddress(); else FocusPage();
    }

    static BrowserTab? TabFromSource(object? source)
    {
        var d = source as DependencyObject;
        while (d != null)
        {
            if (d is FrameworkElement { DataContext: BrowserTab t }) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    void TabList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && TabFromSource(e.OriginalSource) is { } tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
    }

    void TabList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TabFromSource(e.OriginalSource) is not { } tab) return;
        var menu = new ContextMenu();
        void Add(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Закрыть вкладку", () => CloseTab(tab));
        Add("Закрыть остальные", () =>
        {
            foreach (var t in tab.Workspace.Tabs.Where(t => t != tab).ToList()) CloseTab(t);
        }, tab.Workspace.Tabs.Count > 1);
        Add("Дублировать", () => DuplicateTab(tab));
        Add("Добавить на стартовую", () => AddToStart(tab), UrlTools.IsWeb(tab.Url) && !tab.IsPrivate);
        menu.Items.Add(new Separator());
        foreach (var ws in _workspaces.Where(w => w != tab.Workspace))
            Add($"Перенести в «{ws.Name}»", () => MoveTab(tab, ws), !tab.IsPrivate);
        menu.PlacementTarget = TabList;
        menu.IsOpen = true;
        e.Handled = true;
    }

    void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTab tab) CloseTab(tab);
        e.Handled = true;
    }

    void TabMute_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrowserTab { View.CoreWebView2: { } core })
            core.IsMuted = !core.IsMuted;
        e.Handled = true;
    }

    void NewTab_Click(object sender, RoutedEventArgs e) => OpenNewTab();

    // ───────────────────────── движок страниц ─────────────────────────

    async Task EnsureView(BrowserTab tab, bool navigate)
    {
        if (tab.View != null || _env == null) return;
        var view = new WebView2 { DefaultBackgroundColor = WebBackground };
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
        if (navigate) Navigate(tab, tab.Url);
        if (tab == _current?.ActiveTab) UpdateChrome();
    }

    void Configure(CoreWebView2 core, BrowserTab? tab, PanelApp? app)
    {
        var s = core.Settings;
        s.AreHostObjectsAllowed = false;          // страницы не достают до программы
        s.IsWebMessageEnabled = tab != null;      // сообщения принимаются только от своей стартовой
        s.AreDevToolsEnabled = true;
        s.IsStatusBarEnabled = true;
        s.IsReputationCheckingRequired = true;    // SmartScreen: защита от фишинга и вирусов
        s.IsGeneralAutofillEnabled = _settings.Autofill;
        s.IsPasswordAutosaveEnabled = _settings.SavePasswords && tab?.IsPrivate != true;
        try { core.Profile.PreferredTrackingPreventionLevel = _settings.TrackingLevel(); } catch { }

        core.SetVirtualHostNameToFolderMapping(UrlTools.StartHost, AppPaths.Start, CoreWebView2HostResourceAccessKind.Deny);
        foreach (var pattern in _blocker.Patterns)
            core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, e) => OnBlockable(core, e, tab);
        core.NewWindowRequested += (_, e) => OnNewWindow(e, tab);
        core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);
        if (tab == null) return;

        core.NavigationStarting += (_, e) =>
        {
            if (!e.IsRedirected) tab.Blocked = 0;
            tab.PendingUrl = e.Uri;
            tab.IsLoading = true;
            if (tab == _current?.ActiveTab) UpdateChrome();
        };
        core.NavigationCompleted += (_, e) =>
        {
            tab.IsLoading = false;
            tab.PendingUrl = null;
            if (e.IsSuccess && !tab.IsPrivate) _history.Add(core.Source, core.DocumentTitle);
            if (tab == _current?.ActiveTab) UpdateChrome();
        };
        core.SourceChanged += (_, _) =>
        {
            tab.Url = core.Source;
            if (UrlTools.IsStart(tab.Url))
            {
                tab.Title = tab.IsPrivate ? "Приватная вкладка" : "Новая вкладка";
                tab.Favicon = null;
            }
            if (tab == _current?.ActiveTab) UpdateChrome();
            QueueSave();
        };
        core.DocumentTitleChanged += (_, _) =>
        {
            if (UrlTools.IsStart(core.Source)) return;
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
        core.WebMessageReceived += (_, e) => OnStartMessage(e);
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                tab.IsLoading = false;
                tab.Title = "Страница упала — нажмите F5";
            }
        };
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
        int index = opener != null ? ws.Tabs.IndexOf(opener) + 1 : ws.Tabs.Count;
        bool isPrivate = opener?.IsPrivate == true;

        // Ctrl+клик — открыть в фоне, страница загрузится при переходе на вкладку.
        if (Native.IsKeyDown(0x11))
        {
            e.Handled = true;
            NewTab(ws, e.Uri, null, index, isPrivate);
            ShowToast("Открыто в фоновой вкладке");
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var tab = NewTab(ws, e.Uri, null, index, isPrivate);
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

    void OnStartMessage(CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!UrlTools.IsStart(e.Source)) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var cmd = root.GetProperty("cmd").GetString();
            var ws = _workspaces.FirstOrDefault(w => w.Id == root.GetProperty("ws").GetString());
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
            else return;
            StartPage.Write(_workspaces, _settings);
            QueueSave();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    void Navigate(BrowserTab tab, string url)
    {
        if (UrlTools.IsBlankOrStart(url))
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

    CoreWebView2? AnyNormalCore() =>
        _workspaces.SelectMany(w => w.Tabs).FirstOrDefault(t => !t.IsPrivate && t.View?.CoreWebView2 != null)?.View?.CoreWebView2
        ?? _panelApps.FirstOrDefault(a => a.View?.CoreWebView2 != null)?.View?.CoreWebView2;

    // ───────────────────────── верхняя панель ─────────────────────────

    void UpdateChrome()
    {
        var tab = _current?.ActiveTab;
        if (tab == null) return;
        if (!AddressBox.IsKeyboardFocusWithin) AddressBox.Text = UrlTools.Display(tab.Url);
        UpdatePlaceholder();

        var url = tab.Url ?? "";
        if (UrlTools.IsBlankOrStart(url))
        {
            SecurityIcon.Text = "";
            SecurityIcon.Foreground = (Brush)FindResource("MutedBrush");
            SecurityIcon.ToolTip = null;
        }
        else if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            SecurityIcon.Text = "";
            SecurityIcon.Foreground = (Brush)FindResource("MutedBrush");
            SecurityIcon.ToolTip = "Соединение защищено (HTTPS)";
        }
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            SecurityIcon.Text = "";
            SecurityIcon.Foreground = (Brush)FindResource("WarnBrush");
            SecurityIcon.ToolTip = "Соединение не защищено: не вводите здесь пароли и данные карт";
        }
        else
        {
            SecurityIcon.Text = "";
            SecurityIcon.Foreground = (Brush)FindResource("MutedBrush");
            SecurityIcon.ToolTip = null;
        }

        ReloadBtn.Content = tab.IsLoading ? "" : "";
        ReloadBtn.ToolTip = tab.IsLoading ? "Остановить (Esc)" : "Обновить (F5)";
        LoadBar.Visibility = tab.IsLoading ? Visibility.Visible : Visibility.Hidden;
        PrivateBadge.Visibility = tab.IsPrivate ? Visibility.Visible : Visibility.Collapsed;
        if (!AddressBox.IsKeyboardFocusWithin)
            AddressBorder.BorderBrush = tab.IsPrivate ? (Brush)FindResource("PrivateBrush") : Brushes.Transparent;
        UpdateTitle();
        UpdateNavButtons();
        UpdateShield();
    }

    void UpdateTitle()
    {
        var tab = _current?.ActiveTab;
        if (tab == null) return;
        Title = (UrlTools.IsBlankOrStart(tab.Url) ? "Новая вкладка" : tab.Title) + " — Снежный браузер";
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
            ShieldIcon.Foreground = (Brush)FindResource("MutedBrush");
            ShieldCount.Text = "выкл";
            ShieldBtn.ToolTip = "Блокировщик рекламы выключен везде. Нажмите, чтобы включить";
        }
        else if (UrlTools.IsWeb(tab?.Url) && _settings.IsAllowed(site))
        {
            ShieldIcon.Foreground = (Brush)FindResource("MutedBrush");
            ShieldCount.Text = "пауза";
            ShieldBtn.ToolTip = $"На {site} блокировщик отключён. Нажмите, чтобы включить";
        }
        else
        {
            int n = tab?.Blocked ?? 0;
            ShieldIcon.Foreground = (Brush)FindResource("GoodBrush");
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

    void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Enter)
        {
            var url = UrlTools.ToUrl(AddressBox.Text, _settings.SearchEngine);
            e.Handled = true;
            if (url.Length == 0 || _current?.ActiveTab is not { } tab) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                ActivateTab(NewTab(_current, url, null, _current.Tabs.IndexOf(tab) + 1, tab.IsPrivate));
            else
                Navigate(tab, url);
            FocusPage();
        }
        else if (key == Key.Escape)
        {
            AddressBox.Text = UrlTools.Display(_current?.ActiveTab?.Url);
            e.Handled = true;
            FocusPage();
        }
    }

    void AddressBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePlaceholder();

    void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBox.SelectAll();
        AddressBorder.BorderBrush = (Brush)FindResource("AccentBrush");
    }

    void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBorder.BorderBrush = Brushes.Transparent;
        UpdateChrome();
    }

    void AddressBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (AddressBox.IsKeyboardFocusWithin) return;
        AddressBox.Focus();
        e.Handled = true;
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

    void Reload()
    {
        var tab = _current?.ActiveTab;
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

    void AddToStart_Click(object sender, RoutedEventArgs e)
    {
        if (_current?.ActiveTab is { } tab) AddToStart(tab);
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

    void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ───────────────────────── боковая панель ─────────────────────────

    void PanelToggle_Click(object sender, RoutedEventArgs e) => TogglePanel();
    void PanelClose_Click(object sender, RoutedEventArgs e) => ClosePanel();

    void PanelApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PanelApp app) OpenPanel(app);
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
            if (a.View != null) a.View.Visibility = a == app ? Visibility.Visible : Visibility.Collapsed;
        }
        QueueSave();
        if (app.View != null) return;

        var view = new WebView2 { DefaultBackgroundColor = WebBackground };
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
        ShowPanelColumns(false);
        QueueSave();
    }

    void ShowPanelColumns(bool show)
    {
        PanelRoot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PanelSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SplitterCol.Width = new GridLength(show ? 8 : 0);
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
        if (_updating) return;
        _update = m;
        UpdateHeading.Text = $"Новая версия {m.Version}";
        UpdateNotes.Text = string.IsNullOrWhiteSpace(m.Notes) ? $"Сейчас у вас {Updater.CurrentText}." : m.Notes;
        UpdateNotes.ToolTip = m.Notes.Length > 0 ? m.Notes : null;
        UpdateProgress.Visibility = Visibility.Collapsed;
        UpdateButtons.Visibility = Visibility.Visible;
        UpdateNowBtn.Content = "Обновить";
        UpdateCard.Visibility = Visibility.Visible;
    }

    void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        _updater.Postpone();
        UpdateCard.Visibility = Visibility.Collapsed;
        ShowToast("Напомню завтра. Обновиться сразу — Ctrl+K → «Проверить обновления»");
    }

    async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } m || _updating) return;
        _updating = true;
        UpdateButtons.Visibility = Visibility.Collapsed;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateTrack.Visibility = UpdateFill.Visibility = Visibility.Visible;
        UpdateStatus.Margin = new Thickness(0, 10, 0, 0);
        UpdateFill.Width = 0;
        double mb = m.Size / 1048576.0;
        UpdateStatus.Text = $"Скачиваю… 0% из {mb:0} МБ";
        var progress = new Progress<double>(p =>
        {
            UpdateFill.Width = UpdateTrack.ActualWidth * Math.Clamp(p, 0, 1);
            UpdateStatus.Text = $"Скачиваю… {p * 100:0}% из {mb:0} МБ";
        });
        try
        {
            var path = await _updater.DownloadAsync(m, progress);
            UpdateFill.Width = UpdateTrack.ActualWidth;
            UpdateStatus.Text = "Подпись и контрольная сумма сходятся. Перезапускаю браузер, вкладки сохранятся…";
            await Task.Delay(900);
            SaveSession();
            Updater.RunInstaller(path, noActivate: !ShowActivated);
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _updating = false;
            UpdateTrack.Visibility = UpdateFill.Visibility = Visibility.Collapsed;
            UpdateStatus.Margin = new Thickness(0);
            UpdateStatus.Text = "Не получилось: " + ex.Message;
            UpdateButtons.Visibility = Visibility.Visible;
            UpdateNowBtn.Content = "Повторить";
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
        if (_isFullscreen) return;
        RootGrid.Margin = WindowState == WindowState.Maximized ? Native.MaximizedOverhang(this) : new Thickness(0);
    }

    void SetFullscreen(bool on, bool manual = false)
    {
        if (on == _isFullscreen) return;
        _isFullscreen = on;
        _manualFullscreen = on && manual;
        var chrome = new WindowChrome
        {
            CaptionHeight = on ? 0 : 46,
            ResizeBorderThickness = new Thickness(on ? 0 : 6),
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0),
        };
        if (on)
        {
            _preFullscreenState = WindowState;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            _preFullscreenBounds = new Rect(Left, Top, Width, Height);
            TopRow.Height = new GridLength(0);
            SidebarCol.Width = new GridLength(0);
            Sidebar.Visibility = Visibility.Collapsed;
            BrandPanel.Visibility = Visibility.Collapsed;
            TopBar.Visibility = Visibility.Collapsed;
            ContentGrid.Margin = new Thickness(0);
            PageBorder.BorderThickness = new Thickness(0);
            RootGrid.Margin = new Thickness(0);
            ShowPanelColumns(false);
            WindowChrome.SetWindowChrome(this, chrome);
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
            TopRow.Height = new GridLength(46);
            SidebarCol.Width = new GridLength(248);
            Sidebar.Visibility = Visibility.Visible;
            BrandPanel.Visibility = Visibility.Visible;
            TopBar.Visibility = Visibility.Visible;
            ContentGrid.Margin = new Thickness(0, 0, 8, 8);
            PageBorder.BorderThickness = new Thickness(1);
            WindowChrome.SetWindowChrome(this, chrome);
            Native.StyleWindow(this);
            Left = _preFullscreenBounds.Left;
            Top = _preFullscreenBounds.Top;
            Width = _preFullscreenBounds.Width;
            Height = _preFullscreenBounds.Height;
            if (_preFullscreenState == WindowState.Maximized) WindowState = WindowState.Maximized;
            OnWindowStateChanged();
            if (_panelOpen) ShowPanelColumns(true);
            if (ActiveCore is { ContainsFullScreenElement: true } core)
                _ = core.ExecuteScriptAsync("document.exitFullscreen && document.exitFullscreen()");
        }
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
                case Key.W:
                case Key.F4: CloseActiveTab(); break;
                case Key.L: FocusAddress(); break;
                case Key.K: OpenPalette(); break;
                case Key.D: AddToStart_Click(this, new RoutedEventArgs()); break;
                case Key.B when shift: TogglePanel(); break;
                case Key.Tab: CycleTab(shift ? -1 : 1); break;
                case Key.PageDown: CycleTab(1); break;
                case Key.PageUp: CycleTab(-1); break;
                case Key.R: Reload(); break;
                case >= Key.D1 and <= Key.D9 when !shift: SelectTabByNumber(key - Key.D1); break;
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
                case Key.Escape when _manualFullscreen: SetFullscreen(false); break;
                case Key.Escape when _current?.ActiveTab is { IsLoading: true } && e.OriginalSource is not TextBox:
                    ActiveCore?.Stop(); break;
                default: handled = false; break;
            }
        }
        else handled = false;

        if (handled) e.Handled = true;
    }

    // ───────────────────────── палитра команд Ctrl+K ─────────────────────────

    void Palette_Click(object sender, RoutedEventArgs e) => OpenPalette();

    void OpenPalette()
    {
        if (!_ready) return;
        new PaletteWindow(this, BuildPaletteItems).Show();
    }

    List<PaletteItem> BuildPaletteItems(string query)
    {
        var q = query.Trim();
        var match = TextMatch.Matcher(q);
        var engineName = UrlTools.EngineName(_settings.SearchEngine);
        var result = new List<PaletteItem>();
        int found = 0;

        if (q.Length > 0 && UrlTools.LooksLikeAddress(q))
        {
            var url = UrlTools.ToUrl(q, _settings.SearchEngine);
            result.Add(new PaletteItem { Glyph = "", Title = $"Открыть {url}", Kind = "Адрес", Run = () => OpenSmart(url) });
        }

        foreach (var ws in _workspaces)
        foreach (var t in ws.Tabs)
        {
            bool show = q.Length == 0 ? ws == _current && t != ws.ActiveTab : match(t.Title + " " + t.Url + " " + ws.Name);
            if (!show) continue;
            var tab = t;
            result.Add(new PaletteItem
            {
                Glyph = tab.IsPrivate ? "" : "",
                Icon = tab.Favicon,
                Title = tab.Title,
                Subtitle = UrlTools.Display(tab.Url),
                Kind = ws == _current ? "Вкладка" : $"Вкладка · {ws.Name}",
                Run = () => { ActivateTab(tab); FocusPage(); },
            });
            found++;
        }

        if (q.Length > 0)
        {
            foreach (var ws in _workspaces)
            foreach (var l in ws.Links.Where(l => match(l.Title + " " + l.Url + " " + ws.Name)))
            {
                var link = l;
                result.Add(new PaletteItem
                {
                    Glyph = "",
                    Icon = Favicons.Load(UrlTools.Host(link.Url)),
                    Title = link.Title,
                    Subtitle = link.Url,
                    Kind = $"Стартовая · {ws.Name}",
                    Run = () => OpenSmart(link.Url),
                });
                found++;
            }
        }

        foreach (var c in Commands())
        {
            if (q.Length == 0 ? !c.Featured : !match(c.Title + " " + c.Keywords)) continue;
            result.Add(c);
            found++;
        }

        var history = q.Length > 0 ? _history.Search(match, 8) : _history.Recent(5);
        foreach (var h in history)
        {
            var entry = h;
            result.Add(new PaletteItem
            {
                Glyph = "",
                Icon = Favicons.Load(UrlTools.Host(entry.Url)),
                Title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title,
                Subtitle = entry.Url,
                Kind = "История",
                Run = () => OpenSmart(entry.Url),
            });
            found++;
        }

        if (q.Length > 0 && !UrlTools.LooksLikeAddress(q))
        {
            var search = new PaletteItem
            {
                Glyph = "",
                Title = $"Искать «{q}»",
                Kind = engineName,
                Run = () => OpenSmart(UrlTools.SearchUrl(_settings.SearchEngine, q)),
            };
            if (found == 0) result.Insert(0, search); else result.Add(search);
        }
        return result;
    }

    IEnumerable<PaletteItem> Commands()
    {
        PaletteItem C(string glyph, string title, string hint, string keywords, Action run, bool featured = false) =>
            new() { Glyph = glyph, Title = title, Hint = hint, Kind = "Команда", Keywords = keywords, Run = run, Featured = featured };

        var tab = _current?.ActiveTab;
        var site = UrlTools.SiteKey(UrlTools.Host(tab?.Url));

        yield return C("", "Новая вкладка", "Ctrl+T", "new tab открыть", () => OpenNewTab());
        yield return C("", "Новая приватная вкладка", "Ctrl+Shift+N", "private incognito инкогнито приват", () => OpenNewTab(true), true);
        yield return C("", "Закрыть вкладку", "Ctrl+W", "close", CloseActiveTab);
        yield return C("", "Вернуть закрытую вкладку", "Ctrl+Shift+T", "reopen restore восстановить", ReopenClosedTab, _closed.Count > 0);
        yield return C("", "Добавить страницу на стартовую", "Ctrl+D", "bookmark закладка избранное", () => { if (tab != null) AddToStart(tab); });
        yield return C("", _panelOpen ? "Скрыть боковую панель" : "Показать боковую панель", "Ctrl+Shift+B", "panel sidebar", TogglePanel);
        foreach (var app in _panelApps)
        {
            var a = app;
            yield return C(a.Glyph, $"Боковая панель: {a.Name}", "", "panel sidebar " + a.Url, () => OpenPanel(a), true);
        }
        int n = 1;
        foreach (var ws in _workspaces)
        {
            var w = ws;
            yield return C(w.Glyph, $"Пространство: {w.Name}", n <= 9 ? $"Alt+{n}" : "", "workspace перейти", () => SwitchWorkspace(w), w != _current);
            n++;
        }
        yield return C("", "Новое пространство", "", "workspace создать добавить", AddWorkspace);
        if (UrlTools.IsWeb(tab?.Url) && _settings.AdBlock)
        {
            yield return _settings.IsAllowed(site)
                ? C("", $"Включить блокировщик на {site}", "", "adblock реклама", () => Shield_Click(this, new RoutedEventArgs()))
                : C("", $"Отключить блокировщик на {site}", "", "adblock реклама разрешить", () => Shield_Click(this, new RoutedEventArgs()));
        }
        yield return C("", _settings.AdBlock ? "Выключить блокировщик рекламы везде" : "Включить блокировщик рекламы", "",
            "adblock реклама трекеры", () =>
            {
                _settings.AdBlock = !_settings.AdBlock;
                SaveSettings();
                UpdateShield();
                ShowToast(_settings.AdBlock ? "Блокировщик включён" : "Блокировщик выключен");
                Reload();
            });
        if (tab?.View?.CoreWebView2 is { } core && tab.ShowAudio)
            yield return C(tab.IsMuted ? "" : "", tab.IsMuted ? "Включить звук вкладки" : "Выключить звук вкладки", "", "mute звук",
                () => core.IsMuted = !core.IsMuted);
        foreach (var (id, engine) in UrlTools.Engines)
        {
            var key = id;
            yield return C("", $"Поиск по умолчанию: {engine.Name}" + (id == _settings.SearchEngine ? "  ✓" : ""), "",
                "search engine поисковик", () =>
                {
                    _settings.SearchEngine = key;
                    SaveSettings();
                    StartPage.Write(_workspaces, _settings);
                    ShowToast($"Поиск по умолчанию: {UrlTools.EngineName(key)}");
                });
        }
        yield return C("", "Очистить историю и кэш", "", "clear history cache удалить", ClearHistoryAndCache);
        yield return C("", "Удалить все данные сайтов (выйти со всех сайтов)", "", "clear cookies пароли выйти", ClearAllSiteData);
        yield return C("", "Инструменты разработчика", "F12", "devtools console консоль", () => ActiveCore?.OpenDevToolsWindow());
        yield return C("", "Открыть папку данных браузера", "", "data folder профиль", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true }));
        yield return C("", "Открыть файл настроек", "", "settings config", () =>
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppPaths.SettingsFile}\"") { UseShellExecute = true }));
        yield return C("", "Полноэкранный режим", "F11", "fullscreen", () => SetFullscreen(!_isFullscreen, manual: true));
        yield return C("", $"Проверить обновления · версия {Updater.CurrentText}", "", "update обновить обновление версия о программе about",
            () => CheckForUpdates(manual: true));
    }

    async void ClearHistoryAndCache()
    {
        _history.Clear();
        _closed.Clear();
        if (AnyNormalCore() is { } core)
        {
            try
            {
                await core.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.BrowsingHistory | CoreWebView2BrowsingDataKinds.DiskCache |
                    CoreWebView2BrowsingDataKinds.DownloadHistory | CoreWebView2BrowsingDataKinds.CacheStorage);
            }
            catch { }
        }
        SaveSession();
        ShowToast("История и кэш очищены");
    }

    async void ClearAllSiteData()
    {
        if (!InputDialog.Confirm(this, "Удалить все данные сайтов?",
                "Вы выйдете со всех сайтов, удалятся сохранённые пароли, история и кэш. Вкладки и стартовые останутся.", "Удалить"))
            return;
        _history.Clear();
        _closed.Clear();
        if (AnyNormalCore() is { } core)
        {
            try { await core.Profile.ClearBrowsingDataAsync(); } catch { }
        }
        SaveSession();
        ShowToast("Все данные сайтов удалены");
        Reload();
    }
}

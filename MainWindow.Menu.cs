using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace SneznyjBrowser;

/// <summary>Главное меню «≡», палитра Ctrl+K и команды страницы: масштаб, поиск, печать, скриншот, перевод.</summary>
public partial class MainWindow
{
    static readonly double[] ZoomSteps = { 0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5 };
    bool _applyingZoom;

    void Menu_Click(object sender, RoutedEventArgs e) => ShowMainMenu();
    void Bookmarks_Click(object sender, RoutedEventArgs e) => OpenInternal("bookmarks");
    void History_Click(object sender, RoutedEventArgs e) => OpenInternal("history");
    void Downloads_Click(object sender, RoutedEventArgs e) => OpenInternal("downloads");
    void Settings_Click(object sender, RoutedEventArgs e) => OpenInternal("settings");

    void ShowMainMenu()
    {
        var tab = _current?.ActiveTab;
        bool web = UrlTools.IsWeb(tab?.Url);
        var m = new ContextMenu { MinWidth = 300 };

        m.Items.Add(MenuItemFor("Новая вкладка", "", () => OpenNewTab(), gesture: "Ctrl+T"));
        m.Items.Add(MenuItemFor("Новая приватная вкладка", "", () => OpenNewTab(true), gesture: "Ctrl+Shift+N"));
        m.Items.Add(MenuItemFor("Новое пространство", "", AddWorkspace));
        m.Items.Add(new Separator());

        var bookmarks = MenuItemFor("Закладки", "", null);
        bookmarks.Items.Add(MenuItemFor(_bookmarks.FindByUrl(tab?.Url) != null ? "Изменить закладку" : "Добавить в закладки", "",
            ShowBookmarkPopup, web && tab?.IsPrivate != true, "Ctrl+D"));
        var bar = MenuItemFor("Показывать панель закладок", null, () => SetSetting("bookmarksBar", !_settings.BookmarksBar));
        bar.IsChecked = _settings.BookmarksBar;
        bookmarks.Items.Add(bar);
        bookmarks.Items.Add(MenuItemFor("Все закладки", "", () => OpenInternal("bookmarks"), gesture: "Ctrl+Shift+O"));
        bookmarks.Items.Add(MenuItemFor("Перенести из Яндекс Браузера", "", () => ImportBookmarks("yandex"),
            BookmarkStore.FindSourceFile("yandex") != null));
        bookmarks.Items.Add(MenuItemFor("Перенести из Chrome или Edge", null, () => OpenInternal("bookmarks", "import")));
        if (_bookmarks.Bar.Count > 0)
        {
            bookmarks.Items.Add(new Separator());
            foreach (var n in _bookmarks.Bar.Take(25)) bookmarks.Items.Add(BookmarkMenuItem(n));
        }
        m.Items.Add(bookmarks);
        m.Items.Add(MenuItemFor("История", "", () => OpenInternal("history"), gesture: "Ctrl+H"));
        m.Items.Add(MenuItemFor("Загрузки", "", () => OpenInternal("downloads"), gesture: "Ctrl+J"));
        m.Items.Add(new Separator());

        m.Items.Add(ZoomRow());
        m.Items.Add(MenuItemFor("Найти на странице", "", StartFind, tab?.View != null, "Ctrl+F"));
        m.Items.Add(MenuItemFor("Перевести страницу на русский", "", TranslatePage, web));
        m.Items.Add(MenuItemFor("Режим чтения", "", OpenReader, web || UrlTools.InternalPage(tab?.Url) == "reader"));
        var shot = MenuItemFor("Скриншот", "", null, tab?.View != null);
        shot.Items.Add(MenuItemFor("Видимая часть страницы", null, () => TakeScreenshot(fullPage: false), gesture: "Ctrl+Shift+S"));
        shot.Items.Add(MenuItemFor("Вся страница целиком", null, () => TakeScreenshot(fullPage: true)));
        m.Items.Add(shot);
        var more = MenuItemFor("Ещё", "", null);
        more.Items.Add(MenuItemFor("Печать…", "", PrintPage, tab?.View != null, "Ctrl+P"));
        more.Items.Add(MenuItemFor("Сохранить страницу как…", "", SavePageAs, web, "Ctrl+S"));
        more.Items.Add(MenuItemFor("Добавить на стартовую", "", () => { if (tab != null) AddToStart(tab); }, web));
        more.Items.Add(MenuItemFor("Полноэкранный режим", "", () => SetFullscreen(!_isFullscreen, manual: true), gesture: "F11"));
        more.Items.Add(MenuItemFor("Инструменты разработчика", "", () => ActiveCore?.OpenDevToolsWindow(), tab?.View != null, "F12"));
        more.Items.Add(new Separator());
        more.Items.Add(MenuItemFor("Открыть папку данных браузера", "", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true })));
        m.Items.Add(more);
        m.Items.Add(new Separator());

        var look = MenuItemFor("Вид", "", null);
        foreach (var (id, name) in new[] { ("system", "Тема как в Windows"), ("snow", "Тёмная"), ("light", "Светлая") })
        {
            var themeId = id;
            var it = MenuItemFor(name, null, () => SetSetting("theme", themeId));
            it.IsChecked = _settings.Theme == id;
            look.Items.Add(it);
        }
        look.Items.Add(MenuItemFor("Все темы и цвета…", null, () => OpenInternal("settings", "look")));
        look.Items.Add(new Separator());
        var tabsTop = MenuItemFor("Вкладки сверху", null, () => SetSetting("tabsPosition", "top"));
        tabsTop.IsChecked = _settings.TabsPosition == "top";
        var tabsLeft = MenuItemFor("Вкладки слева списком", null, () => SetSetting("tabsPosition", "left"));
        tabsLeft.IsChecked = _settings.TabsPosition == "left";
        look.Items.Add(tabsTop);
        look.Items.Add(tabsLeft);
        look.Items.Add(new Separator());
        foreach (var (id, name) in new[] { ("pinned", "Левая панель закреплена"), ("compact", "Левая панель раскрывается при наведении"), ("hidden", "Левая панель выезжает от края") })
        {
            var mode = id;
            var it = MenuItemFor(name, null, () => SetSetting("leftPanel", mode));
            it.IsChecked = _settings.LeftPanel == id;
            look.Items.Add(it);
        }
        m.Items.Add(look);
        m.Items.Add(MenuItemFor("Настройки", "", () => OpenInternal("settings"), gesture: "Ctrl+,"));
        m.Items.Add(MenuItemFor("Очистить историю…", "", () => OpenInternal("settings", "privacy"), gesture: "Ctrl+Shift+Del"));
        m.Items.Add(MenuItemFor($"О браузере и обновления · {Updater.CurrentText}", "", () => { OpenInternal("settings", "about"); CheckForUpdates(manual: true); }));
        m.Items.Add(new Separator());
        m.Items.Add(MenuItemFor("Выход", "", Close));
        OpenMenu(m, MenuBtn, PlacementMode.Bottom);
        m.HorizontalOffset = -(m.MinWidth - MenuBtn.ActualWidth);
    }

    MenuItem BookmarkMenuItem(BookmarkNode n)
    {
        if (n.IsFolder)
        {
            var folder = MenuItemFor(n.Title, "", null);
            if (n.Children!.Count == 0) folder.Items.Add(MenuItemFor("Пусто", null, null, enabled: false));
            foreach (var c in n.Children.Take(40)) folder.Items.Add(BookmarkMenuItem(c));
            return folder;
        }
        var item = MenuItemFor(n.Title, null, () => OpenSmart(n.Url!));
        item.ToolTip = n.Url;
        if (n.Icon is { } icon) item.Icon = new Image { Source = icon, Width = 16, Height = 16 };
        else
        {
            var glyph = new TextBlock { Text = "", FontSize = 13 };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            item.Icon = glyph;
        }
        return item;
    }

    /// <summary>Строка меню «Масштаб  −  100%  +  ⛶».</summary>
    MenuItem ZoomRow()
    {
        var row = new MenuItem { StaysOpenOnClick = true, Focusable = false };
        var text = new TextBlock { Text = $"{Math.Round(CurrentZoom() * 100)}%", Width = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Button B(string glyph, string tip, Action run)
        {
            var b = new Button { Content = glyph, ToolTip = tip, Width = 32, Height = 28, MinWidth = 0, FontSize = 12 };
            b.SetResourceReference(StyleProperty, "IconButton");
            b.Click += (_, e) => { run(); text.Text = $"{Math.Round(CurrentZoom() * 100)}%"; e.Handled = true; };
            return b;
        }
        var panel = new DockPanel { LastChildFill = false };
        var label = new TextBlock { Text = "Масштаб", VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(label, Dock.Left);
        panel.Children.Add(label);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(B("", "Уменьшить (Ctrl −)", () => ZoomStep(-1)));
        buttons.Children.Add(text);
        buttons.Children.Add(B("", "Увеличить (Ctrl +)", () => ZoomStep(+1)));
        buttons.Children.Add(B("", "Во весь экран (F11)", () => SetFullscreen(true, manual: true)));
        DockPanel.SetDock(buttons, Dock.Right);
        panel.Children.Add(buttons);
        row.Header = panel;
        var icon = new TextBlock { Text = "", FontSize = 13.5 };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        row.Icon = icon;
        return row;
    }

    // ───────────────────────── масштаб по сайтам ─────────────────────────

    double CurrentZoom() => _current?.ActiveTab?.View?.ZoomFactor ?? 1.0;

    void ZoomStep(int dir)
    {
        var z = CurrentZoom();
        double next = dir > 0 ? ZoomSteps.FirstOrDefault(s => s > z + 0.001, ZoomSteps[^1]) : ZoomSteps.LastOrDefault(s => s < z - 0.001, ZoomSteps[0]);
        SetZoom(next);
    }

    void SetZoom(double z)
    {
        if (_current?.ActiveTab?.View is { } view) view.ZoomFactor = z;
    }

    void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    /// <summary>Масштаб запоминается для сайта, как в Яндексе: зашли снова — тот же масштаб.</summary>
    void OnZoomChanged(BrowserTab tab)
    {
        if (tab.View == null) return;
        if (!_applyingZoom && UrlTools.IsWeb(tab.Url) && !tab.IsPrivate)
        {
            var site = UrlTools.SiteKey(UrlTools.Host(tab.Url));
            double z = Math.Round(tab.View.ZoomFactor, 2);
            if (Math.Abs(z - 1) < 0.01) _zoom.Remove(site); else _zoom[site] = z;
            try { Json.Save(AppPaths.ZoomFile, _zoom); } catch { }
        }
        if (tab == _current?.ActiveTab) UpdateZoomChip();
    }

    void ApplySiteZoom(BrowserTab tab)
    {
        if (tab.View == null) return;
        var site = UrlTools.SiteKey(UrlTools.Host(tab.Url));
        double z = UrlTools.IsWeb(tab.Url) && _zoom.TryGetValue(site, out var saved) ? saved : 1.0;
        if (Math.Abs(tab.View.ZoomFactor - z) < 0.001) return;
        _applyingZoom = true;
        try { tab.View.ZoomFactor = z; }
        finally { _applyingZoom = false; }
    }

    void UpdateZoomChip()
    {
        var z = CurrentZoom();
        bool show = Math.Abs(z - 1) > 0.005;
        ZoomChip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) ZoomChip.Content = $"{Math.Round(z * 100)}%";
    }

    // ───────────────────────── поиск, печать, сохранение ─────────────────────────

    // ───────────── своя строка поиска на странице ─────────────

    CoreWebView2? _findCore;
    System.Windows.Threading.DispatcherTimer? _findTimer;

    async void StartFind()
    {
        if (ActiveCore is not { } core) return;
        string selected = "";
        try { selected = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("String(getSelection())")) ?? ""; } catch { }
        FindBar.Visibility = Visibility.Visible;
        selected = selected.Trim();
        if (selected.Length is > 0 and < 200 && !selected.Contains('\n')) FindBox.Text = selected;
        FindBox.Focus();
        Keyboard.Focus(FindBox);
        FindBox.SelectAll();
        RunFind();
    }

    void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FindHint.Visibility = FindBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_findTimer == null)
        {
            _findTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _findTimer.Tick += (_, _) => { _findTimer.Stop(); RunFind(); };
        }
        _findTimer.Stop();
        _findTimer.Start();
    }

    void FindOption_Changed(object sender, RoutedEventArgs e) => RunFind();

    async void RunFind()
    {
        if (_env == null || FindBar.Visibility != Visibility.Visible || ActiveCore is not { } core) return;
        if (_findCore != core)
        {
            StopFind();
            _findCore = core;
            core.Find.MatchCountChanged += (_, _) => UpdateFindCount(core);
            core.Find.ActiveMatchIndexChanged += (_, _) => UpdateFindCount(core);
        }
        var term = FindBox.Text;
        if (term.Trim().Length == 0) { try { core.Find.Stop(); } catch { } FindCount.Text = ""; return; }
        try
        {
            var options = _env.CreateFindOptions();
            options.FindTerm = term;
            options.IsCaseSensitive = FindCase.IsChecked == true;
            options.ShouldHighlightAllMatches = true;
            options.SuppressDefaultFindDialog = true;
            await core.Find.StartAsync(options);
            UpdateFindCount(core);
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    void UpdateFindCount(CoreWebView2 core)
    {
        if (core != _findCore) return;
        try
        {
            int n = core.Find.MatchCount, i = core.Find.ActiveMatchIndex;
            FindCount.Text = FindBox.Text.Trim().Length == 0 ? "" : n == 0 ? "нет" : $"{Math.Max(1, i)} из {n}";
            FindCount.SetResourceReference(TextBlock.ForegroundProperty, n == 0 ? "DangerBrush" : "MutedBrush");
        }
        catch { }
    }

    void FindNext_Click(object sender, RoutedEventArgs e) { try { _findCore?.Find.FindNext(); } catch { } }
    void FindPrev_Click(object sender, RoutedEventArgs e) { try { _findCore?.Find.FindPrevious(); } catch { } }
    void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();

    void FindBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) FindPrev_Click(sender, e); else FindNext_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    void StopFind()
    {
        try { _findCore?.Find.Stop(); } catch { }
        _findCore = null;
        FindCount.Text = "";
    }

    void CloseFind()
    {
        StopFind();
        FindBar.Visibility = Visibility.Collapsed;
        FocusPage();
    }

    void PrintPage()
    {
        try { ActiveCore?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); } catch { }
    }

    async void SavePageAs()
    {
        try { if (ActiveCore is { } core) await core.ShowSaveAsUIAsync(); } catch { }
    }

    /// <summary>Перевод через переводчик Google (адрес страницы уходит переводчику только по этой команде).</summary>
    void TranslatePage()
    {
        if (_current?.ActiveTab is not { } tab || !UrlTools.IsWeb(tab.Url)) return;
        var url = _settings.SearchEngine == "yandex"
            ? $"https://translate.yandex.ru/translate?url={Uri.EscapeDataString(tab.Url)}&lang=auto-ru"
            : $"https://translate.google.com/translate?sl=auto&tl=ru&hl=ru&u={Uri.EscapeDataString(tab.Url)}";
        Navigate(tab, url);
    }

    async void TakeScreenshot(bool fullPage)
    {
        if (_current?.ActiveTab is not { View.CoreWebView2: { } core } tab) return;
        try
        {
            byte[] png;
            if (fullPage)
            {
                var metrics = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}")).RootElement;
                var size = metrics.TryGetProperty("cssContentSize", out var c) ? c : metrics.GetProperty("contentSize");
                double w = Math.Ceiling(size.GetProperty("width").GetDouble()), h = Math.Min(Math.Ceiling(size.GetProperty("height").GetDouble()), 30000);
                var args = JsonSerializer.Serialize(new
                {
                    format = "png", captureBeyondViewport = true, fromSurface = true,
                    clip = new { x = 0, y = 0, width = w, height = h, scale = 1 },
                });
                var result = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", args)).RootElement;
                png = Convert.FromBase64String(result.GetProperty("data").GetString() ?? "");
            }
            else
            {
                using var ms = new MemoryStream();
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                png = ms.ToArray();
            }
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Снежный браузер");
            Directory.CreateDirectory(folder);
            var site = UrlTools.SiteKey(UrlTools.Host(tab.Url));
            if (site.Length == 0) site = "страница";
            var file = Path.Combine(folder, $"Скриншот {site} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
            await File.WriteAllBytesAsync(file, png);
            bool copied = false;
            if (!fullPage || png.Length < 20_000_000)
            {
                try
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = new MemoryStream(png);
                    img.EndInit();
                    Clipboard.SetImage(img);
                    copied = true;
                }
                catch { }
            }
            ShowToast(copied ? "Скриншот сохранён и скопирован — можно вставить Ctrl+V" : "Скриншот сохранён", "Показать",
                () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ShowToast("Не получилось сделать скриншот: " + ex.Message);
        }
    }

    // ───────────────────────── контекстное меню страницы ─────────────────────────

    void OnContextMenu(CoreWebView2 core, CoreWebView2ContextMenuRequestedEventArgs e, BrowserTab? tab)
    {
        if (Gestures.ConsumeContextMenu())
        {
            e.Handled = true;   // только что был жест правой кнопкой — меню не нужно
            return;
        }
        if (_env == null) return;
        var items = e.MenuItems;
        var target = e.ContextMenuTarget;
        int insert = 0;
        if (target.HasLinkUri && UrlTools.IsWeb(target.LinkUri))
        {
            // Вместо «Открыть в новом окне» — привычные пункты Яндекса.
            for (int i = items.Count - 1; i >= 0; i--)
                if (items[i].Name is "openLinkInNewWindow" or "openLinkInNewTab") items.RemoveAt(i);
            var link = target.LinkUri;
            var open = _env.CreateContextMenuItem("Открыть ссылку в новой вкладке", null, CoreWebView2ContextMenuItemKind.Command);
            open.CustomItemSelected += (_, _) => Dispatcher.BeginInvoke(() => OpenLinkFrom(tab, link, background: false));
            var bg = _env.CreateContextMenuItem("Открыть ссылку в фоновой вкладке", null, CoreWebView2ContextMenuItemKind.Command);
            bg.CustomItemSelected += (_, _) => Dispatcher.BeginInvoke(() => OpenLinkFrom(tab, link, background: true));
            var priv = _env.CreateContextMenuItem("Открыть ссылку в приватной вкладке", null, CoreWebView2ContextMenuItemKind.Command);
            priv.CustomItemSelected += (_, _) => Dispatcher.BeginInvoke(() => { if (_current != null) ActivateTab(NewTab(_current, link, null, null, true)); });
            items.Insert(insert++, open);
            items.Insert(insert++, bg);
            items.Insert(insert++, priv);
            if (!tab?.IsPrivate ?? true)
            {
                var save = _env.CreateContextMenuItem("Добавить ссылку в закладки", null, CoreWebView2ContextMenuItemKind.Command);
                save.CustomItemSelected += (_, _) => Dispatcher.BeginInvoke(() =>
                {
                    if (_bookmarks.FindByUrl(link) != null) { ShowToast("Эта ссылка уже в закладках"); return; }
                    _bookmarks.Add(UrlTools.SiteKey(UrlTools.Host(link)), link, BookmarkStore.OtherId);
                    ShowToast("Ссылка добавлена в «Другие закладки»");
                });
                items.Insert(insert++, save);
            }
            items.Insert(insert++, _env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
        }
        if (target.HasSelection && target.SelectionText.Trim() is { Length: > 0 } text)
        {
            var shortText = text.Length > 24 ? text[..24].TrimEnd() + "…" : text;
            var search = _env.CreateContextMenuItem($"Найти в {UrlTools.EngineName(_settings.SearchEngine)}: «{shortText}»", null, CoreWebView2ContextMenuItemKind.Command);
            search.CustomItemSelected += (_, _) => Dispatcher.BeginInvoke(() => OpenLinkFrom(tab, UrlTools.SearchUrl(_settings.SearchEngine, text), background: false));
            items.Insert(insert++, search);
            if (UrlTools.LooksLikeAddress(text))
            {
                var go = _env.CreateContextMenuItem($"Перейти: {shortText}", null, CoreWebView2ContextMenuItemKind.Command);
                go.CustomItemSelected += (_, _) => Dispatcher.BeginInvoke(() => OpenLinkFrom(tab, UrlTools.ToUrl(text, _settings.SearchEngine), background: false));
                items.Insert(insert++, go);
            }
            items.Insert(insert, _env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
        }
    }

    void OpenLinkFrom(BrowserTab? opener, string url, bool background)
    {
        var ws = opener?.Workspace ?? _current;
        if (ws == null) return;
        int? index = opener != null && _settings.OpenNearCurrent ? NextIndexAfter(opener) : null;
        var tab = NewTab(ws, url, null, index, opener?.IsPrivate == true, opener?.Group);
        if (background) ShowToast("Открыто в фоновой вкладке");
        else ActivateTab(tab);
    }

    // ───────────────────────── палитра команд Ctrl+K ─────────────────────────

    void Palette_Click(object sender, RoutedEventArgs e) => OpenPalette();

    void OpenPalette()
    {
        if (!_ready) return;
        CollapsePanelOverlay(immediate: true);
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
            result.Add(new PaletteItem { Glyph = "", Title = $"Открыть {UrlTools.Display(url)}", Kind = "Адрес", Run = () => OpenSmart(url) });
        }

        foreach (var ws in _workspaces)
        foreach (var t in ws.Tabs)
        {
            bool show = q.Length == 0 ? ws == _current && t != ws.ActiveTab : match(t.Title + " " + t.Url + " " + ws.Name + " " + t.Group?.Name);
            if (!show) continue;
            var tab = t;
            result.Add(new PaletteItem
            {
                Glyph = tab.FallbackGlyph,
                Icon = tab.Favicon,
                Title = tab.Title,
                Subtitle = UrlTools.Display(tab.Url),
                Kind = (ws == _current ? "Вкладка" : $"Вкладка · {ws.Name}") + (tab.Group is { Name.Length: > 0 } g ? $" · {g.Name}" : ""),
                Run = () => { ActivateTab(tab); FocusPage(); },
            });
            found++;
        }

        if (q.Length > 0)
        {
            foreach (var b in _bookmarks.AllLinks().Where(b => match(b.Title + " " + b.Url)).Take(8))
            {
                var node = b;
                result.Add(new PaletteItem
                {
                    Glyph = "", Icon = node.Icon, Title = node.Title, Subtitle = node.Url ?? "", Kind = "Закладка",
                    Run = () => OpenSmart(node.Url!),
                });
                found++;
            }
            foreach (var ws in _workspaces)
            foreach (var l in ws.Links.Where(l => match(l.Title + " " + l.Url + " " + ws.Name)))
            {
                var link = l;
                result.Add(new PaletteItem
                {
                    Glyph = "",
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
        if (tab != null)
        {
            yield return C(tab.IsPinned ? "" : "", tab.IsPinned ? "Открепить вкладку" : "Закрепить вкладку", "", "pin закрепить", () => TogglePin(tab));
            yield return C("", "Добавить вкладку в новую группу", "", "group группа", () => NewGroup(tab, ask: true));
        }
        yield return C("", "Добавить в закладки", "Ctrl+D", "bookmark закладка избранное звезда", ShowBookmarkPopup);
        yield return C("", "Добавить страницу на стартовую", "", "start стартовая табло плитка", () => { if (tab != null) AddToStart(tab); });
        yield return C("", "Закладки", "Ctrl+Shift+O", "bookmarks закладки менеджер", () => OpenInternal("bookmarks"), true);
        yield return C("", "История", "Ctrl+H", "history история", () => OpenInternal("history"), true);
        yield return C("", "Загрузки", "Ctrl+J", "downloads загрузки скачанные", () => OpenInternal("downloads"), true);
        yield return C("", "Настройки", "Ctrl+,", "settings настройки параметры", () => OpenInternal("settings"), true);
        yield return C("", "Темы и цвета", "", "theme тема оформление цвет тёмная светлая", () => OpenInternal("settings", "look"));
        yield return C("", "Перенести закладки из Яндекс Браузера", "", "import импорт яндекс", () => ImportBookmarks("yandex"));
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
            "adblock реклама трекеры", () => SetSetting("adBlock", !_settings.AdBlock));
        if (tab?.View?.CoreWebView2 is { } core && tab.ShowAudio)
            yield return C(tab.IsMuted ? "" : "", tab.IsMuted ? "Включить звук вкладки" : "Выключить звук вкладки", "", "mute звук",
                () => core.IsMuted = !core.IsMuted);
        yield return C("", "Найти на странице", "Ctrl+F", "find поиск искать", StartFind);
        yield return C("", "Перевести страницу", "", "translate перевод переводчик", TranslatePage);
        yield return C("", "Режим чтения", "", "reader чтение статья читать", OpenReader);
        yield return C("", "Скриншот видимой части", "Ctrl+Shift+S", "screenshot снимок экрана", () => TakeScreenshot(false));
        yield return C("", "Скриншот всей страницы", "", "screenshot снимок экрана целиком", () => TakeScreenshot(true));
        yield return C("", "Печать", "Ctrl+P", "print печать pdf", PrintPage);
        foreach (var (id, engine) in UrlTools.Engines)
        {
            var key = id;
            yield return C("", $"Поиск по умолчанию: {engine.Name}" + (id == _settings.SearchEngine ? "  ✓" : ""), "",
                "search engine поисковик", () => SetSetting("searchEngine", key));
        }
        yield return C("", "Очистить историю и кэш", "", "clear history cache удалить", ClearHistoryAndCache);
        yield return C("", "Удалить все данные сайтов (выйти со всех сайтов)", "", "clear cookies пароли выйти", ClearAllSiteData);
        yield return C("", "Инструменты разработчика", "F12", "devtools console консоль", () => ActiveCore?.OpenDevToolsWindow());
        yield return C("", "Открыть папку данных браузера", "", "data folder профиль", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true }));
        yield return C("", "Полноэкранный режим", "F11", "fullscreen", () => SetFullscreen(!_isFullscreen, manual: true));
        yield return C("", $"Проверить обновления · версия {Updater.CurrentText}", "", "update обновить обновление версия о программе about",
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

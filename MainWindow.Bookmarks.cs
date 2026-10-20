using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SneznyjBrowser;

/// <summary>Закладки: звёздочка в адресной строке, панель закладок, перенос из Яндекс Браузера.</summary>
public partial class MainWindow
{
    Popup? _bookmarkPopup;
    ClipRowPanel? _barPanel;

    void Star_Click(object sender, RoutedEventArgs e) => ShowBookmarkPopup();

    void UpdateStar()
    {
        var tab = _current?.ActiveTab;
        bool can = tab != null && UrlTools.IsWeb(tab.Url) && !tab.IsPrivate;
        StarBtn.Visibility = can ? Visibility.Visible : Visibility.Collapsed;
        bool marked = can && _bookmarks.FindByUrl(tab!.Url) != null;
        StarBtn.Content = marked ? "" : "";
        if (marked) StarBtn.SetResourceReference(ForegroundProperty, "AccentBrush");
        else StarBtn.SetResourceReference(ForegroundProperty, "MutedBrush");
        StarBtn.ToolTip = marked ? "Изменить закладку (Ctrl+D)" : "Добавить в закладки (Ctrl+D)";
    }

    /// <summary>Звёздочка или Ctrl+D: закладка добавляется сразу, а в окошке можно поправить название и папку.</summary>
    void ShowBookmarkPopup()
    {
        if (_current?.ActiveTab is not { } tab || !UrlTools.IsWeb(tab.Url) || tab.IsPrivate)
        {
            ShowToast("Эту страницу нельзя добавить в закладки");
            return;
        }
        if (_bookmarkPopup?.IsOpen == true) { _bookmarkPopup.IsOpen = false; return; }
        var node = _bookmarks.FindByUrl(tab.Url);
        bool isNew = node == null;
        node ??= _bookmarks.Add(tab.Title.Length > 60 ? tab.Title[..60].TrimEnd() + "…" : tab.Title, tab.Url);
        var ws = tab.Workspace;

        var title = new TextBlock { Text = isNew ? "Закладка добавлена" : "Изменить закладку", FontSize = 15, FontWeight = FontWeights.SemiBold };
        TextBlock Label(string t)
        {
            var l = new TextBlock { Text = t, FontSize = 12, Margin = new Thickness(0, 12, 0, 5) };
            l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            return l;
        }
        var name = new TextBox { Text = node.Title };
        name.SetResourceReference(StyleProperty, "FieldBox");
        var folder = new ComboBox();
        folder.SetResourceReference(StyleProperty, "FieldCombo");
        var folders = _bookmarks.Folders();
        foreach (var f in folders) folder.Items.Add(new ComboBoxItem { Content = f.Name, Tag = f.Id });
        var current = _bookmarks.FolderOf(node);
        folder.SelectedIndex = Math.Max(0, folders.FindIndex(f => f.Id == current));
        var onStart = new CheckBox { Content = $"Показать на стартовой «{ws.Name}»", Margin = new Thickness(0, 14, 0, 0), IsChecked = ws.Links.Any(l => l.Url == tab.Url) };
        onStart.SetResourceReference(StyleProperty, "CheckRow");

        var remove = new Button { Content = "Удалить" };
        remove.SetResourceReference(StyleProperty, "TextButton");
        var all = new Button { Content = "Все закладки", Margin = new Thickness(8, 0, 0, 0) };
        all.SetResourceReference(StyleProperty, "TextButton");
        var done = new Button { Content = "Готово", Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        done.SetResourceReference(StyleProperty, "AccentButton");
        var buttons = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        DockPanel.SetDock(remove, Dock.Left);
        buttons.Children.Add(remove);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(all);
        right.Children.Add(done);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);

        var body = new StackPanel();
        body.Children.Add(title);
        body.Children.Add(Label("Название"));
        body.Children.Add(name);
        body.Children.Add(Label("Папка"));
        body.Children.Add(folder);
        body.Children.Add(onStart);
        body.Children.Add(buttons);
        var card = new Border
        {
            Width = 360, Padding = new Thickness(18, 16, 18, 16), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 16, 16), Child = body, Effect = (System.Windows.Media.Effects.Effect)FindResource("PopupShadow"),
        };
        card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
        TextElement.SetFontFamily(card, FontFamily);
        card.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");

        var popup = new Popup
        {
            Child = card, PlacementTarget = StarBtn, Placement = PlacementMode.Bottom, HorizontalOffset = -330,
            StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Focusable = true,
        };
        bool removed = false;
        void Apply()
        {
            if (removed) return;
            var t = name.Text.Trim();
            if (t.Length > 0) node.Title = t;
            if (folder.SelectedItem is ComboBoxItem { Tag: string fid } && fid != _bookmarks.FolderOf(node)) _bookmarks.Move(node, fid);
            bool want = onStart.IsChecked == true, has = ws.Links.Any(l => l.Url == tab.Url);
            if (want && !has) ws.Links.Add(new QuickLink { Title = node.Title.Length > 40 ? node.Title[..40] + "…" : node.Title, Url = tab.Url });
            if (!want && has) ws.Links.RemoveAll(l => l.Url == tab.Url);
            if (want != has) StartLinksChanged();
            if (want && !has) FetchStartLogos(new[] { tab.Url });
            _bookmarks.Notify();
        }
        remove.Click += (_, _) => { removed = true; _bookmarks.Remove(node); popup.IsOpen = false; ShowToast("Закладка удалена"); };
        all.Click += (_, _) => { Apply(); popup.IsOpen = false; OpenInternal("bookmarks"); };
        done.Click += (_, _) => popup.IsOpen = false;
        popup.Closed += (_, _) => { Apply(); _openMenus = Math.Max(0, _openMenus - 1); };
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.OriginalSource is TextBox) { popup.IsOpen = false; e.Handled = true; }
            else if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
        };
        _bookmarkPopup = popup;
        _openMenus++;
        popup.IsOpen = true;
        Dispatcher.BeginInvoke(() => { name.Focus(); Keyboard.Focus(name); name.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        UpdateStar();
    }

    void OnBookmarksChanged()
    {
        RefreshBookmarkBar();
        UpdateStar();
        InternalPages.Broadcast(AllPageCores("bookmarks"), BookmarksPayload());
    }

    void RefreshBookmarkBar()
    {
        bool empty = _bookmarks.Bar.Count == 0;
        BarEmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ImportYandexBtn.Visibility = _bookmarks.IsEmpty && BookmarkStore.FindSourceFile("yandex") != null ? Visibility.Visible : Visibility.Collapsed;
        OtherBookmarksBtn.Visibility = _bookmarks.Other.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_barPanel == null && FindChild<ClipRowPanel>(BookmarkBarItems) is { } panel)
        {
            _barPanel = panel;
            panel.OverflowChanged += () => BarMoreBtn.Visibility = panel.FirstHidden >= 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_barPanel != null) BarMoreBtn.Visibility = _barPanel.FirstHidden >= 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static BookmarkNode? NodeOf(object sender) => (sender as FrameworkElement)?.DataContext as BookmarkNode;

    void BookmarkBar_Click(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is not { } n) return;
        if (n.IsFolder) { ShowFolderMenu(n.Children!, (UIElement)sender); return; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) OpenBookmarkIn(n, "background");
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) OpenBookmarkIn(n, "new");
        else OpenBookmarkIn(n, "same");
    }

    void BookmarkBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || NodeOf(sender) is not { IsFolder: false } n) return;
        OpenBookmarkIn(n, "background");
        e.Handled = true;
    }

    void OpenBookmarkIn(BookmarkNode n, string mode)
    {
        if (_current == null || n.Url == null) return;
        var tab = _current.ActiveTab;
        switch (mode)
        {
            case "background":
                NewTab(_current, n.Url, n.Title, tab != null ? NextIndexAfter(tab) : null);
                ShowToast("Открыто в фоновой вкладке");
                break;
            case "new":
                ActivateTab(NewTab(_current, n.Url, n.Title, tab != null ? NextIndexAfter(tab) : null));
                break;
            case "private":
                ActivateTab(NewTab(_current, n.Url, n.Title, null, true));
                break;
            default:
                if (tab != null && !tab.IsPinned) { Navigate(tab, n.Url); FocusPage(); }
                else ActivateTab(NewTab(_current, n.Url, n.Title));
                break;
        }
    }

    void BookmarkBar_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (NodeOf(sender) is not { } n) return;
        e.Handled = true;
        ShowBookmarkMenu(n, (UIElement)sender);
    }

    void ShowBookmarkMenu(BookmarkNode n, UIElement target)
    {
        var m = new ContextMenu();
        if (!n.IsFolder)
        {
            m.Items.Add(MenuItemFor("Открыть в новой вкладке", "", () => OpenBookmarkIn(n, "new")));
            m.Items.Add(MenuItemFor("Открыть в фоновой вкладке", null, () => OpenBookmarkIn(n, "background")));
            m.Items.Add(MenuItemFor("Открыть в приватной вкладке", "", () => OpenBookmarkIn(n, "private")));
            m.Items.Add(new Separator());
        }
        else
        {
            m.Items.Add(MenuItemFor("Открыть все ссылки папки", "", () =>
            {
                foreach (var c in n.Children!.Where(c => !c.IsFolder).Take(30)) OpenBookmarkIn(c, "background");
            }, n.Children!.Any(c => !c.IsFolder)));
            m.Items.Add(new Separator());
        }
        m.Items.Add(MenuItemFor("Изменить…", "", () => EditBookmark(n)));
        m.Items.Add(MenuItemFor("Удалить", "", () =>
        {
            if (n.IsFolder && n.Children!.Count > 0 &&
                !InputDialog.Confirm(this, $"Удалить папку «{n.Title}»?", $"Вместе с ней удалятся закладки внутри ({n.Children.Count}).", "Удалить"))
                return;
            _bookmarks.Remove(n);
        }));
        m.Items.Add(new Separator());
        m.Items.Add(MenuItemFor("Новая папка…", "", () =>
        {
            var name = InputDialog.Ask(this, "Новая папка на панели закладок", "Например: Работа", "");
            if (!string.IsNullOrWhiteSpace(name)) _bookmarks.AddFolder(name.Trim());
        }));
        m.Items.Add(MenuItemFor("Все закладки", "", () => OpenInternal("bookmarks"), gesture: "Ctrl+Shift+O"));
        m.Items.Add(MenuItemFor("Скрыть панель закладок", null, () => SetSetting("bookmarksBar", false)));
        OpenMenu(m, target);
    }

    void EditBookmark(BookmarkNode n)
    {
        if (n.IsFolder)
        {
            var name = InputDialog.Ask(this, "Название папки", "", n.Title);
            if (string.IsNullOrWhiteSpace(name)) return;
            n.Title = name.Trim();
        }
        else
        {
            var result = InputDialog.AskTwo(this, "Изменить закладку", "Название", n.Title, "Адрес", n.Url ?? "");
            if (result is not { } r) return;
            if (r.First.Trim().Length > 0) n.Title = r.First.Trim();
            var url = UrlTools.ToUrl(r.Second, _settings.SearchEngine);
            if (UrlTools.IsWeb(url)) n.Url = url;
        }
        _bookmarks.Notify();
    }

    void ShowFolderMenu(IEnumerable<BookmarkNode> items, UIElement target)
    {
        var m = new ContextMenu { MaxHeight = 640 };
        var list = items.ToList();
        if (list.Count == 0) m.Items.Add(MenuItemFor("Пусто", null, null, enabled: false));
        foreach (var c in list) m.Items.Add(BookmarkMenuItem(c));
        OpenMenu(m, target, PlacementMode.Bottom);
    }

    void BarMore_Click(object sender, RoutedEventArgs e)
    {
        int first = _barPanel?.FirstHidden ?? -1;
        if (first < 0) return;
        ShowFolderMenu(_bookmarks.Bar.Skip(first), BarMoreBtn);
    }

    void OtherBookmarks_Click(object sender, RoutedEventArgs e) => ShowFolderMenu(_bookmarks.Other, OtherBookmarksBtn);

    void ImportYandex_Click(object sender, RoutedEventArgs e) => ImportBookmarks("yandex");

    void ImportBookmarks(string source)
    {
        var src = BookmarkStore.Sources.FirstOrDefault(s => s.Id == source);
        if (src.Id == null) return;
        var file = BookmarkStore.FindSourceFile(source);
        if (file == null) { ShowToast($"Не нашёл закладок {src.Name} на этом компьютере"); return; }
        try
        {
            // Файл может быть занят открытым браузером — читаем копию.
            var copy = Path.Combine(Path.GetTempPath(), $"sneznyj-import-{Guid.NewGuid():N}.json");
            File.Copy(file, copy, true);
            int n;
            try { n = _bookmarks.ImportChromium(copy, src.Name); }
            finally { try { File.Delete(copy); } catch { } }
            if (n == 0) { ShowToast($"Новых закладок в {src.Name} нет — всё уже перенесено"); return; }
            if (!_settings.BookmarksBar) SetSetting("bookmarksBar", true);
            ShowToast($"Перенесено закладок из {src.Name}: {n}", "Открыть", () => OpenInternal("bookmarks"));
            _ = FetchMissingFavicons(_bookmarks.AllLinks().Take(150).ToList());
        }
        catch (Exception ex)
        {
            ShowToast("Не получилось перенести закладки: " + ex.Message);
        }
    }

    static readonly HttpClient IconClient = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>
    /// Значки для перенесённых закладок: берём /favicon.ico прямо с самих сайтов (как при обычном заходе),
    /// без сторонних сервисов, которые узнали бы список ваших закладок.
    /// </summary>
    async Task FetchMissingFavicons(List<BookmarkNode> nodes)
    {
        var hosts = nodes.Select(n => UrlTools.Host(n.Url)).Where(h => h.Length > 0 && !File.Exists(Favicons.PathFor(h))).Distinct().Take(120).ToList();
        if (hosts.Count == 0) return;
        if (!IconClient.DefaultRequestHeaders.Contains("User-Agent"))
            IconClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        using var gate = new SemaphoreSlim(6);
        var tasks = hosts.Select(async host =>
        {
            await gate.WaitAsync();
            try
            {
                var bytes = await IconClient.GetByteArrayAsync($"https://{host}/favicon.ico");
                if (bytes.Length is < 16 or > 500_000) return;
                var png = await Task.Run(() => ToPng(bytes));
                if (png != null) await File.WriteAllBytesAsync(Favicons.PathFor(host), png);
            }
            catch { }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        foreach (var n in nodes) n.RefreshIcon();
        Favicons.Dirty = true;
        StartPage.Write(_workspaces, _settings);
        InternalPages.Broadcast(AllPageCores("bookmarks"), BookmarksPayload());
    }

    static byte[]? ToPng(byte[] data)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            // Из нескольких размеров иконки берём ближайший к 32 пикселям.
            var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(frame));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}

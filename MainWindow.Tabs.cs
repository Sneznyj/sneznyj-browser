using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SneznyjBrowser;

/// <summary>Полоса вкладок (сверху, списком слева и значками в узкой панели): клики, перетаскивание, меню, закрепление, группы.</summary>
public partial class MainWindow
{
    ItemsControl? _dragHost;
    BrowserTab? _dragTab;
    Point _dragStart;
    bool _dragging;

    static object? ItemFromSource(object? source, ItemsControl host)
    {
        var d = source as DependencyObject;
        while (d != null && d != host)
        {
            if (d is FrameworkElement { DataContext: BrowserTab or TabGroup } fe && fe.DataContext is not StripEnd) return fe.DataContext;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    static bool InsideButton(object? source, DependencyObject host)
    {
        var d = source as DependencyObject;
        while (d != null && d != host)
        {
            if (d is ButtonBase) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var c = VisualTreeHelper.GetChild(parent, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is { } found) return found;
        }
        return null;
    }

    void Strip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var host = (ItemsControl)sender;
        if (InsideButton(e.OriginalSource, host)) return;
        var item = ItemFromSource(e.OriginalSource, host);
        if (item is TabGroup g)
        {
            ToggleGroup(g);
            e.Handled = true;
            return;
        }
        if (item is not BrowserTab tab) return;
        _dragHost = host;
        _dragTab = tab;
        _dragStart = e.GetPosition(host);
        _dragging = false;
        if (tab != _current?.ActiveTab)
        {
            ActivateTab(tab);
            if (UrlTools.IsNewTab(tab.Url)) FocusAddress(); else FocusPage();
        }
        e.Handled = true;
    }

    void Strip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab == null || sender != _dragHost) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
        var pos = e.GetPosition(_dragHost);
        bool horizontal = _dragHost == TopTabs;
        if (!_dragging)
        {
            double moved = horizontal ? Math.Abs(pos.X - _dragStart.X) : Math.Abs(pos.Y - _dragStart.Y);
            if (moved < 6) return;
            _dragging = true;
            _dragHost.CaptureMouse();
            _dragHost.Cursor = Cursors.SizeAll;
        }
        DragTo(_dragHost, pos, horizontal);
    }

    void Strip_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragTab == null) return;
        bool wasDragging = _dragging;
        var tab = _dragTab;
        EndDrag();
        if (!wasDragging) return;
        e.Handled = true;
        // Бросили на значок другого пространства — переносим туда.
        if (WorkspaceUnderMouse() is { } ws && ws != tab.Workspace && !tab.IsPrivate) MoveTab(tab, ws);
        QueueSave();
    }

    void EndDrag()
    {
        if (_dragHost != null)
        {
            _dragHost.Cursor = null;
            if (_dragHost.IsMouseCaptured) _dragHost.ReleaseMouseCapture();
        }
        _dragHost = null;
        _dragTab = null;
        _dragging = false;
    }

    Workspace? WorkspaceUnderMouse()
    {
        foreach (var list in new ItemsControl[] { RailWorkspaces, WorkspaceList, WorkspaceRows })
        {
            if (!list.IsVisible) continue;
            var p = Mouse.GetPosition(list);
            if (p.X < 0 || p.Y < 0 || p.X > list.ActualWidth || p.Y > list.ActualHeight) continue;
            if (list.InputHitTest(p) is DependencyObject hit)
            {
                var d = hit;
                while (d != null && d != list)
                {
                    if (d is FrameworkElement { DataContext: Workspace w }) return w;
                    d = VisualTreeHelper.GetParent(d);
                }
            }
        }
        return null;
    }

    /// <summary>Живое перетаскивание: вкладка меняется местами с соседями, как только курсор проходит середину соседа.</summary>
    void DragTo(ItemsControl host, Point pos, bool horizontal)
    {
        var tab = _dragTab!;
        var ws = tab.Workspace;
        var tabs = ws.Tabs;
        double a = horizontal ? pos.X : pos.Y;
        for (int i = 0; i < host.Items.Count; i++)
        {
            if (host.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c || !c.IsVisible) continue;
            Rect r;
            try { r = c.TransformToAncestor(host).TransformBounds(new Rect(c.RenderSize)); }
            catch { continue; }
            double start = horizontal ? r.Left : r.Top, end = horizontal ? r.Right : r.Bottom;
            if (a < start || a >= end) continue;
            var item = host.Items[i];
            if (item == tab) return;
            double mid = (start + end) / 2;
            int from = tabs.IndexOf(tab);

            if (item is BrowserTab other)
            {
                int to = tabs.IndexOf(other);
                if ((from < to && a < mid) || (from > to && a > mid)) return;
                tabs.Move(from, to);
                tab.IsPinned = other.IsPinned;
                FixGroupAfterMove(tab);
            }
            else if (item is TabGroup g)
            {
                var members = tabs.Where(t => t.Group == g).ToList();
                if (members.Count == 0) return;
                int first = tabs.IndexOf(members[0]), last = tabs.IndexOf(members[^1]);
                if (tab.Group == g)
                {
                    // Тянем первую вкладку группы влево через её ярлычок — выходит из группы.
                    if (from == first && a < mid) tab.Group = null;
                    else return;
                }
                else if (from < first)
                {
                    if (a < mid) return;
                    if (g.Collapsed) { tabs.Move(from, last); tab.Group = null; }
                    else { tabs.Move(from, first - 1); tab.Group = g; }
                    tab.IsPinned = false;
                }
                else
                {
                    if (a > mid) return;
                    tabs.Move(from, first);
                    tab.Group = null;
                    tab.IsPinned = false;
                }
            }
            else return;
            ws.RefreshStrip();
            return;
        }
    }

    /// <summary>После перемещения: закреплённые — без группы, вкладка внутри группы — в ней, на краю своей — остаётся.</summary>
    static void FixGroupAfterMove(BrowserTab t)
    {
        if (t.IsPinned) { t.Group = null; return; }
        var tabs = t.Workspace.Tabs;
        int i = tabs.IndexOf(t);
        var prev = i > 0 ? tabs[i - 1] : null;
        var next = i < tabs.Count - 1 ? tabs[i + 1] : null;
        if (prev?.Group != null && prev.Group == next?.Group) t.Group = prev.Group;
        else if (t.Group != null && (prev?.Group == t.Group || next?.Group == t.Group)) { }
        else if (t.Group != null && prev?.Group == null && next?.Group == null && tabs.Count(x => x.Group == t.Group) == 1) { }
        else t.Group = null;
    }

    void Strip_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        var host = (ItemsControl)sender;
        if (ItemFromSource(e.OriginalSource, host) is BrowserTab tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
        else if (InsideButton(e.OriginalSource, host) || ItemFromSource(e.OriginalSource, host) == null)
        {
            OpenNewTab();
            e.Handled = true;
        }
    }

    void Strip_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var host = (ItemsControl)sender;
        switch (ItemFromSource(e.OriginalSource, host))
        {
            case BrowserTab tab: ShowTabMenu(tab, host); e.Handled = true; break;
            case TabGroup g: ShowGroupMenu(g, host); e.Handled = true; break;
        }
    }

    void TopTabs_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindChild<TabStripPanel>(TopTabs) is not { } panel) return;
        if (panel.Extent <= panel.ActualWidth + 1) return;
        panel.ScrollBy(-e.Delta / 2.0);
        e.Handled = true;
    }

    void BringTabIntoView(BrowserTab tab)
    {
        if (_current == null) return;
        int index = _current.Strip.IndexOf(tab);
        if (index < 0) return;
        if (TopTabs.IsVisible && FindChild<TabStripPanel>(TopTabs) is { } panel) panel.EnsureVisible(index);
        foreach (var list in new ItemsControl[] { SideTabs, RailTabs })
            if (list.IsVisible && list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement c) c.BringIntoView();
    }

    // ───────────────────────── меню вкладки и группы ─────────────────────────

    static MenuItem MenuItemFor(string header, string? glyph, Action? run, bool enabled = true, string? gesture = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture ?? "" };
        if (glyph != null)
        {
            var icon = new TextBlock { Text = glyph, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            item.Icon = icon;
        }
        if (run != null) item.Click += (_, _) => run();
        return item;
    }

    static FrameworkElement ColorDot(Brush brush, double size = 12) =>
        new Ellipse { Width = size, Height = size, Fill = brush, VerticalAlignment = VerticalAlignment.Center };

    void ShowTabMenu(BrowserTab tab, UIElement target, UIElement? anchor = null)
    {
        var ws = tab.Workspace;
        bool top = target == TopTabs;
        var m = new ContextMenu();
        m.Items.Add(MenuItemFor(top ? "Новая вкладка справа" : "Новая вкладка ниже", "", () =>
        {
            ActivateTab(NewTab(ws, "", null, NextIndexAfter(tab), tab.IsPrivate, tab.Group));
            FocusAddress();
        }));
        m.Items.Add(MenuItemFor("Обновить", "", () => ReloadTab(tab), gesture: "F5"));
        m.Items.Add(MenuItemFor("Дублировать", "", () => DuplicateTab(tab)));
        m.Items.Add(MenuItemFor(tab.IsPinned ? "Открепить вкладку" : "Закрепить вкладку", tab.IsPinned ? "" : "", () => TogglePin(tab)));
        m.Items.Add(MenuItemFor(tab.IsMuted ? "Включить звук" : "Выключить звук", tab.IsMuted ? "" : "", () => ToggleMute(tab),
            tab.View?.CoreWebView2 != null));
        m.Items.Add(new Separator());

        if (!tab.IsPinned)
        {
            m.Items.Add(MenuItemFor("Добавить в новую группу", "", () => NewGroup(tab, ask: true)));
            var others = ws.Groups.Where(g => g != tab.Group).ToList();
            if (others.Count > 0)
            {
                var sub = MenuItemFor("Добавить в группу", "", null);
                foreach (var g in others)
                {
                    var group = g;
                    var it = MenuItemFor(g.Name.Length > 0 ? g.Name : "Группа без названия", null, () => AddToGroup(tab, group));
                    it.Icon = ColorDot(g.Brush);
                    sub.Items.Add(it);
                }
                m.Items.Add(sub);
            }
            if (tab.Group != null) m.Items.Add(MenuItemFor("Убрать из группы", "", () => RemoveFromGroup(tab)));
            m.Items.Add(new Separator());
        }

        if (_workspaces.Count > 1 && !tab.IsPrivate)
        {
            var sub = MenuItemFor("Перенести в пространство", "", null);
            foreach (var w in _workspaces.Where(w => w != ws))
            {
                var target2 = w;
                var it = MenuItemFor(w.Name, null, () => MoveTab(tab, target2));
                var icon = new TextBlock { Text = w.Glyph, FontSize = 13.5, Foreground = w.AccentBrush };
                icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                it.Icon = icon;
                sub.Items.Add(it);
            }
            m.Items.Add(sub);
        }
        bool web = UrlTools.IsWeb(tab.Url) && !tab.IsPrivate;
        m.Items.Add(MenuItemFor(_bookmarks.FindByUrl(tab.Url) != null ? "Изменить закладку" : "Добавить в закладки", "",
            () => { ActivateTab(tab); ShowBookmarkPopup(); }, web));
        m.Items.Add(MenuItemFor("Добавить на стартовую", "", () => AddToStart(tab), web));
        m.Items.Add(MenuItemFor("Скопировать адрес", "", () => { try { Clipboard.SetText(tab.Url); ShowToast("Адрес скопирован"); } catch { } },
            UrlTools.IsWeb(tab.Url)));
        m.Items.Add(new Separator());
        m.Items.Add(MenuItemFor("Закрыть вкладку", "", () => CloseTab(tab), gesture: "Ctrl+W"));
        m.Items.Add(MenuItemFor("Закрыть другие вкладки", null, () =>
        {
            foreach (var t in ws.Tabs.Where(t => t != tab && !t.IsPinned).ToList()) CloseTab(t);
        }, ws.Tabs.Any(t => t != tab && !t.IsPinned)));
        int idx = ws.Tabs.IndexOf(tab);
        m.Items.Add(MenuItemFor(top ? "Закрыть вкладки справа" : "Закрыть вкладки ниже", null, () =>
        {
            foreach (var t in ws.Tabs.Skip(idx + 1).Where(t => !t.IsPinned).ToList()) CloseTab(t);
        }, ws.Tabs.Skip(idx + 1).Any(t => !t.IsPinned)));
        m.Items.Add(MenuItemFor("Вернуть закрытую вкладку", "", ReopenClosedTab, _closed.Count > 0, "Ctrl+Shift+T"));
        if (anchor != null) OpenMenu(m, anchor, PlacementMode.Bottom);
        else OpenMenu(m, target);
    }

    void ShowGroupMenu(TabGroup g, UIElement target)
    {
        var ws = _current!;
        var m = new ContextMenu();
        m.Items.Add(MenuItemFor("Название группы…", "", () => RenameGroup(g)));
        var colors = MenuItemFor("Цвет", "", null);
        foreach (var c in Themes.GroupColors)
        {
            var color = c;
            var it = MenuItemFor(c.Name, null, () => { g.Color = color.Id; QueueSave(); });
            it.Icon = ColorDot(Themes.GroupBrush(c.Id));
            if (g.Color == c.Id) it.IsChecked = true;
            colors.Items.Add(it);
        }
        m.Items.Add(colors);
        m.Items.Add(MenuItemFor(g.Collapsed ? "Развернуть группу" : "Свернуть группу", g.Collapsed ? "" : "", () => ToggleGroup(g)));
        m.Items.Add(MenuItemFor("Новая вкладка в группе", "", () =>
        {
            var last = ws.Tabs.Last(t => t.Group == g);
            ActivateTab(NewTab(ws, "", null, ws.Tabs.IndexOf(last) + 1, false, g));
            FocusAddress();
        }));
        m.Items.Add(new Separator());
        if (_workspaces.Count > 1)
        {
            var sub = MenuItemFor("Перенести группу в пространство", "", null);
            foreach (var w in _workspaces.Where(w => w != ws))
            {
                var dest = w;
                sub.Items.Add(MenuItemFor(w.Name, null, () => MoveGroup(g, dest)));
            }
            m.Items.Add(sub);
        }
        m.Items.Add(MenuItemFor("Разгруппировать", "", () =>
        {
            foreach (var t in ws.Tabs.Where(t => t.Group == g)) t.Group = null;
            ws.RefreshStrip();
            QueueSave();
        }));
        m.Items.Add(MenuItemFor("Закрыть группу", "", () =>
        {
            var members = ws.Tabs.Where(t => t.Group == g).ToList();
            if (members.Count > 3 && !InputDialog.Confirm(this, "Закрыть группу?", $"Закроются {members.Count} вкладок группы «{g.Name}».", "Закрыть"))
                return;
            foreach (var t in members) CloseTab(t);
        }));
        OpenMenu(m, target);
    }

    void ToggleGroup(TabGroup g)
    {
        if (_current == null) return;
        g.Collapsed = !g.Collapsed;
        // Свернули группу с активной вкладкой — переходим на ближайшую вкладку вне группы.
        if (g.Collapsed && _current.ActiveTab?.Group == g)
        {
            var tabs = _current.Tabs;
            int i = tabs.IndexOf(_current.ActiveTab);
            var next = tabs.Skip(i).FirstOrDefault(t => t.Group != g) ?? tabs.Take(i).LastOrDefault(t => t.Group != g);
            if (next == null)
            {
                next = NewTab(_current, "", null, tabs.IndexOf(tabs.Last(t => t.Group == g)) + 1);
                next.Group = null;
            }
            _current.RefreshStrip();
            ActivateTab(next);
        }
        _current.RefreshStrip();
        QueueSave();
    }

    void TogglePin(BrowserTab tab)
    {
        var ws = tab.Workspace;
        var tabs = ws.Tabs;
        int from = tabs.IndexOf(tab);
        int pinned = tabs.Count(t => t.IsPinned);
        if (tab.IsPinned)
        {
            tab.IsPinned = false;
            tabs.Move(from, pinned - 1);   // сразу после закреплённых
        }
        else
        {
            tab.Group = null;
            tab.IsPinned = true;
            tabs.Move(from, pinned);       // в конец закреплённых
        }
        ws.RefreshStrip();
        BringTabIntoView(tab);
        QueueSave();
    }

    void NewGroup(BrowserTab tab, bool ask)
    {
        var ws = tab.Workspace;
        if (tab.IsPinned) TogglePin(tab);
        var used = ws.Groups.Select(g => g.Color).ToHashSet();
        var color = Themes.GroupColors.FirstOrDefault(c => !used.Contains(c.Id))?.Id ?? Themes.GroupColors[ws.Groups.Count % Themes.GroupColors.Length].Id;
        var group = new TabGroup { Color = color };
        // Вкладка была посреди другой группы — выносим её за ту группу, чтобы группы не перемешивались.
        if (tab.Group != null) MoveAfterGroup(tab, tab.Group);
        ws.Groups.Add(group);
        tab.Group = group;
        ws.RefreshStrip();
        QueueSave();
        if (ask) RenameGroup(group);
    }

    void RenameGroup(TabGroup g)
    {
        var name = InputDialog.Ask(this, "Название группы", "Например: Работа, Покупки, Читать позже", g.Name);
        if (name == null) return;
        g.Name = name.Trim();
        QueueSave();
    }

    void AddToGroup(BrowserTab tab, TabGroup g)
    {
        var ws = tab.Workspace;
        if (tab.IsPinned) TogglePin(tab);
        var last = ws.Tabs.LastOrDefault(t => t.Group == g);
        int from = ws.Tabs.IndexOf(tab);
        int to = last == null ? from : ws.Tabs.IndexOf(last);
        if (from > to) to++;
        ws.Tabs.Move(from, Math.Clamp(to, 0, ws.Tabs.Count - 1));
        tab.Group = g;
        ws.RefreshStrip();
        QueueSave();
    }

    void RemoveFromGroup(BrowserTab tab)
    {
        if (tab.Group is not { } g) return;
        MoveAfterGroup(tab, g);
        tab.Group = null;
        tab.Workspace.RefreshStrip();
        QueueSave();
    }

    static void MoveAfterGroup(BrowserTab tab, TabGroup g)
    {
        var tabs = tab.Workspace.Tabs;
        var last = tabs.Last(t => t.Group == g);
        int from = tabs.IndexOf(tab), to = tabs.IndexOf(last);
        if (from < to) tabs.Move(from, to);
    }

    void MoveGroup(TabGroup g, Workspace target)
    {
        var source = _current!;
        var members = source.Tabs.Where(t => t.Group == g).ToList();
        source.Groups.Remove(g);
        target.Groups.Add(g);
        foreach (var t in members)
        {
            bool wasActive = source.ActiveTab == t;
            source.Tabs.Remove(t);
            t.IsActive = false;
            if (wasActive) { source.ActiveTab = null; HideView(t); }
            t.Workspace = target;
            target.Tabs.Add(t);
            t.Group = g;
        }
        source.RefreshStrip();
        target.RefreshStrip();
        if (source.ActiveTab == null)
            ActivateTab(source.Tabs.FirstOrDefault() ?? NewTab(source, ""));
        ShowToast($"Группа перенесена в «{target.Name}»");
        QueueSave();
    }
}

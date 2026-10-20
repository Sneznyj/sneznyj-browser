using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace SneznyjBrowser;

/// <summary>
/// Левая панель как в Яндексе: закреплена, узкая с раскрытием при наведении или спрятана и выезжает от края.
/// Раскрытая панель — отдельное окно поверх страницы: страницу рисует движок Edge в своём окне,
/// и обычные элементы над ней не видны.
/// </summary>
public partial class MainWindow
{
    const double PanelWidth = 264, RailWidth = 52;

    Window? _panelOverlay;
    Border? _overlayFrame;
    TranslateTransform? _overlaySlide;
    bool _panelExpanded, _panelCollapsing;
    readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    long _hoverSince, _leaveSince;
    int _openMenus;
    /// <summary>Только для проверки командой --cmd=expand-panel: не прятать панель, пока не придёт collapse-panel.</summary>
    bool _holdExpanded;

    void InitLayout()
    {
        _hoverTimer.Tick += (_, _) => OnHoverTick();
        // Пока открыто меню из панели (правый клик), панель не прячется.
        LeftPanel.AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler((_, _) => _openMenus++), true);
        LeftPanel.AddHandler(ContextMenuClosingEvent, new ContextMenuEventHandler((_, _) => _openMenus = Math.Max(0, _openMenus - 1)), true);
        Deactivated += (_, _) =>
        {
            if (_openMenus == 0 && !_dragging) CollapsePanelOverlay(immediate: false);
        };
    }

    void CreatePanelOverlay()
    {
        _overlaySlide = new TranslateTransform();
        _overlayFrame = new Border
        {
            Width = PanelWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0, 0, 1, 0),
            ClipToBounds = true,
            RenderTransform = _overlaySlide,
        };
        _overlayFrame.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        _overlayFrame.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        var shadow = new Border
        {
            Margin = new Thickness(0, 0, 18, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _overlayFrame,
            Effect = (Effect)FindResource("PopupShadow"),
        };
        _panelOverlay = new Window
        {
            Owner = this,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            FontFamily = FontFamily,
            FontSize = FontSize,
            UseLayoutRounding = true,
            Content = shadow,
            Title = "Левая панель",
        };
        _panelOverlay.SetResourceReference(ForegroundProperty, "TextBrush");
        _panelOverlay.SourceInitialized += (_, _) => Native.MakeOverlay(_panelOverlay);
        _panelOverlay.PreviewKeyDown += OnKey;
    }

    /// <summary>Перестроить окно под настройки: вкладки сверху/слева, режим левой панели, панель закладок.</summary>
    void ApplyLayout()
    {
        if (_isFullscreen) return;
        bool top = _settings.TabsPosition == "top";
        var mode = _settings.LeftPanel;

        TabRow.Visibility = top ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(CaptionButtons, top ? 0 : 1);
        ToolbarGrid.Margin = new Thickness(6, 0, top ? 6 : 146, 0);
        Toolbar.BorderThickness = new Thickness(0);

        SideTabsHeader.Visibility = top ? Visibility.Collapsed : Visibility.Visible;
        SideTabsScroll.Visibility = top ? Visibility.Collapsed : Visibility.Visible;
        WsIconsBox.Visibility = top ? Visibility.Collapsed : Visibility.Visible;
        WsRowsBox.Visibility = top ? Visibility.Visible : Visibility.Collapsed;
        ServicesDock.Visibility = top ? Visibility.Collapsed : Visibility.Visible;
        ServicesRows.Visibility = top ? Visibility.Visible : Visibility.Collapsed;
        RailTabsScroll.Visibility = top ? Visibility.Hidden : Visibility.Visible;
        PanelHeaderRow.Height = new GridLength(top ? 40 : 46);
        RailHeaderRow.Height = new GridLength(top ? 40 : 46);

        bool pinned = mode == "pinned";
        PinBtn.Content = pinned ? "" : "";
        PinBtn.ToolTip = pinned ? "Открепить: панель будет прятаться и раскрываться при наведении" : "Закрепить панель";

        if (pinned)
        {
            CollapsePanelOverlay(immediate: true);
            MoveLeftPanel(PinnedHost);
            PinnedHost.Visibility = Visibility.Visible;
            Rail.Visibility = Visibility.Collapsed;
            _hoverTimer.Stop();
        }
        else
        {
            if (_overlayFrame != null) MoveLeftPanel(_overlayFrame);
            PinnedHost.Visibility = Visibility.Collapsed;
            Rail.Visibility = mode == "compact" ? Visibility.Visible : Visibility.Collapsed;
            _hoverTimer.Start();
        }

        BookmarksBarRow.Visibility = _settings.BookmarksBar ? Visibility.Visible : Visibility.Collapsed;
        ApplyWindowChrome();
        if (_current?.ActiveTab is { } t) Dispatcher.BeginInvoke(() => BringTabIntoView(t), DispatcherPriority.Loaded);
    }

    void MoveLeftPanel(Decorator host)
    {
        if (LeftPanel.Parent == host) return;
        if (LeftPanel.Parent is Decorator old) old.Child = null;
        host.Child = LeftPanel;
    }

    void PanelPin_Click(object sender, RoutedEventArgs e)
    {
        _settings.LeftPanel = _settings.LeftPanel == "pinned" ? "compact" : "pinned";
        SaveSettings();
        ApplyLayout();
        BroadcastSettings();
        ShowToast(_settings.LeftPanel == "pinned"
            ? "Панель закреплена. Открепить — кнопкой с булавкой"
            : "Панель раскрывается при наведении. Режимы — в настройках");
    }

    /// <summary>Каждые 40 мс: не навели ли мышь на узкую полоску или к левому краю окна.</summary>
    void OnHoverTick()
    {
        if (_settings.LeftPanel == "pinned" || _isFullscreen || WindowState == WindowState.Minimized || _panelOverlay == null) return;
        var cursor = Native.CursorPx();
        long now = Environment.TickCount64;

        if (_panelExpanded && !_panelCollapsing)
        {
            var frame = Native.ScreenRectPx(_overlayFrame!);
            frame.Inflate(6, 6);
            bool keep = frame.Contains(cursor) || _openMenus > 0 || _dragging || Mouse.Captured != null || _holdExpanded;
            if (keep) { _leaveSince = 0; return; }
            if (_leaveSince == 0) _leaveSince = now;
            if (now - _leaveSince > 300) CollapsePanelOverlay(immediate: false);
            return;
        }

        var root = Native.RootWindowUnderCursor();
        bool overUs = root == Native.Handle(this) || root == Native.Handle(_panelOverlay);
        if (!overUs || Native.AnyMouseButtonDown() || _dragging || !IsVisible) { _hoverSince = 0; return; }

        Rect zone;
        int delay;
        if (_settings.LeftPanel == "compact" && Rail.IsVisible)
        {
            zone = Native.ScreenRectPx(Rail);
            // Верх полоски — заголовок окна: там раскрывать не нужно, иначе мешает перетаскивать окно.
            double head = RailHeaderRow.ActualHeight * VisualTreeHelper.GetDpi(this).DpiScaleY;
            zone = new Rect(zone.X, zone.Y + head, zone.Width, Math.Max(0, zone.Height - head));
            delay = 170;
        }
        else
        {
            var area = Native.ScreenRectPx(MainArea);
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            bool maximized = WindowState == WindowState.Maximized;
            double head = 40 * dpi;
            zone = new Rect(area.Left - (maximized ? 2 : 0), area.Top + head, (maximized ? 4 : 8) * dpi, Math.Max(0, area.Height - head));
            delay = maximized ? 60 : 260;
        }
        if (!zone.Contains(cursor)) { _hoverSince = 0; return; }
        if (_hoverSince == 0) _hoverSince = now;
        if (now - _hoverSince >= delay) ExpandPanelOverlay();
    }

    void PlacePanelOverlay()
    {
        if (_panelOverlay == null) return;
        var host = LeftHost.IsVisible ? (FrameworkElement)LeftHost : MainArea;
        var px = Native.ScreenRectPx(host);
        var tl = Native.PxToDip(this, px.TopLeft);
        var br = Native.PxToDip(this, px.BottomRight);
        _panelOverlay.Left = tl.X;
        _panelOverlay.Top = tl.Y;
        _panelOverlay.Width = PanelWidth + 20;
        _panelOverlay.Height = Math.Max(100, br.Y - tl.Y);
    }

    void ExpandPanelOverlay()
    {
        if (_panelOverlay == null || _overlayFrame == null || _settings.LeftPanel == "pinned") return;
        if (_panelExpanded && !_panelCollapsing) return;
        _panelExpanded = true;
        _panelCollapsing = false;
        _leaveSince = 0;
        PlacePanelOverlay();
        if (!_panelOverlay.IsVisible) _panelOverlay.Show();
        if (_current?.ActiveTab is { } t) Dispatcher.BeginInvoke(() => BringTabIntoView(t), DispatcherPriority.Loaded);

        bool compact = _settings.LeftPanel == "compact";
        var duration = TimeSpan.FromMilliseconds(compact ? 170 : 190);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (!_settings.Animations || !SystemParameters.ClientAreaAnimation)
        {
            _overlayFrame.BeginAnimation(WidthProperty, null);
            _overlaySlide!.BeginAnimation(TranslateTransform.XProperty, null);
            _overlayFrame.Width = PanelWidth;
            _overlaySlide.X = 0;
            return;
        }
        if (compact)
        {
            _overlaySlide!.BeginAnimation(TranslateTransform.XProperty, null);
            _overlaySlide.X = 0;
            double from = _overlayFrame.ActualWidth is > 0 and < PanelWidth && _panelOverlay.IsVisible ? _overlayFrame.ActualWidth : RailWidth;
            _overlayFrame.BeginAnimation(WidthProperty, new DoubleAnimation(from, PanelWidth, duration) { EasingFunction = ease });
        }
        else
        {
            _overlayFrame.BeginAnimation(WidthProperty, null);
            _overlayFrame.Width = PanelWidth;
            double from = _overlaySlide!.X < 0 ? _overlaySlide.X : -PanelWidth - 20;
            _overlaySlide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, duration) { EasingFunction = ease });
        }
    }

    void CollapsePanelOverlay(bool immediate)
    {
        if (_panelOverlay == null || _overlayFrame == null || !_panelExpanded) return;
        if (_panelCollapsing && !immediate) return;
        _hoverSince = 0;
        if (immediate || !_settings.Animations || !SystemParameters.ClientAreaAnimation)
        {
            _panelExpanded = false;
            _panelCollapsing = false;
            _overlayFrame.BeginAnimation(WidthProperty, null);
            _overlaySlide!.BeginAnimation(TranslateTransform.XProperty, null);
            _panelOverlay.Hide();
            return;
        }
        _panelCollapsing = true;
        bool compact = _settings.LeftPanel == "compact";
        var duration = TimeSpan.FromMilliseconds(140);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        void Done()
        {
            if (!_panelCollapsing) return;   // за время анимации панель снова раскрыли
            _panelCollapsing = false;
            _panelExpanded = false;
            _panelOverlay.Hide();
            _overlayFrame.BeginAnimation(WidthProperty, null);
            _overlaySlide!.BeginAnimation(TranslateTransform.XProperty, null);
            _overlayFrame.Width = PanelWidth;
            _overlaySlide.X = 0;
        }
        if (compact)
        {
            var a = new DoubleAnimation(_overlayFrame.ActualWidth, RailWidth, duration) { EasingFunction = ease };
            a.Completed += (_, _) => Done();
            _overlayFrame.BeginAnimation(WidthProperty, a);
        }
        else
        {
            var a = new DoubleAnimation(_overlaySlide!.X, -PanelWidth - 20, duration) { EasingFunction = ease };
            a.Completed += (_, _) => Done();
            _overlaySlide.BeginAnimation(TranslateTransform.XProperty, a);
        }
    }

    /// <summary>Открыть меню и не прятать панель, пока оно открыто.</summary>
    void OpenMenu(ContextMenu menu, UIElement? target, PlacementMode placement = PlacementMode.MousePoint)
    {
        _openMenus++;
        menu.Closed += (_, _) => _openMenus = Math.Max(0, _openMenus - 1);
        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    // ───────────────────────── темы ─────────────────────────

    void OnThemeChanged()
    {
        Native.StyleWindow(this, rounded: !_isFullscreen);
        foreach (var ws in _workspaces)
        {
            ws.RefreshBrush();
            foreach (var g in ws.Groups) g.RefreshBrush();
        }
        var bg = Themes.ToDrawing(Themes.Current.PageBg);
        foreach (var ws in _workspaces)
        foreach (var t in ws.Tabs)
            if (t.View != null) t.View.DefaultBackgroundColor = bg;
        foreach (var a in _panelApps)
            if (a.View != null) a.View.DefaultBackgroundColor = bg;
        foreach (var core in AllCores()) ApplyColorScheme(core);
        StartPage.Write(_workspaces, _settings);
        InternalPages.Broadcast(AllPageCores(), new { type = "theme" });
        UpdateChrome();
        UpdateDownloadsButton();
    }
}

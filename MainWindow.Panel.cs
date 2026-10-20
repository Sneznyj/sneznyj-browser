using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace SneznyjBrowser;

/// <summary>
/// Боковая панель справа: мессенджеры и почта, а ещё «Любой сайт» — второй сайт рядом с основной страницей.
/// В строке адреса панели можно набрать любой адрес или запрос: сайт откроется в панели, страница слева останется.
/// </summary>
public partial class MainWindow
{
    const double PanelMinWidth = 280, PageMinWidth = 320, PanelDefaultWidth = 400;

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
        if (UrlTools.IsWeb(url)) OpenSmart(url!);
    }

    void PanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _session.PanelWidth = PanelCol.ActualWidth;
        QueueSave();
    }

    /// <summary>«Поровну»: страница и панель одинаковой ширины; второй раз — обычная узкая панель.</summary>
    void PanelHalf_Click(object sender, RoutedEventArgs e)
    {
        double half = Math.Floor((ContentGrid.ActualWidth - SplitterCol.ActualWidth) / 2);
        double width = Math.Abs(PanelCol.ActualWidth - half) < 24 ? PanelDefaultWidth : half;
        _session.PanelWidth = width;
        PanelCol.Width = new GridLength(ClampPanelWidth(width));
        QueueSave();
    }

    double ClampPanelWidth(double width)
    {
        double max = ContentGrid.ActualWidth > 1 ? ContentGrid.ActualWidth - 6 - PageMinWidth : 1600;
        return Math.Clamp(width, PanelMinWidth, Math.Max(PanelMinWidth, max));
    }

    void TogglePanel()
    {
        if (_panelOpen) ClosePanel();
        else OpenPanel(_panelApp ?? _panelApps.FirstOrDefault());
    }

    /// <summary>Открыть адрес рядом с основной страницей — в «Любом сайте» боковой панели.</summary>
    void OpenInPanel(string url)
    {
        if (!UrlTools.IsWeb(url)) return;
        _siteApp.Url = url;
        if (_siteApp.View?.CoreWebView2 is { } core)
            try { core.Navigate(url); } catch (ArgumentException) { }
        OpenPanel(_siteApp);
        QueueSave();
    }

    /// <summary>Открыть панель на «Любом сайте» и поставить курсор в строку адреса.</summary>
    void OpenSitePanel()
    {
        OpenPanel(_siteApp);
        FocusPanelAddress();
    }

    async void OpenPanel(PanelApp? app, bool focusAddress = true)
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
        UpdatePanelBar();
        QueueSave();
        if (app.View != null) return;
        // Пустой «Любой сайт»: подсказка и курсор в строке адреса.
        if (app.IsSite && !UrlTools.IsWeb(app.Url))
        {
            if (focusAddress) FocusPanelAddress();
            return;
        }

        var view = new WebView2 { DefaultBackgroundColor = Themes.ToDrawing(Themes.Current.PageBg) };
        app.View = view;
        PanelHost.Children.Add(view);
        UpdatePanelBar();
        try { await view.EnsureCoreWebView2Async(_env); }
        catch (Exception ex) { Debug.WriteLine(ex); return; }
        if (app.View != view || view.CoreWebView2 is not { } core) return;
        KeepWindowOnPageClose(view);
        Configure(core, null, app);
        core.SourceChanged += (_, _) => OnPanelSourceChanged(view);
        core.HistoryChanged += (_, _) => { if (_panelApp?.View == view) UpdatePanelBar(); };
        try { core.Navigate(app.Url); }
        catch (ArgumentException) { core.Navigate(UrlTools.SearchUrl(_settings.SearchEngine, app.Url)); }
    }

    void OnPanelSourceChanged(WebView2 view)
    {
        // Ищем хозяина по движку: страницу «Любого сайта» можно перенести в отдельный значок.
        var owner = _panelApps.FirstOrDefault(a => a.View == view);
        if (owner == null || view.CoreWebView2 is not { } core) return;
        if (owner.IsSite && UrlTools.IsWeb(core.Source))
        {
            owner.Url = core.Source;
            QueueSave();
        }
        if (owner == _panelApp) UpdatePanelBar();
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
        PanelCol.MinWidth = show ? PanelMinWidth : 0;
        PanelCol.Width = new GridLength(show ? ClampPanelWidth(_session.PanelWidth) : 0);
        foreach (var a in _panelApps)
            if (a.View != null) a.View.Visibility = show && a == _panelApp ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Строка адреса панели, «Назад», подсказка у пустого «Любого сайта».</summary>
    void UpdatePanelBar()
    {
        var app = _panelApp;
        var core = app?.View?.CoreWebView2;
        if (!PanelAddress.IsKeyboardFocusWithin)
        {
            var url = core?.Source ?? app?.Url ?? "";
            PanelAddress.Text = UrlTools.IsWeb(url) ? url : "";
            PanelAddress.ScrollToHome();
        }
        PanelAddressHint.Visibility = PanelAddress.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelBackBtn.IsEnabled = core?.CanGoBack == true;
        PanelReloadBtn.IsEnabled = core != null;
        PanelEmptyHint.Visibility = app is { IsSite: true, View: null } ? Visibility.Visible : Visibility.Collapsed;
        PanelKeepBtn.Visibility = app is { IsSite: true } && core != null ? Visibility.Visible : Visibility.Collapsed;
    }

    void FocusPanelAddress()
    {
        Dispatcher.BeginInvoke(() =>
        {
            PanelAddress.Focus();
            Keyboard.Focus(PanelAddress);
            PanelAddress.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    void PanelAddress_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var url = UrlTools.ToUrl(PanelAddress.Text, _settings.SearchEngine);
            e.Handled = true;
            if (!UrlTools.IsWeb(url)) { ShowToast("В боковой панели открываются сайты: введите адрес или запрос"); return; }
            NavigatePanelTo(url);
            Dispatcher.BeginInvoke(() => _panelApp?.View?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Keyboard.ClearFocus();
            UpdatePanelBar();
            _panelApp?.View?.Focus();
        }
    }

    /// <summary>Свой сайт приложения (почта в почте) открываем в нём же, всё остальное — в «Любом сайте».</summary>
    void NavigatePanelTo(string url)
    {
        if (_panelApp is { IsSite: false, View.CoreWebView2: { } core } &&
            string.Equals(UrlTools.Host(core.Source), UrlTools.Host(url), StringComparison.OrdinalIgnoreCase))
        {
            try { core.Navigate(url); } catch (ArgumentException) { }
            return;
        }
        OpenInPanel(url);
    }

    void PanelAddress_TextChanged(object sender, TextChangedEventArgs e) =>
        PanelAddressHint.Visibility = PanelAddress.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void PanelAddress_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        PanelAddress.SelectAll();
        PanelAddressBorder.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
    }

    void PanelAddress_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        PanelAddressBorder.BorderBrush = Brushes.Transparent;
        UpdatePanelBar();
    }

    void PanelAddress_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PanelAddress.IsKeyboardFocusWithin) return;
        PanelAddress.Focus();
        e.Handled = true;
    }

    void PanelBack_Click(object sender, RoutedEventArgs e)
    {
        if (_panelApp?.View?.CoreWebView2 is { CanGoBack: true } core) core.GoBack();
    }

    void PanelReload_Click(object sender, RoutedEventArgs e) => _panelApp?.View?.CoreWebView2?.Reload();

    /// <summary>Оставить сайт из «Любого сайта» в панели отдельным значком — страница переезжает без перезагрузки.</summary>
    void PanelKeep_Click(object sender, RoutedEventArgs e)
    {
        if (_siteApp.View is not { CoreWebView2: { } core } view || !UrlTools.IsWeb(core.Source)) return;
        var title = core.DocumentTitle.Trim();
        var name = title.Length is > 0 and <= 24 ? title : UrlTools.SiteKey(UrlTools.Host(core.Source));
        if (AddPanelApp(name, core.Source) is not { } app) return;
        app.View = view;
        _siteApp.View = null;
        _siteApp.Url = "";
        OpenPanel(app);
    }
}

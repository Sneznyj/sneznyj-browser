using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace SneznyjBrowser;

/// <summary>Загрузки: своя кнопка с кольцом прогресса, окошко последних файлов и страница sneznyj://downloads.</summary>
public partial class MainWindow
{
    bool _downloadsThisSession;
    readonly List<Microsoft.Web.WebView2.Wpf.WebView2> _downloadViews = new();
    DispatcherTimer? _downloadsPush;

    string EffectiveDownloadFolder() =>
        _settings.DownloadFolder is { Length: > 0 } f && Directory.Exists(f) ? f : DownloadStore.DefaultFolder();

    void OnDownloadStarting(CoreWebView2 core, CoreWebView2DownloadStartingEventArgs e)
    {
        var op = e.DownloadOperation;
        var deferral = e.GetDeferral();
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                e.Handled = true;   // своё окошко загрузок вместо встроенного из Edge
                var suggested = Path.GetFileName(e.ResultFilePath);
                if (string.IsNullOrWhiteSpace(suggested)) suggested = "download";
                var folder = EffectiveDownloadFolder();
                string path;
                if (_settings.AskDownloadFolder)
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog { FileName = suggested, InitialDirectory = folder, Title = "Сохранить файл" };
                    var ext = Path.GetExtension(suggested);
                    if (ext.Length > 0) dlg.Filter = $"{ext.TrimStart('.').ToUpperInvariant()}|*{ext}|Все файлы|*.*";
                    if (dlg.ShowDialog(this) != true) { e.Cancel = true; return; }
                    path = dlg.FileName;
                }
                else path = UniquePath(Path.Combine(folder, suggested));
                e.ResultFilePath = path;
                Track(op, path);
                // Новая вкладка открылась только ради файла — как в Яндексе, закрываем её.
                var tab = _workspaces.SelectMany(w => w.Tabs).FirstOrDefault(t => t.View?.CoreWebView2 == core);
                if (tab != null && !core.CanGoBack && (core.Source is "" or "about:blank" || core.Source == op.Uri || tab.Url == op.Uri))
                {
                    var view = tab.View;
                    Dispatcher.BeginInvoke(() => CloseTab(tab, keepView: true));
                    op.StateChanged += (_, _) =>
                    {
                        if (op.State == CoreWebView2DownloadState.InProgress || view == null || !_downloadViews.Remove(view)) return;
                        PageHost.Children.Remove(view);
                        try { view.Dispose(); } catch { }
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; i < 1000; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
        return path;
    }

    void Track(CoreWebView2DownloadOperation op, string path)
    {
        var item = new DownloadItem { Url = op.Uri, Path = path, Total = (long)(op.TotalBytesToReceive ?? 0), Operation = op };
        long lastBytes = 0;
        var lastTime = DateTime.Now;
        op.BytesReceivedChanged += (_, _) =>
        {
            item.Received = op.BytesReceived;
            if (op.TotalBytesToReceive is ulong total) item.Total = (long)total;
            var now = DateTime.Now;
            var dt = (now - lastTime).TotalSeconds;
            if (dt >= 1)
            {
                double speed = (item.Received - lastBytes) / dt;
                item.Speed = speed > 0 ? DownloadItem.Size((long)speed) + "/с" : "";
                lastBytes = item.Received;
                lastTime = now;
            }
            UpdateDownloadsButton();
            QueueDownloadsPush();
        };
        op.StateChanged += (_, _) =>
        {
            switch (op.State)
            {
                case CoreWebView2DownloadState.Completed:
                    item.Received = item.Total = (long)op.BytesReceived;
                    item.State = "done";
                    item.Speed = "";
                    item.Operation = null;
                    item.RefreshIcon();
                    ShowToast($"Скачано: {item.FileName}", "Открыть", item.Open);
                    break;
                case CoreWebView2DownloadState.Interrupted:
                    item.State = op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled ? "canceled" : "failed";
                    item.Speed = "";
                    if (item.State == "failed") ShowToast($"Не удалось скачать {item.FileName}", "Загрузки", () => OpenInternal("downloads"));
                    break;
                default:
                    if (item.State != "paused") item.State = "progress";
                    break;
            }
            _downloads.Notify();
        };
        _downloadsThisSession = true;
        _downloads.Add(item);
        ShowToast($"Скачивается: {item.FileName}", "Показать", ShowDownloadsPopup);
    }

    void QueueDownloadsPush()
    {
        if (_downloadsPush == null)
        {
            _downloadsPush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _downloadsPush.Tick += (_, _) =>
            {
                _downloadsPush.Stop();
                InternalPages.Broadcast(AllPageCores("downloads"), DownloadsPayload());
            };
        }
        if (!_downloadsPush.IsEnabled) _downloadsPush.Start();
    }

    void OnDownloadsChanged()
    {
        UpdateDownloadsButton();
        InternalPages.Broadcast(AllPageCores("downloads"), DownloadsPayload());
    }

    void DownloadAction(string id, string action)
    {
        var item = _downloads.Items.FirstOrDefault(d => d.Id == id);
        if (item == null) return;
        switch (action)
        {
            case "open": item.Open(); break;
            case "show": item.ShowInFolder(); break;
            case "cancel": try { item.Operation?.Cancel(); } catch { } break;
            case "pause": try { item.Operation?.Pause(); item.State = "paused"; } catch { } break;
            case "resume": try { item.Operation?.Resume(); item.State = "progress"; } catch { } break;
            case "remove": _downloads.Remove(item); return;
            case "retry":
                _downloads.Remove(item);
                if (_current?.ActiveTab?.View?.CoreWebView2 is { } core) core.Navigate(item.Url);
                else OpenSmart(item.Url);
                return;
        }
        _downloads.Notify();
    }

    void UpdateDownloadsButton()
    {
        var active = _downloads.Items.Where(d => d.IsActive).ToList();
        DownloadsBtn.Visibility = _downloadsThisSession || active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (active.Count == 0)
        {
            DownloadRing.Visibility = DownloadRingBack.Visibility = Visibility.Collapsed;
            DownloadIcon.FontSize = 14;
            DownloadsBtn.ToolTip = "Загрузки (Ctrl+J)";
            return;
        }
        long total = active.Sum(d => d.Total), got = active.Sum(d => d.Received);
        double p = total > 0 ? Math.Clamp((double)got / total, 0.02, 0.999) : 0.25;
        DownloadRing.Data = Arc(11, 11, 10, p);
        DownloadRing.Visibility = DownloadRingBack.Visibility = Visibility.Visible;
        DownloadIcon.FontSize = 10;
        DownloadsBtn.ToolTip = active.Count == 1 ? $"{active[0].FileName}: {active[0].Status}" : $"Скачивается файлов: {active.Count}";
    }

    static Geometry Arc(double cx, double cy, double r, double part)
    {
        double angle = part * 2 * Math.PI;
        var start = new Point(cx, cy - r);
        var end = new Point(cx + r * Math.Sin(angle), cy - r * Math.Cos(angle));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, part > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }

    void DownloadsBtn_Click(object sender, RoutedEventArgs e) => ShowDownloadsPopup();

    /// <summary>Окошко с последними загрузками под кнопкой, как в Яндексе.</summary>
    void ShowDownloadsPopup()
    {
        var list = new StackPanel();
        var recent = _downloads.Items.Take(6).ToList();
        if (recent.Count == 0)
        {
            var empty = new TextBlock { Text = "Загрузок пока нет", Margin = new Thickness(8, 10, 8, 10) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            list.Children.Add(empty);
        }
        Popup? popup = null;
        foreach (var d in recent) list.Children.Add(DownloadRow(d, () => popup!.IsOpen = false));

        var header = new DockPanel { Margin = new Thickness(8, 2, 4, 8) };
        var open = new Button { Content = "Открыть папку", Padding = new Thickness(0) };
        open.SetResourceReference(StyleProperty, "LinkButton");
        open.Click += (_, _) => { popup!.IsOpen = false; OpenFolder(EffectiveDownloadFolder()); };
        DockPanel.SetDock(open, Dock.Right);
        header.Children.Add(open);
        header.Children.Add(new TextBlock { Text = "Загрузки", FontSize = 15, FontWeight = FontWeights.SemiBold });

        var footer = new Button { Content = "Все загрузки", Margin = new Thickness(0, 6, 0, 0), Height = 32 };
        footer.SetResourceReference(StyleProperty, "TextButton");
        footer.Click += (_, _) => { popup!.IsOpen = false; OpenInternal("downloads"); };

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(list);
        body.Children.Add(footer);
        var card = new Border
        {
            Width = 380, Padding = new Thickness(10, 12, 10, 10), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 16, 16), Child = body, Effect = (System.Windows.Media.Effects.Effect)FindResource("PopupShadow"),
        };
        card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
        TextElement.SetFontFamily(card, FontFamily);
        card.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        popup = new Popup
        {
            Child = card, PlacementTarget = DownloadsBtn.IsVisible ? DownloadsBtn : MenuBtn, Placement = PlacementMode.Bottom,
            HorizontalOffset = -340, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
        };
        _openMenus++;
        popup.Closed += (_, _) => _openMenus = Math.Max(0, _openMenus - 1);
        popup.IsOpen = true;
    }

    FrameworkElement DownloadRow(DownloadItem d, Action close)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, DataContext = d };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bg = new Border { CornerRadius = new CornerRadius(9) };
        Grid.SetColumnSpan(bg, 3);
        grid.Children.Add(bg);
        grid.MouseEnter += (_, _) => bg.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        grid.MouseLeave += (_, _) => bg.Background = Brushes.Transparent;

        var iconHost = new Grid { Width = 24, Height = 24, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var glyph = new TextBlock { Text = "", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var img = new Image { Width = 24, Height = 24 };
        img.SetBinding(Image.SourceProperty, new Binding(nameof(DownloadItem.Icon)));
        iconHost.Children.Add(glyph);
        iconHost.Children.Add(img);
        grid.Children.Add(iconHost);

        var text = new StackPanel { Margin = new Thickness(8, 7, 8, 7), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = d.FileName, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = d.Path };
        var status = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(DownloadItem.Status)));
        var bar = new ProgressBar { Height = 3, Maximum = 1, Margin = new Thickness(0, 5, 0, 0), BorderThickness = new Thickness(0) };
        bar.SetResourceReference(ForegroundProperty, "AccentBrush");
        bar.SetResourceReference(BackgroundProperty, "LineBrush");
        bar.SetBinding(RangeBase.ValueProperty, new Binding(nameof(DownloadItem.Progress)) { Mode = BindingMode.OneWay });
        bar.SetBinding(VisibilityProperty, new Binding(nameof(DownloadItem.IsActive)) { Converter = new BooleanToVisibilityConverter() });
        text.Children.Add(name);
        text.Children.Add(status);
        text.Children.Add(bar);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        Button A(string g, string tip, Action run)
        {
            var b = new Button { Content = g, ToolTip = tip, Width = 30, Height = 30, MinWidth = 0, FontSize = 13 };
            b.SetResourceReference(StyleProperty, "IconButton");
            b.Click += (_, e) => { run(); e.Handled = true; };
            return b;
        }
        if (d.IsActive) actions.Children.Add(A("", "Отменить", () => DownloadAction(d.Id, "cancel")));
        actions.Children.Add(A("", "Показать в папке", () => { close(); d.ShowInFolder(); }));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        grid.MouseLeftButtonUp += (_, _) =>
        {
            if (!d.IsDone) return;
            close();
            d.Open();
        };
        return grid;
    }
}

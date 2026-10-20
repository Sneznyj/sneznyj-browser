using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SneznyjBrowser;

/// <summary>
/// «Умная строка»: подсказки из открытых вкладок, закладок и истории, дописывание адреса прямо в строке
/// (you → youtube.com), а подсказки поисковика — только если их включить в настройках.
/// </summary>
public partial class MainWindow
{
    Popup? _suggestPopup;
    ListBox? _suggestList;
    bool _settingAddress, _suppressComplete;
    string _typed = "";
    CancellationTokenSource? _suggestCts;
    static readonly HttpClient SuggestClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    void SetAddressText(string text)
    {
        _settingAddress = true;
        AddressBox.Text = text;
        _settingAddress = false;
        _typed = text;
    }

    void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        if (_settingAddress || !AddressBox.IsKeyboardFocusWithin) return;
        var text = AddressBox.Text;
        bool grew = text.Length > _typed.Length && text.StartsWith(_typed, StringComparison.OrdinalIgnoreCase);
        _typed = text;
        if (!_suppressComplete && grew && AddressBox.CaretIndex == text.Length) InlineComplete(text);
        _suppressComplete = false;
        BuildSuggestions(_typed);
    }

    /// <summary>Дописывает адрес сайта, который вы часто открываете: набрали «you» — в строке «youtube.com».</summary>
    void InlineComplete(string typed)
    {
        var t = typed.Trim();
        if (t.Length < 1 || t.Contains(' ') || t.Contains('/')) return;
        var lower = t.ToLowerInvariant();
        string? best = null;
        foreach (var h in _history.Ranked().Take(400))
        {
            var site = UrlTools.SiteKey(UrlTools.Host(h.Url));
            if (site.Length > lower.Length && site.StartsWith(lower)) { best = site; break; }
        }
        best ??= _bookmarks.AllLinks().Select(b => UrlTools.SiteKey(UrlTools.Host(b.Url)))
            .FirstOrDefault(s => s.Length > lower.Length && s.StartsWith(lower));
        if (best == null) return;
        _settingAddress = true;
        AddressBox.Text = typed + best[t.Length..];
        AddressBox.Select(typed.Length, AddressBox.Text.Length - typed.Length);
        _settingAddress = false;
    }

    void BuildSuggestions(string typed)
    {
        var q = typed.Trim();
        if (q.Length == 0) { HideSuggestions(); return; }
        var full = AddressBox.Text.Trim();
        var match = TextMatch.Matcher(q);
        var items = new List<PaletteItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (UrlTools.LooksLikeAddress(full))
        {
            var url = UrlTools.ToUrl(full, _settings.SearchEngine);
            items.Add(new PaletteItem { Glyph = "", Title = UrlTools.Display(url), Kind = "Перейти", Run = () => NavigateTyped(url, false) });
            seen.Add(url);
        }
        else
        {
            items.Add(new PaletteItem
            {
                Glyph = "", Title = q, Kind = $"Искать в {UrlTools.EngineName(_settings.SearchEngine)}",
                Run = () => NavigateTyped(UrlTools.SearchUrl(_settings.SearchEngine, q), false),
            });
        }

        foreach (var t in _workspaces.SelectMany(w => w.Tabs).Where(t => t != _current?.ActiveTab && !UrlTools.IsNewTab(t.Url) && match(t.Title + " " + t.Url)).Take(3))
        {
            var tab = t;
            items.Add(new PaletteItem
            {
                Glyph = tab.FallbackGlyph, Icon = tab.Favicon, Title = tab.Title, Subtitle = UrlTools.Display(tab.Url),
                Kind = "Открытая вкладка", Run = () => { ActivateTab(tab); FocusPage(); },
            });
            seen.Add(tab.Url);
        }
        foreach (var b in _bookmarks.AllLinks().Where(b => match(b.Title + " " + b.Url) && !seen.Contains(b.Url!)).Take(3))
        {
            var node = b;
            items.Add(new PaletteItem
            {
                Glyph = "", Icon = node.Icon, Title = node.Title, Subtitle = node.Url ?? "", Kind = "Закладка",
                Run = () => NavigateTyped(node.Url!, false),
            });
            seen.Add(node.Url!);
        }
        foreach (var h in _history.Search(match, 12).Where(h => !seen.Contains(h.Url)).Take(5))
        {
            var entry = h;
            items.Add(new PaletteItem
            {
                Glyph = "", Icon = Favicons.Load(UrlTools.Host(entry.Url)),
                Title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title, Subtitle = entry.Url, Kind = "История",
                Run = () => NavigateTyped(entry.Url, false),
            });
        }
        ShowSuggestions(items);
        if (_settings.SearchSuggestions && !UrlTools.LooksLikeAddress(q) && q.Length >= 2) FetchEngineSuggestions(q);
    }

    void ShowSuggestions(List<PaletteItem> items)
    {
        if (_suggestPopup == null)
        {
            _suggestList = new ListBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), MaxHeight = 440, Focusable = false,
                Margin = new Thickness(0, 6, 0, 6),
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_suggestList, ScrollBarVisibility.Disabled);
            _suggestList.ItemTemplate = (DataTemplate)FindResource("PaletteItemTemplate");
            _suggestList.ItemContainerStyle = (Style)FindResource("PaletteItemStyle");
            _suggestList.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(_suggestList, d) is ListBoxItem { DataContext: PaletteItem item })
                {
                    e.Handled = true;
                    HideSuggestions();
                    item.Run();
                }
            };
            var card = new Border
            {
                CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Child = _suggestList,
                Margin = new Thickness(0, 0, 16, 16), Effect = (System.Windows.Media.Effects.Effect)FindResource("PopupShadow"),
            };
            card.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
            TextElement.SetFontFamily(card, FontFamily);
            TextElement.SetFontSize(card, 13);
            card.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
            _suggestPopup = new Popup
            {
                Child = card, PlacementTarget = AddressBorder, Placement = PlacementMode.Bottom, VerticalOffset = 4,
                StaysOpen = true, AllowsTransparency = true, Focusable = false,
            };
            Deactivated += (_, _) => HideSuggestions();
        }
        if (items.Count == 0) { HideSuggestions(); return; }
        _suggestList!.ItemsSource = items;
        _suggestList.SelectedIndex = 0;
        ((Border)_suggestPopup.Child).Width = AddressBorder.ActualWidth + 16;
        _suggestPopup.IsOpen = true;
    }

    void HideSuggestions()
    {
        _suggestCts?.Cancel();
        if (_suggestPopup != null) _suggestPopup.IsOpen = false;
    }

    async void FetchEngineSuggestions(string q)
    {
        _suggestCts?.Cancel();
        var cts = _suggestCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(140, cts.Token);
            var e = Uri.EscapeDataString(q);
            var url = _settings.SearchEngine switch
            {
                "yandex" => $"https://suggest.yandex.ru/suggest-ff.cgi?part={e}&uil=ru",
                "duckduckgo" => $"https://duckduckgo.com/ac/?type=list&q={e}",
                "bing" => $"https://api.bing.com/osjson.aspx?query={e}",
                "brave" => $"https://search.brave.com/api/suggest?q={e}",
                _ => $"https://suggestqueries.google.com/complete/search?client=firefox&hl=ru&ie=utf-8&oe=utf-8&q={e}",
            };
            var json = await SuggestClient.GetStringAsync(url, cts.Token);
            if (cts.IsCancellationRequested || _suggestPopup?.IsOpen != true || _typed.Trim() != q) return;
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 2) return;
            var words = arr[1].EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0 && !s.Equals(q, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
            if (words.Count == 0 || _suggestList?.ItemsSource is not List<PaletteItem> current) return;
            var list = new List<PaletteItem>(current);
            int at = 1;
            foreach (var w in words)
            {
                var word = w;
                list.Insert(at++, new PaletteItem
                {
                    Glyph = "", Title = word, Kind = "Подсказка поисковика",
                    Run = () => NavigateTyped(UrlTools.SearchUrl(_settings.SearchEngine, word), false),
                });
            }
            int sel = _suggestList.SelectedIndex;
            _suggestList.ItemsSource = list;
            _suggestList.SelectedIndex = Math.Max(0, sel);
        }
        catch { }
    }

    /// <summary>Для проверки без клавиатуры: показать подсказки, как будто это напечатали.</summary>
    void ShowSuggestionsFor(string text)
    {
        SetAddressText(text);
        _typed = text;
        BuildSuggestions(text);
    }

    void NavigateTyped(string url, bool newTab)
    {
        HideSuggestions();
        if (url.Length == 0 || _current?.ActiveTab is not { } tab) return;
        if (newTab || tab.IsPinned && !UrlTools.IsNewTab(tab.Url) && UrlTools.Host(url) != UrlTools.Host(tab.Url))
            ActivateTab(NewTab(_current, url, null, NextIndexAfter(tab), tab.IsPrivate, tab.Group));
        else
            Navigate(tab, url);
        FocusPage();
    }

    void AddressBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool open = _suggestPopup?.IsOpen == true && _suggestList is { Items.Count: > 0 };
        switch (key)
        {
            case Key.Back or Key.Delete:
                _suppressComplete = true;
                break;
            case Key.Down when open:
                _suggestList!.SelectedIndex = (_suggestList.SelectedIndex + 1) % _suggestList.Items.Count;
                _suggestList.ScrollIntoView(_suggestList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when open:
                _suggestList!.SelectedIndex = (_suggestList.SelectedIndex - 1 + _suggestList.Items.Count) % _suggestList.Items.Count;
                _suggestList.ScrollIntoView(_suggestList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
            {
                e.Handled = true;
                if (open && _suggestList!.SelectedIndex > 0 && _suggestList.SelectedItem is PaletteItem item)
                {
                    HideSuggestions();
                    item.Run();
                    return;
                }
                var text = AddressBox.Text.Trim();
                // Ctrl+Enter: «vk» → https://www.vk.com
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !text.Contains('.') && !text.Contains(' ') && text.Length > 0)
                    text = $"www.{text}.com";
                var url = UrlTools.ToUrl(text, _settings.SearchEngine);
                NavigateTyped(url, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
                break;
            }
            case Key.Escape:
                e.Handled = true;
                if (open)
                {
                    HideSuggestions();
                    if (AddressBox.SelectionLength > 0 && AddressBox.SelectionStart + AddressBox.SelectionLength == AddressBox.Text.Length)
                        SetAddressText(AddressBox.Text[..AddressBox.SelectionStart]);
                }
                else
                {
                    SetAddressText(UrlTools.Display(_current?.ActiveTab?.Url));
                    FocusPage();
                }
                break;
        }
    }

    void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBox.SelectAll();
        AddressBorder.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _typed = AddressBox.Text;
    }

    void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBorder.BorderBrush = Brushes.Transparent;
        HideSuggestions();
        UpdateChrome();
    }

    void AddressBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (AddressBox.IsKeyboardFocusWithin) return;
        AddressBox.Focus();
        e.Handled = true;
    }
}

using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace SneznyjBrowser;

/// <summary>Режим чтения: статья без рекламы и меню, своим шрифтом и в цветах темы (sneznyj://reader).</summary>
public partial class MainWindow
{
    readonly Dictionary<string, JsonElement> _readerArticles = new();

    /// <summary>Вытаскивает со страницы заголовки, абзацы, списки и картинки основной статьи.</summary>
    const string ReaderScript = """
    (() => {
      const abs = u => { try { return new URL(u, location.href).href; } catch { return ''; } };
      const len = el => (el.innerText || '').trim().length;
      let best = null, bestScore = 0;
      for (const c of document.querySelectorAll('article, main, [role=main], section, div')) {
        let score = 0;
        for (const p of c.querySelectorAll(':scope > p, :scope > div > p, :scope > section > p')) score += Math.min(len(p), 1200);
        if (c.tagName === 'ARTICLE') score *= 1.3;
        if (score > bestScore) { bestScore = score; best = c; }
      }
      if (!best || bestScore < 400) return { blocks: [] };
      const blocks = [];
      const img = (n, cap) => {
        const src = abs(n.currentSrc || n.src || n.dataset.src || '');
        if (/^https?:/i.test(src) && (n.naturalWidth || n.width || 0) >= 120) blocks.push({ t: 'img', src, alt: n.alt || '', cap: cap || '' });
      };
      const walk = el => {
        for (const n of el.children) {
          if (blocks.length > 3000) return;
          const tag = n.tagName;
          if (/^(SCRIPT|STYLE|NAV|ASIDE|FOOTER|HEADER|FORM|BUTTON|IFRAME|NOSCRIPT|SVG|INPUT|SELECT|TEXTAREA)$/.test(tag)) continue;
          const st = getComputedStyle(n);
          if (st.display === 'none' || st.visibility === 'hidden' || st.position === 'fixed') continue;
          if (/^H[1-6]$/.test(tag)) { const x = n.innerText.trim(); if (x) blocks.push({ t: tag.toLowerCase(), x }); continue; }
          if (tag === 'P') { const x = n.innerText.trim(); if (x.length > 1) blocks.push({ t: 'p', x }); n.querySelectorAll('img').forEach(i => img(i)); continue; }
          if (tag === 'IMG') { img(n); continue; }
          if (tag === 'FIGURE') { const i = n.querySelector('img'); if (i) img(i, n.querySelector('figcaption')?.innerText.trim()); continue; }
          if (tag === 'BLOCKQUOTE') { const x = n.innerText.trim(); if (x) blocks.push({ t: 'q', x }); continue; }
          if (tag === 'PRE') { blocks.push({ t: 'pre', x: n.innerText }); continue; }
          if (tag === 'UL' || tag === 'OL') {
            const items = [...n.children].filter(li => li.tagName === 'LI').map(li => li.innerText.trim()).filter(Boolean);
            if (items.length && items.join('').length > 20) blocks.push({ t: tag.toLowerCase(), items });
            continue;
          }
          walk(n);
        }
      };
      walk(best);
      const h1 = document.querySelector('h1');
      return { title: (h1 ? h1.innerText : document.title || '').trim(), url: location.href, site: location.hostname.replace(/^www\./, ''), blocks };
    })()
    """;

    async void OpenReader()
    {
        if (_current?.ActiveTab is not { View.CoreWebView2: { } core } tab) return;
        if (UrlTools.InternalPage(tab.Url) == "reader") { GoBack(); return; }
        if (!UrlTools.IsWeb(tab.Url)) { ShowToast("Режим чтения работает на страницах сайтов"); return; }
        try
        {
            var json = await core.ExecuteScriptAsync(ReaderScript);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("blocks", out var blocks) || blocks.GetArrayLength() < 3)
            {
                ShowToast("На этой странице не нашлось статьи для режима чтения");
                return;
            }
            var id = Guid.NewGuid().ToString("N")[..10];
            _readerArticles[id] = root.Clone();
            if (_readerArticles.Count > 20) _readerArticles.Remove(_readerArticles.Keys.First());
            Navigate(tab, UrlTools.PageUrl("reader") + "?id=" + id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            ShowToast("Не получилось открыть режим чтения");
        }
    }

    object ReaderPayload(string id) =>
        _readerArticles.TryGetValue(id, out var a) ? new { type = "reader", article = a } : new { type = "reader", article = (object?)null };

    /// <summary>Есть ли на странице статья: показываем в адресной строке значок «Режим чтения».</summary>
    async void CheckReadable(BrowserTab tab, CoreWebView2 core)
    {
        tab.CanRead = false;
        if (!UrlTools.IsWeb(tab.Url)) { if (tab == _current?.ActiveTab) UpdateReaderButton(); return; }
        try
        {
            var r = await core.ExecuteScriptAsync(
                "(() => { let n = 0; for (const p of document.querySelectorAll('article p, main p, p')) { n += (p.innerText || '').length; if (n > 2500) return true; } return false; })()");
            tab.CanRead = r == "true";
        }
        catch { }
        if (tab == _current?.ActiveTab) UpdateReaderButton();
    }

    void UpdateReaderButton()
    {
        var tab = _current?.ActiveTab;
        bool reading = UrlTools.InternalPage(tab?.Url) == "reader";
        ReaderBtn.Visibility = tab != null && (tab.CanRead || reading) ? Visibility.Visible : Visibility.Collapsed;
        ReaderBtn.ToolTip = reading ? "Выйти из режима чтения" : "Режим чтения: только текст статьи";
        if (reading) ReaderBtn.SetResourceReference(ForegroundProperty, "AccentBrush");
        else ReaderBtn.SetResourceReference(ForegroundProperty, "MutedBrush");
    }

    void Reader_Click(object sender, RoutedEventArgs e) => OpenReader();
}

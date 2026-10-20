using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SneznyjBrowser;

/// <summary>
/// Крупные значки сайтов для плиток стартовой (как в Табло Яндекса). Берём их прямо с самого сайта:
/// apple-touch-icon и значки из &lt;link rel="icon" sizes&gt; на главной, без сторонних сервисов,
/// которые узнали бы, какие сайты у вас на стартовой.
/// </summary>
public static class SiteLogos
{
    const int MaxSide = 128;
    static readonly HttpClient Http = CreateClient();
    static readonly HashSet<string> Tried = new(StringComparer.OrdinalIgnoreCase);
    static readonly Regex LinkTag = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Dir => Path.Combine(AppPaths.Start, "logo");
    public static string PathFor(string host) => Path.Combine(Dir, Favicons.FileName(host));
    public static string? RelUrl(string host) =>
        host.Length > 0 && File.Exists(PathFor(host)) ? "logo/" + Favicons.FileName(host) : null;

    /// <summary>Сайты без крупного значка, которые в этот запуск ещё не пробовали.</summary>
    public static List<string> Missing(IEnumerable<string> urls)
    {
        lock (Tried)
            return urls.Select(UrlTools.Host).Where(h => h.Length > 0 && !UrlTools.IsLocalHost(h) && RelUrl(h) == null && !Tried.Contains(h))
                       .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Скачивает значки; onEach — после каждого скачанного (из фонового потока).</summary>
    public static async Task FetchAsync(IEnumerable<string> hosts, Action onEach)
    {
        var list = new List<string>();
        lock (Tried)
            foreach (var h in hosts)
                if (Tried.Add(h)) list.Add(h);
        if (list.Count == 0) return;
        try { Directory.CreateDirectory(Dir); } catch { return; }
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(list.Select(async host =>
        {
            await gate.WaitAsync();
            try { if (await FetchOne(host)) onEach(); }
            catch { }
            finally { gate.Release(); }
        }));
    }

    static async Task<bool> FetchOne(string host)
    {
        var candidates = new List<(Uri Url, int Size)>();
        var home = new Uri($"https://{host}/");
        try
        {
            using var resp = await Http.GetAsync(home, HttpCompletionOption.ResponseHeadersRead);
            if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentType?.MediaType?.Contains("html") == true)
            {
                var html = await ReadLimited(resp, 768 * 1024);
                var baseUri = resp.RequestMessage?.RequestUri ?? home;
                // Без входа почта, диск и т. п. перекидывают на страницу входа — её значок не тот.
                if (!SameSite(baseUri.Host, host)) html = "";
                foreach (Match m in LinkTag.Matches(html))
                {
                    var tag = m.Value;
                    var rel = Attr(tag, "rel").ToLowerInvariant();
                    var href = WebUtility.HtmlDecode(Attr(tag, "href")).Trim();
                    if (href.Length == 0 || !rel.Contains("icon") || rel.Contains("mask-icon")) continue;
                    if (href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || Attr(tag, "type").Contains("svg")) continue;
                    if (!Uri.TryCreate(baseUri, href, out var url) || url.Scheme is not ("https" or "http")) continue;
                    int size = 0;
                    foreach (var part in Attr(tag, "sizes").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        if (part.Split('x', 'X') is [var w, _] && int.TryParse(w, out var n)) size = Math.Max(size, n);
                    if (size == 0) size = rel.Contains("apple-touch-icon") ? 180 : 32;
                    candidates.Add((url, size));
                }
            }
        }
        catch { }
        candidates.Add((new Uri($"https://{host}/apple-touch-icon.png"), 180));
        candidates.Add((new Uri($"https://{host}/favicon.ico"), 32));

        // Сначала крупные (но без огромных картинок), одинаковые адреса — один раз.
        var ordered = candidates.GroupBy(c => c.Url.AbsoluteUri).Select(g => g.First())
            .OrderByDescending(c => c.Size is >= 48 and <= 512 ? c.Size : c.Size > 512 ? 47 : c.Size).Take(5);
        foreach (var (url, _) in ordered)
        {
            try
            {
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) continue;
                // mail.google.com/apple-touch-icon.png → www.google.com/apple-touch-icon.png: это значок другого сайта.
                var final = resp.RequestMessage?.RequestUri ?? url;
                if (final.Host != url.Host && final.AbsolutePath == url.AbsolutePath) continue;
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                if (bytes.Length is < 64 or > 3_000_000) continue;
                var png = await Task.Run(() => ToPng(bytes));
                if (png == null) continue;
                File.WriteAllBytes(PathFor(home.Host), png);
                return true;
            }
            catch { }
        }
        return false;
    }

    static bool SameSite(string a, string b) =>
        string.Equals(UrlTools.SiteKey(a), UrlTools.SiteKey(b), StringComparison.OrdinalIgnoreCase);

    static byte[]? ToPng(byte[] data)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames.OrderByDescending(f => f.PixelWidth <= 512 ? f.PixelWidth : 0).First();
            if (frame.PixelWidth < 16) return null;
            if (frame.PixelWidth > MaxSide)
            {
                double k = (double)MaxSide / frame.PixelWidth;
                frame = new TransformedBitmap(frame, new ScaleTransform(k, k));
            }
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(frame));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    static async Task<string> ReadLimited(HttpResponseMessage resp, int max)
    {
        await using var s = await resp.Content.ReadAsStreamAsync();
        var buf = new byte[max];
        int total = 0, n;
        while (total < max && (n = await s.ReadAsync(buf.AsMemory(total, max - total))) > 0) total += n;
        return Encoding.UTF8.GetString(buf, 0, total);
    }

    static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\b{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value : "";
    }

    static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(6),
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
        return http;
    }
}

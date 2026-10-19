using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace SneznyjBrowser;

/// <summary>Значки сайтов: кэш на диске, чтобы вкладки и стартовая показывали их сразу.</summary>
public static class Favicons
{
    /// <summary>Появились новые значки — стартовую стоит пересобрать.</summary>
    public static bool Dirty { get; set; }

    public static string FileName(string host)
    {
        var sb = new StringBuilder();
        foreach (var ch in UrlTools.SiteKey(host.ToLowerInvariant()))
            sb.Append(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' ? ch : '_');
        return sb + ".png";
    }

    public static string PathFor(string host) => Path.Combine(AppPaths.Fav, FileName(host));

    public static ImageSource? Load(string host)
    {
        if (string.IsNullOrEmpty(host)) return null;
        var path = PathFor(host);
        if (!File.Exists(path)) return null;
        try { return Decode(File.ReadAllBytes(path)); } catch { return null; }
    }

    public static async Task<ImageSource?> FromCore(CoreWebView2 core, string host, bool save)
    {
        if (string.IsNullOrEmpty(core.FaviconUri)) return null;
        using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        if (ms.Length == 0) return null;
        var bytes = ms.ToArray();
        if (save && !string.IsNullOrEmpty(host))
        {
            try
            {
                var path = PathFor(host);
                if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
                {
                    File.WriteAllBytes(path, bytes);
                    Dirty = true;
                }
            }
            catch { }
        }
        return Decode(bytes);
    }

    static ImageSource Decode(byte[] bytes)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = new MemoryStream(bytes);
        img.EndInit();
        img.Freeze();
        return img;
    }
}

using System.IO;
using System.Reflection;

namespace SneznyjBrowser;

/// <summary>
/// Блокировщик рекламы и трекеров по списку доменов.
/// Каждый домен превращается в фильтр движка, поэтому обычные запросы
/// до программы вообще не доходят и страницы не замедляются.
/// </summary>
public sealed class AdBlocker
{
    readonly HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);

    public AdBlocker()
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("blocklist.txt"))
            if (s != null) Read(new StreamReader(s));

        // Свой список: по домену в строке, можно в формате hosts («0.0.0.0 domain»).
        try
        {
            if (File.Exists(AppPaths.CustomBlocklist))
                using (var r = new StreamReader(AppPaths.CustomBlocklist)) Read(r);
            else
                File.WriteAllText(AppPaths.CustomBlocklist,
                    "# Свои домены для блокировки, по одному в строке. Работает после перезапуска.\n");
        }
        catch { }

        Patterns = _domains.SelectMany(d => new[] { $"*://{d}/*", $"*://*.{d}/*" }).ToArray();
    }

    public int Count => _domains.Count;
    public string[] Patterns { get; }

    void Read(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var s = line.Trim();
            int hash = s.IndexOf('#');
            if (hash >= 0) s = s[..hash].Trim();
            if (s.Length == 0) continue;
            var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            s = parts[^1].ToLowerInvariant();
            if (s is "localhost" or "0.0.0.0" or "127.0.0.1") continue;
            if (s.Contains('.') && !s.Contains('/') && !s.Contains('*')) _domains.Add(s);
        }
    }
}

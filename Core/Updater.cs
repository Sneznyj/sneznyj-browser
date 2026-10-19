using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;

namespace SneznyjBrowser;

/// <summary>
/// Обновление оболочки браузера. Раз в сутки скачивается маленький update.json с GitHub Releases;
/// если там версия новее и подпись сходится с вшитым ключом — браузер предлагает обновиться.
/// Установщик скачивается в папку данных, сверяется по размеру и SHA-256 из подписанного файла
/// и запускается с --update: он ждёт, пока браузер закроется, ставит новую версию и открывает его снова.
/// </summary>
public sealed class Updater
{
    public const string DefaultUrl = "https://github.com/Sneznyj/sneznyj-browser/releases/latest/download/update.json";

    /// <summary>Открытый ключ выпусков (ECDSA P-256). Закрытый — только у автора: %APPDATA%\Sneznyj\browser-update-key.pem.</summary>
    const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAETOoB7JseLYroCFD/SbH8aiukm2FqJ3z72mooEpxl5VNbJdo/mxH7s4v543fJ24m09uVsf0OdUuyyrF2dKCvSKg==";

    static readonly TimeSpan CheckEvery = TimeSpan.FromHours(20);

    public static Version Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    /// <summary>Установленная программа, а не запуск из папки сборки — только её есть смысл обновлять.</summary>
    public static bool IsInstalled
    {
        get
        {
            var exe = Environment.ProcessPath ?? "";
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SneznyjBrowser");
            return string.Equals(Path.GetDirectoryName(exe), dir, StringComparison.OrdinalIgnoreCase);
        }
    }

    readonly SettingsData _settings;
    readonly UpdateState _state;
    readonly HttpClient _http;

    public Updater(SettingsData settings)
    {
        _settings = settings;
        _state = Json.Load<UpdateState>(AppPaths.UpdateStateFile);
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"SneznyjBrowser/{CurrentText}");
    }

    /// <summary>Уже найденное обновление, которое не отложено: показать сразу при запуске, без сети.</summary>
    public UpdateManifest? Pending()
    {
        var m = _state.Available;
        if (m == null || !IsNewer(m) || !m.Verify(PublicKey)) return null;
        return _state.PostponedUntil is DateTime until && until > DateTime.UtcNow ? null : m;
    }

    public bool CheckDue => _settings.CheckUpdates && (_state.LastCheck is not DateTime last || DateTime.UtcNow - last >= CheckEvery);

    /// <summary>Скачать update.json. Возвращает новую версию или null, если у нас последняя.</summary>
    public async Task<UpdateManifest?> CheckAsync(CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(_settings.UpdateUrl) ? DefaultUrl : _settings.UpdateUrl.Trim();
        if (!IsAllowedUrl(url)) throw new InvalidOperationException("адрес обновлений должен быть https");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        var json = await _http.GetStringAsync(AddNoCache(url), cts.Token);
        var m = UpdateManifest.Parse(json) ?? throw new InvalidDataException("пустой файл версии");
        if (m.Product != "SneznyjBrowser" || !m.Verify(PublicKey))
            throw new CryptographicException("подпись файла версии не сходится — обновление отклонено");
        if (!IsAllowedUrl(m.Url)) throw new InvalidDataException("установщик должен скачиваться по https");

        _state.LastCheck = DateTime.UtcNow;
        bool newer = IsNewer(m);
        if (newer && _state.Available?.Version != m.Version) _state.PostponedUntil = null;
        _state.Available = newer ? m : null;
        SaveState();
        return newer ? m : null;
    }

    /// <summary>Установщик прошлого обновления уже не нужен (к этому моменту он давно закрылся).</summary>
    public static void CleanDownloads()
    {
        if (!Directory.Exists(AppPaths.Updates)) return;
        foreach (var f in Directory.GetFiles(AppPaths.Updates))
            try { File.Delete(f); } catch { }
    }

    public void Postpone()
    {
        _state.PostponedUntil = DateTime.UtcNow.AddDays(1);
        SaveState();
    }

    /// <summary>Скачать установщик и сверить его с подписанным update.json. Возвращает путь к проверенному файлу.</summary>
    public async Task<string> DownloadAsync(UpdateManifest m, IProgress<double> progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(AppPaths.Updates);
        foreach (var old in Directory.GetFiles(AppPaths.Updates))
            try { File.Delete(old); } catch { }

        var target = Path.Combine(AppPaths.Updates, $"SneznyjBrowser-Setup-{m.Version}.exe");
        var part = target + ".part";
        try
        {
            await DownloadToAsync(m, part, progress, ct);
        }
        catch
        {
            try { File.Delete(part); } catch { }
            throw;
        }
        File.Move(part, target, true);
        return target;
    }

    async Task DownloadToAsync(UpdateManifest m, string part, IProgress<double> progress, CancellationToken ct)
    {
        using (var response = await _http.GetAsync(m.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(part);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 16];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > m.Size) throw new InvalidDataException("файл больше, чем указано в подписанной версии");
                sha.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                progress.Report((double)total / m.Size);
            }
            if (total != m.Size) throw new InvalidDataException("файл скачался не полностью");
            var hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            if (!string.Equals(hash, m.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("контрольная сумма не совпала — файл повреждён или подменён");
        }
    }

    /// <summary>Запустить проверенный установщик: он дождётся закрытия браузера и откроет новую версию.</summary>
    public static void RunInstaller(string path, bool noActivate)
    {
        var args = "--update --launch" + (noActivate ? " --no-activate" : "");
        Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = false, WorkingDirectory = AppPaths.Updates });
    }

    static bool IsNewer(UpdateManifest m) => m.ParsedVersion() is { } v && Normalize(v) > Current;

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>Только https; http — лишь для своего компьютера (тестовый сервер).</summary>
    static bool IsAllowedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback));

    static string AddNoCache(string url) => url + (url.Contains('?') ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks;

    void SaveState()
    {
        try { Json.Save(AppPaths.UpdateStateFile, _state); } catch { }
    }
}

public sealed class UpdateState
{
    public DateTime? LastCheck { get; set; }
    public DateTime? PostponedUntil { get; set; }
    public UpdateManifest? Available { get; set; }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace SneznyjBrowser;

/// <summary>Одна загрузка: идёт, готова, прервана.</summary>
public sealed class DownloadItem : Observable
{
    long _received, _total;
    string _state = "progress";
    string _speed = "";
    ImageSource? _icon;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Url { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime Started { get; set; } = DateTime.Now;

    public long Received { get => _received; set { if (Set(ref _received, value)) RaiseStatus(); } }
    public long Total { get => _total; set { if (Set(ref _total, value)) RaiseStatus(); } }

    /// <summary>progress, done, failed, canceled, paused</summary>
    public string State
    {
        get => _state;
        set { if (Set(ref _state, value)) { RaiseStatus(); Raise(nameof(IsActive)); Raise(nameof(IsDone)); } }
    }

    [JsonIgnore] public string Speed { get => _speed; set { if (Set(ref _speed, value)) RaiseStatus(); } }
    [JsonIgnore] public CoreWebView2DownloadOperation? Operation { get; set; }
    [JsonIgnore] public string FileName => System.IO.Path.GetFileName(Path);
    [JsonIgnore] public bool IsActive => _state is "progress" or "paused";
    [JsonIgnore] public bool IsDone => _state == "done";
    [JsonIgnore] public double Progress => _total > 0 ? Math.Clamp((double)_received / _total, 0, 1) : 0;
    [JsonIgnore] public bool Exists => File.Exists(Path);

    [JsonIgnore]
    public string Status => _state switch
    {
        "progress" => _total > 0
            ? $"{Size(_received)} из {Size(_total)}" + (_speed.Length > 0 ? $" · {_speed}" : "")
            : Size(_received) + (_speed.Length > 0 ? $" · {_speed}" : ""),
        "paused" => $"Пауза · {Size(_received)}" + (_total > 0 ? $" из {Size(_total)}" : ""),
        "done" => Exists ? Size(_total > 0 ? _total : _received) : "Файл удалён",
        "canceled" => "Отменено",
        _ => "Не удалось скачать",
    };

    [JsonIgnore]
    public ImageSource? Icon
    {
        get
        {
            if (_icon != null) return _icon;
            try
            {
                var path = File.Exists(Path) ? Path : null;
                if (path == null) return null;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                _icon = src;
            }
            catch { }
            return _icon;
        }
    }

    void RaiseStatus()
    {
        Raise(nameof(Status));
        Raise(nameof(Progress));
    }

    public void RefreshIcon()
    {
        _icon = null;
        Raise(nameof(Icon));
        Raise(nameof(Status));
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0} КБ",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.0} МБ",
        _ => $"{bytes / 1073741824.0:0.00} ГБ",
    };

    public object ToPageData() => new
    {
        id = Id, url = Url, path = Path, name = FileName, state = State, received = Received, total = Total,
        status = Status, progress = Progress, started = Started.ToString("o"), exists = Exists,
    };

    public void Open()
    {
        if (!Exists) return;
        try { Process.Start(new ProcessStartInfo(Path) { UseShellExecute = true }); } catch { }
    }

    public void ShowInFolder()
    {
        try
        {
            if (Exists) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path}\"") { UseShellExecute = true });
            else if (Directory.Exists(System.IO.Path.GetDirectoryName(Path)))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{System.IO.Path.GetDirectoryName(Path)}\"") { UseShellExecute = true });
        }
        catch { }
    }
}

/// <summary>Список загрузок: последние 200, хранятся локально.</summary>
public sealed class DownloadStore
{
    readonly string _path;
    public ObservableCollection<DownloadItem> Items { get; } = new();
    public event Action? Changed;

    public DownloadStore(string path)
    {
        _path = path;
        foreach (var d in Json.Load<List<DownloadItem>>(path).Take(200))
        {
            if (d.IsActive) d.State = "failed";   // браузер закрыли посреди загрузки
            Items.Add(d);
        }
    }

    public static string DefaultFolder()
    {
        try
        {
            var path = Native.KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"));
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch { }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public void Add(DownloadItem item)
    {
        Items.Insert(0, item);
        while (Items.Count > 200) Items.RemoveAt(Items.Count - 1);
        Notify();
    }

    public void Remove(DownloadItem item)
    {
        Items.Remove(item);
        Notify();
    }

    public void ClearFinished()
    {
        foreach (var d in Items.Where(d => !d.IsActive).ToList()) Items.Remove(d);
        Notify();
    }

    public void Notify()
    {
        Save();
        Changed?.Invoke();
    }

    public void Save()
    {
        try { Json.Save(_path, Items.ToList()); } catch { }
    }
}

using System.IO;

namespace SneznyjBrowser;

/// <summary>
/// Журнал ошибок: crash.log в папке данных. Ошибка в обработчике окна больше не закрывает браузер —
/// она записывается сюда, и работа продолжается. По журналу видно, что именно сломалось.
/// </summary>
public static class CrashLog
{
    static readonly object Gate = new();

    public static string File => Path.Combine(AppPaths.Root, "crash.log");

    public static void Write(string where, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                var info = new FileInfo(File);
                if (info.Exists && info.Length > 512 * 1024) System.IO.File.Move(File, File + ".old", true);
                System.IO.File.AppendAllText(File,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Updater.CurrentText} · {where}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void Note(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                System.IO.File.AppendAllText(File, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Updater.CurrentText} · {text}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

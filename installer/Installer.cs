using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace SneznyjSetup
{
    /// <summary>Где что лежит. Всё в профиле пользователя — права администратора не нужны.</summary>
    static class Paths
    {
        public const string ExeName = "SneznyjBrowser.exe";
        public const string UninstallerName = "Uninstall.exe";
        public const string DisplayName = "Снежный браузер";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SneznyjBrowser";

        static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static string AppDir => Path.Combine(LocalAppData, "Programs", "SneznyjBrowser");
        public static string Exe => Path.Combine(AppDir, ExeName);
        public static string Uninstaller => Path.Combine(AppDir, UninstallerName);
        public static string DataDir => Path.Combine(LocalAppData, "SneznyjBrowser");
        public static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DisplayName + ".lnk");
        public static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), DisplayName + ".lnk");
        /// <summary>Сюда .NET распаковывает служебные библиотеки однофайловой программы.</summary>
        public static string BundleCache => Path.Combine(Path.GetTempPath(), ".net", "SneznyjBrowser");
        public static string Self => Assembly.GetEntryAssembly().Location;

        public static string Version
        {
            get
            {
                var v = Assembly.GetEntryAssembly().GetName().Version;
                return $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
            }
        }
    }

    /// <summary>Программа, приклеенная к концу установщика: сжатый LZMS SneznyjBrowser.exe + хвост 64 байта.</summary>
    static class Payload
    {
        sealed class Tail
        {
            public long Offset, PackedLength, Length;
            public byte[] Hash;
        }

        static Tail ReadTail()
        {
            using (var f = File.OpenRead(Paths.Self))
            {
                if (f.Length < 64) return null;
                var t = new byte[64];
                f.Seek(-64, SeekOrigin.End);
                if (f.Read(t, 0, 64) != 64 || Encoding.ASCII.GetString(t, 0, 8) != "SNZPACK1") return null;
                var tail = new Tail
                {
                    Offset = BitConverter.ToInt64(t, 8),
                    PackedLength = BitConverter.ToInt64(t, 16),
                    Length = BitConverter.ToInt64(t, 24),
                    Hash = t.Skip(32).ToArray(),
                };
                return tail.Offset > 0 && tail.Offset + tail.PackedLength + 64 == f.Length ? tail : null;
            }
        }

        public static bool Exists => ReadTail() != null;

        public static long AppSize => ReadTail()?.Length ?? 0;

        /// <summary>Распаковать программу и сверить её SHA-256: битый или недокачанный установщик ничего не испортит.</summary>
        public static byte[] Extract()
        {
            var tail = ReadTail() ?? throw new InvalidDataException("в установщике нет программы");
            var packed = new byte[tail.PackedLength];
            using (var f = File.OpenRead(Paths.Self))
            {
                f.Seek(tail.Offset, SeekOrigin.Begin);
                int read = 0;
                while (read < packed.Length)
                {
                    int n = f.Read(packed, read, packed.Length - read);
                    if (n <= 0) throw new EndOfStreamException("установщик обрезан");
                    read += n;
                }
            }
            var app = Lzms.Decompress(packed, tail.Length);
            using (var sha = SHA256.Create())
                if (!sha.ComputeHash(app).SequenceEqual(tail.Hash))
                    throw new InvalidDataException("файл установщика повреждён — скачайте его заново");
            return app;
        }

        /// <summary>Сам установщик без программы (≈50 КБ) — он же деинсталлятор.</summary>
        public static void WriteStub(string path)
        {
            var tail = ReadTail();
            using (var src = File.OpenRead(Paths.Self))
            using (var dst = File.Create(path))
            {
                long left = tail?.Offset ?? src.Length;
                var buffer = new byte[81920];
                while (left > 0)
                {
                    int n = src.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (n <= 0) break;
                    dst.Write(buffer, 0, n);
                    left -= n;
                }
            }
        }
    }

    static class Lzms
    {
        const uint COMPRESS_ALGORITHM_LZMS = 5;

        [DllImport("cabinet.dll", SetLastError = true)]
        static extern bool CreateDecompressor(uint algorithm, IntPtr routines, out IntPtr handle);

        [DllImport("cabinet.dll", SetLastError = true)]
        static extern bool Decompress(IntPtr handle, byte[] src, UIntPtr srcSize, byte[] dst, UIntPtr dstSize, out UIntPtr size);

        [DllImport("cabinet.dll")]
        static extern bool CloseDecompressor(IntPtr handle);

        public static byte[] Decompress(byte[] packed, long length)
        {
            if (!CreateDecompressor(COMPRESS_ALGORITHM_LZMS, IntPtr.Zero, out var h))
                throw new IOException("в Windows нет распаковщика LZMS (нужна Windows 10 или 11)");
            try
            {
                var app = new byte[length];
                if (!Decompress(h, packed, (UIntPtr)packed.Length, app, (UIntPtr)app.Length, out var size) || (long)size.ToUInt64() != length)
                    throw new InvalidDataException("не удалось распаковать программу (код " + Marshal.GetLastWin32Error() + ")");
                return app;
            }
            finally { CloseDecompressor(h); }
        }
    }

    /// <summary>Запущенные копии браузера из папки установки.</summary>
    static class Running
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        static string ImagePath(int pid)
        {
            var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        public static List<Process> Browsers()
        {
            var list = new List<Process>();
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Paths.ExeName)))
            {
                var path = ImagePath(p.Id);
                if (path != null && string.Equals(Path.GetDirectoryName(path), Paths.AppDir, StringComparison.OrdinalIgnoreCase)) list.Add(p);
                else p.Dispose();
            }
            return list;
        }

        public static bool Any => Browsers().Count > 0;

        /// <summary>Подождать, пока браузер закроется сам (после «Обновить» он выходит).</summary>
        public static bool WaitAllExit(TimeSpan timeout)
        {
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                if (!Any) return true;
                Thread.Sleep(250);
            }
            return !Any;
        }

        /// <summary>Закрыть окна по-хорошему (браузер сохранит вкладки), через 15 секунд — принудительно.</summary>
        public static void CloseAll()
        {
            foreach (var p in Browsers())
                using (p)
                    try { p.CloseMainWindow(); } catch { }
            if (WaitAllExit(TimeSpan.FromSeconds(15))) return;
            foreach (var p in Browsers())
                using (p)
                    try { p.Kill(); p.WaitForExit(5000); } catch { }
            WaitAllExit(TimeSpan.FromSeconds(5));
        }
    }

    sealed class InstallOptions
    {
        public bool DesktopShortcut = true;
        public bool IsUpdate;
    }

    static class Installer
    {
        /// <summary>Поставить программу. progress(проценты, что делаем) вызывается из рабочего потока.</summary>
        public static void Install(InstallOptions o, Action<int, string> progress)
        {
            progress(5, "Распаковываю программу…");
            var app = Payload.Extract();
            progress(55, "Проверяю контрольную сумму… готово");

            Directory.CreateDirectory(Paths.AppDir);
            progress(60, "Копирую файлы…");
            var temp = Paths.Exe + ".new";
            File.WriteAllBytes(temp, app);
            Retry(() =>
            {
                if (File.Exists(Paths.Exe)) File.Delete(Paths.Exe);
                File.Move(temp, Paths.Exe);
            });
            app = null;

            progress(75, "Убираю старые файлы…");
            RemoveLeftovers();
            Payload.WriteStub(Paths.Uninstaller + ".new");
            Retry(() =>
            {
                if (File.Exists(Paths.Uninstaller)) File.Delete(Paths.Uninstaller);
                File.Move(Paths.Uninstaller + ".new", Paths.Uninstaller);
            });

            progress(88, "Создаю ярлыки…");
            Shortcut.Create(Paths.StartMenuLink, Paths.Exe, Paths.DisplayName);
            if (!o.IsUpdate && o.DesktopShortcut) Shortcut.Create(Paths.DesktopLink, Paths.Exe, Paths.DisplayName);
            else if (File.Exists(Paths.DesktopLink)) Shortcut.Create(Paths.DesktopLink, Paths.Exe, Paths.DisplayName);

            progress(95, "Записываю в «Приложения»…");
            Register();
            progress(100, "Готово");
        }

        /// <summary>От версии 0.1 (сборка не в один файл) остались библиотеки — они больше не нужны.</summary>
        static void RemoveLeftovers()
        {
            foreach (var f in Directory.GetFiles(Paths.AppDir))
            {
                var name = Path.GetFileName(f);
                if (name.Equals(Paths.ExeName, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(Paths.UninstallerName, StringComparison.OrdinalIgnoreCase)) continue;
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".dll" or ".json" or ".pdb" or ".xml" or ".new")
                    try { File.Delete(f); } catch { }
            }
            var runtimes = Path.Combine(Paths.AppDir, "runtimes");
            if (Directory.Exists(runtimes)) try { Directory.Delete(runtimes, true); } catch { }
            // Служебные библиотеки прошлых версий в %TEMP%\.net — браузер закрыт, их можно убрать.
            if (Directory.Exists(Paths.BundleCache)) try { Directory.Delete(Paths.BundleCache, true); } catch { }
        }

        static void Register()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Paths.UninstallKey))
            {
                k.SetValue("DisplayName", Paths.DisplayName);
                k.SetValue("DisplayVersion", Paths.Version);
                k.SetValue("Publisher", "Sneznyj");
                k.SetValue("DisplayIcon", Paths.Exe + ",0");
                k.SetValue("InstallLocation", Paths.AppDir);
                k.SetValue("UninstallString", $"\"{Paths.Uninstaller}\" --uninstall");
                k.SetValue("QuietUninstallString", $"\"{Paths.Uninstaller}\" --uninstall --silent");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(Paths.Exe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        public static bool IsInstalled => File.Exists(Paths.Exe);

        public static string InstalledVersion
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Paths.UninstallKey))
                    return k?.GetValue("DisplayVersion") as string;
            }
        }

        /// <summary>Удалить программу, ярлыки и запись в «Приложениях». Данные — только если попросили.</summary>
        public static void Uninstall(bool removeData)
        {
            foreach (var link in new[] { Paths.DesktopLink, Paths.StartMenuLink })
                if (File.Exists(link) && Shortcut.PointsTo(link, Paths.Exe))
                    try { File.Delete(link); } catch { }
            Registry.CurrentUser.DeleteSubKeyTree(Paths.UninstallKey, false);

            bool self = string.Equals(Path.GetFullPath(Paths.Self), Path.GetFullPath(Paths.Uninstaller), StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(Paths.AppDir))
            {
                foreach (var f in Directory.GetFiles(Paths.AppDir))
                    if (!(self && string.Equals(f, Paths.Uninstaller, StringComparison.OrdinalIgnoreCase)))
                        Retry(() => File.Delete(f));
                foreach (var d in Directory.GetDirectories(Paths.AppDir))
                    try { Directory.Delete(d, true); } catch { }
            }
            if (Directory.Exists(Paths.BundleCache)) try { Directory.Delete(Paths.BundleCache, true); } catch { }
            if (removeData && Directory.Exists(Paths.DataDir))
                Retry(() => Directory.Delete(Paths.DataDir, true), 20);

            if (self)
            {
                // Себя удалить нельзя, пока работаем: это сделает cmd через пару секунд после выхода.
                var cmd = $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{Paths.Uninstaller}\" & rmdir \"{Paths.AppDir}\"";
                Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
            }
            else if (Directory.Exists(Paths.AppDir) && !Directory.EnumerateFileSystemEntries(Paths.AppDir).Any())
            {
                try { Directory.Delete(Paths.AppDir); } catch { }
            }
        }

        public static void Launch(string args)
        {
            Process.Start(new ProcessStartInfo(Paths.Exe, args) { UseShellExecute = false, WorkingDirectory = Paths.AppDir });
        }

        /// <summary>Есть ли движок Microsoft Edge WebView2 (в Windows 11 есть всегда, в Windows 10 — почти всегда).</summary>
        public static bool HasWebView2
        {
            get
            {
                const string client = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
                var places = new[]
                {
                    new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\" + client),
                    new KeyValuePair<RegistryHive, string>(RegistryHive.LocalMachine, @"SOFTWARE\" + client),
                    new KeyValuePair<RegistryHive, string>(RegistryHive.CurrentUser, @"Software\" + client),
                };
                foreach (var place in places)
                {
                    try
                    {
                        using (var root = RegistryKey.OpenBaseKey(place.Key, RegistryView.Registry64))
                        using (var k = root.OpenSubKey(place.Value))
                            if (k?.GetValue("pv") is string pv && pv.Length > 0 && pv != "0.0.0.0") return true;
                    }
                    catch { }
                }
                return false;
            }
        }

        public const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        static void Retry(Action action, int attempts = 40)
        {
            for (int i = 1; ; i++)
            {
                try { action(); return; }
                catch (Exception ex) when (i < attempts && (ex is IOException || ex is UnauthorizedAccessException))
                {
                    Thread.Sleep(250);
                }
            }
        }
    }

    /// <summary>Ярлыки через системный IShellLink — без сторонних библиотек.</summary>
    static class Shortcut
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, int flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
            void Resolve(IntPtr hwnd, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        public static void Create(string linkPath, string target, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(target);
                link.SetWorkingDirectory(Path.GetDirectoryName(target));
                link.SetDescription(description);
                link.SetIconLocation(target, 0);
                ((IPersistFile)link).Save(linkPath, true);
            }
            finally { Marshal.ReleaseComObject(link); }
        }

        public static bool PointsTo(string linkPath, string target)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(linkPath, 0);
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return string.Equals(sb.ToString(), target, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
            finally { Marshal.ReleaseComObject(link); }
        }
    }
}

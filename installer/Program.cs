using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace SneznyjSetup
{
    // SneznyjBrowser-Setup-x.y.z.exe                — обычная установка с окном
    //   --silent [--no-desktop-shortcut] [--launch] — без окон (скрипты, проверка)
    //   --update [--launch] [--no-activate]         — из браузера: дождаться его закрытия, поставить, открыть снова
    // Uninstall.exe (тот же файл без программы)      — удаление; --uninstall --silent [--remove-data] — без окон
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var a = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);
            bool silent = a.Contains("--silent");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var mutex = new Mutex(true, "SneznyjBrowser.Setup." + Environment.UserName, out bool first))
            {
                if (!first) return 3; // установщик уже запущен
                try
                {
                    if (a.Contains("--uninstall") || !Payload.Exists)
                    {
                        if (!Installer.IsInstalled && !silent)
                        {
                            MessageBox.Show("Снежный браузер не установлен на этом компьютере.", Paths.DisplayName,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return 1;
                        }
                        if (silent)
                        {
                            if (Running.Any) Running.CloseAll();
                            Installer.Uninstall(a.Contains("--remove-data"));
                            return 0;
                        }
                        Application.Run(new UninstallForm());
                        return 0;
                    }

                    if (a.Contains("--update"))
                    {
                        var form = new UpdateForm(a.Contains("--launch"), a.Contains("--no-activate"));
                        Application.Run(form);
                        return form.ExitCode;
                    }

                    if (silent)
                    {
                        if (Running.Any) Running.CloseAll();
                        Installer.Install(new InstallOptions { DesktopShortcut = !a.Contains("--no-desktop-shortcut") }, (p, s) => { });
                        if (a.Contains("--launch")) Installer.Launch("");
                        return 0;
                    }

                    Application.Run(new SetupForm());
                    return 0;
                }
                catch (Exception ex)
                {
                    if (!silent) MessageBox.Show(ex.Message, Paths.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }
    }
}

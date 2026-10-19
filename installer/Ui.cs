using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SneznyjSetup
{
    static class Theme
    {
        static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

        public static readonly Color Bg = C(0x0E1319), Surface = C(0x1A232E), Hover = C(0x24303E), Line = C(0x263241),
            Text = C(0xE6EDF5), Soft = C(0xC3CEDB), Muted = C(0x8696A9), Accent = C(0x7CC4FF), AccentHover = C(0x9BD3FF),
            OnAccent = C(0x0E1319), Danger = C(0xE5484D), DangerHover = C(0xEE6A6E), Good = C(0x6EE7B7), Warn = C(0xFBBF24);

        public static Font Regular(float size) => new Font("Segoe UI", size, FontStyle.Regular);
        public static Font Semibold(float size) => new Font("Segoe UI Semibold", size, FontStyle.Regular);

        static Image _logo;

        public static Image Logo
        {
            get
            {
                if (_logo != null) return _logo;
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.png"))
                    _logo = s != null ? Image.FromStream(s) : null;
                return _logo;
            }
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>Скруглённая кнопка как в браузере.</summary>
    sealed class PillButton : Control, IButtonControl
    {
        public Color Fill = Theme.Surface, HoverFill = Theme.Hover, TextColor = Theme.Soft;
        bool _hover;

        public PillButton(string text, bool accent = false)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Text = text;
            Cursor = Cursors.Hand;
            Font = accent ? Theme.Semibold(10) : Theme.Regular(10);
            if (accent) { Fill = Theme.Accent; HoverFill = Theme.AccentHover; TextColor = Theme.OnAccent; }
        }

        public static PillButton Danger(string text) =>
            new PillButton(text) { Fill = Theme.Danger, HoverFill = Theme.DangerHover, TextColor = Color.White, Font = Theme.Semibold(10) };

        /// <summary>Для экранного диктора и UI Automation это обычная кнопка.</summary>
        protected override AccessibleObject CreateAccessibilityInstance() => new PillAccessible(this);

        sealed class PillAccessible : ControlAccessibleObject
        {
            readonly PillButton _owner;
            public PillAccessible(PillButton owner) : base(owner) { _owner = owner; }
            public override AccessibleRole Role => AccessibleRole.PushButton;
            public override string Name => _owner.Text;
            public override string DefaultAction => "Нажать";
            public override void DoDefaultAction() => _owner.BeginInvoke(new Action(_owner.PerformClick));
        }

        public DialogResult DialogResult { get; set; }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { PerformClick(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Height * 0.26f))
            {
                var fill = !Enabled ? Theme.Line : _hover ? HoverFill : Fill;
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (Focused && ShowFocusCues)
                    using (var pen = new Pen(Theme.Text, 1.2f)) g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? TextColor : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Тонкая полоса прогресса.</summary>
    sealed class Bar : Control
    {
        int _value;

        public Bar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int Value
        {
            get => _value;
            set { _value = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = Height / 2f;
            using (var track = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), radius))
            using (var b = new SolidBrush(Theme.Line))
                g.FillPath(b, track);
            float w = (Width - 1) * _value / 100f;
            if (w < Height) return;
            using (var fill = Theme.Round(new RectangleF(0, 0, w, Height - 1), radius))
            using (var b = new SolidBrush(Theme.Accent))
                g.FillPath(b, fill);
        }
    }

    /// <summary>Тёмное окно фиксированного размера; размеры заданы для 96 dpi и масштабируются сами.</summary>
    class DarkForm : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected DarkForm(int width, int height)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(width, height);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Regular(10);
            Text = Paths.DisplayName;
            try { Icon = Icon.ExtractAssociatedIcon(Paths.Self); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            DwmSetWindowAttribute(Handle, 20 /* тёмный заголовок */, ref on, 4);
            int caption = 0x19130E; // BGR цвета фона
            DwmSetWindowAttribute(Handle, 35 /* цвет заголовка, Windows 11 */, ref caption, 4);
        }

        protected Label AddLabel(Control parent, string text, int x, int y, int w, int h, Font font = null, Color? color = null)
        {
            var l = new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), AutoSize = false,
                Font = font ?? Theme.Regular(10), ForeColor = color ?? Theme.Soft, BackColor = Color.Transparent,
                UseMnemonic = false,
            };
            parent.Controls.Add(l);
            return l;
        }

        protected PictureBox AddLogo(Control parent, int x, int y, int size)
        {
            var p = new PictureBox { Image = Theme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(x, y), Size = new Size(size, size) };
            parent.Controls.Add(p);
            return p;
        }

        protected static T Place<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
        {
            c.Location = new Point(x, y);
            c.Size = new Size(w, h);
            parent.Controls.Add(c);
            return c;
        }

        protected Panel AddPage(int top, int height)
        {
            var p = new Panel { Location = new Point(0, top), Size = new Size(ClientSize.Width, height), BackColor = Theme.Bg, Visible = false };
            Controls.Add(p);
            return p;
        }

        protected static CheckBox MakeCheck(string text, bool isChecked)
        {
            var c = new CheckBox
            {
                Text = text, Checked = isChecked, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Soft, BackColor = Theme.Bg,
                Cursor = Cursors.Hand, Font = Theme.Regular(10),
            };
            c.FlatAppearance.BorderColor = Theme.Muted;
            c.FlatAppearance.CheckedBackColor = Theme.Bg;
            c.FlatAppearance.MouseOverBackColor = Theme.Bg;
            return c;
        }

        protected void Ui(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(a); else a();
        }

        protected static string Short(Exception ex) => ex is AggregateException ae && ae.InnerException != null ? ae.InnerException.Message : ex.Message;
    }

    /// <summary>Обычная установка: приветствие → ход установки → готово.</summary>
    sealed class SetupForm : DarkForm
    {
        readonly Panel _welcome, _progress, _done;
        readonly Label _subtitle, _status, _doneTitle, _doneText;
        readonly Bar _bar;
        readonly CheckBox _desktop;
        readonly PillButton _install, _launch;
        bool _needWebView;

        public SetupForm() : base(560, 340)
        {
            AddLogo(this, 32, 28, 60);
            AddLabel(this, Paths.DisplayName, 108, 30, 420, 34, Theme.Semibold(17), Theme.Text);
            bool update = Installer.IsInstalled;
            var installed = Installer.InstalledVersion;
            _subtitle = AddLabel(this, update
                    ? (installed != null && installed != Paths.Version ? $"Обновление {installed} → {Paths.Version}" : $"Версия {Paths.Version} · переустановка")
                    : $"Версия {Paths.Version} · для Windows 10 и 11",
                110, 66, 420, 22, Theme.Regular(9.5f), Theme.Muted);

            // Приветствие
            _welcome = AddPage(104, 236);
            AddLabel(_welcome, "Быстрый браузер на движке Microsoft Edge: пространства, вертикальные вкладки, " +
                               "блокировщик рекламы. Вкладки, история и пароли хранятся только на этом компьютере.",
                32, 6, 496, 68);
            AddLabel(_welcome, "Установится только для вас — права администратора не нужны.\n" + Paths.AppDir,
                32, 80, 496, 40, Theme.Regular(9), Theme.Muted);
            _desktop = Place(_welcome, MakeCheck("Ярлык на рабочем столе", true), 32, 128, 300, 26);
            var cancel = Place(_welcome, new PillButton("Отмена"), 300, 176, 104, 38);
            cancel.Click += (_, _) => Close();
            _install = Place(_welcome, new PillButton(update ? "Обновить" : "Установить", accent: true), 416, 176, 112, 38);
            _install.Click += (_, _) => StartInstall();
            AcceptButton = _install;
            CancelButton = cancel;

            // Ход установки
            _progress = AddPage(104, 236);
            _status = AddLabel(_progress, "Готовлюсь…", 32, 24, 496, 24, Theme.Regular(10), Theme.Soft);
            _bar = Place(_progress, new Bar(), 32, 60, 496, 6);
            AddLabel(_progress, "Обычно это занимает несколько секунд.", 32, 78, 496, 22, Theme.Regular(9), Theme.Muted);

            // Готово
            _done = AddPage(104, 236);
            _doneTitle = AddLabel(_done, "Готово! Снежный браузер установлен.", 32, 8, 496, 28, Theme.Semibold(12), Theme.Good);
            _doneText = AddLabel(_done, "", 32, 42, 496, 96, Theme.Regular(10), Theme.Soft);
            var close = Place(_done, new PillButton("Закрыть"), 300, 176, 104, 38);
            close.Click += (_, _) => Close();
            _launch = Place(_done, new PillButton("Запустить", accent: true), 416, 176, 112, 38);
            _launch.Click += (_, _) => LaunchAndClose();

            _welcome.Visible = true;
            ResumeLayout(false);
        }

        void ShowPage(Panel page)
        {
            foreach (var p in new[] { _welcome, _progress, _done }) p.Visible = p == page;
            AcceptButton = page == _done ? _launch : page == _welcome ? _install : null;
        }

        async void StartInstall()
        {
            if (Running.Any)
            {
                var answer = MessageBox.Show(this, "Снежный браузер сейчас открыт. Закрыть его, чтобы продолжить?\nВкладки сохранятся.",
                    Paths.DisplayName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (answer != DialogResult.OK) return;
            }
            ShowPage(_progress);
            var options = new InstallOptions { DesktopShortcut = _desktop.Checked };
            try
            {
                await Task.Run(() =>
                {
                    if (Running.Any)
                    {
                        Ui(() => _status.Text = "Закрываю браузер…");
                        Running.CloseAll();
                    }
                    Installer.Install(options, (p, s) => Ui(() => { _bar.Value = p; _status.Text = s; }));
                    _needWebView = !Installer.HasWebView2;
                });
                _subtitle.Text = $"Версия {Paths.Version}";
                var where = options.DesktopShortcut ? "в меню «Пуск» и на рабочем столе" : "в меню «Пуск»";
                _doneText.Text = $"Ярлык «{Paths.DisplayName}» — {where}. Удалить можно в «Параметры → Приложения».\n" +
                                 "Обновления браузер проверяет сам, раз в сутки.";
                if (_needWebView)
                {
                    _doneText.Text += "\n\nДля работы нужен компонент Microsoft Edge WebView2 — его нет на этом компьютере. " +
                                      "Нажмите «Скачать WebView2», установите и запустите браузер.";
                    _doneText.ForeColor = Theme.Warn;
                    _launch.Text = "Скачать WebView2";
                }
                ShowPage(_done);
            }
            catch (Exception ex)
            {
                _doneTitle.Text = "Не получилось установить";
                _doneTitle.ForeColor = Theme.Danger;
                _doneText.Text = Short(ex) + "\n\nЗакройте Снежный браузер, если он открыт, и запустите установку ещё раз.";
                _launch.Visible = false;
                ShowPage(_done);
            }
        }

        void LaunchAndClose()
        {
            try
            {
                if (_needWebView) System.Diagnostics.Process.Start(Installer.WebView2Download);
                else Installer.Launch("");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Short(ex), Paths.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Close();
        }
    }

    /// <summary>Обновление из браузера: маленькое окно, ждёт закрытия браузера, ставит и открывает новую версию.</summary>
    sealed class UpdateForm : DarkForm
    {
        readonly bool _launch, _quiet;
        readonly Label _status;
        readonly Bar _bar;

        public int ExitCode { get; private set; }

        public UpdateForm(bool launch, bool quiet) : base(460, 132)
        {
            _launch = launch;
            _quiet = quiet;
            MinimizeBox = false;
            ControlBox = false;
            AddLogo(this, 24, 24, 44);
            AddLabel(this, $"Обновляю Снежный браузер до {Paths.Version}", 84, 22, 356, 26, Theme.Semibold(11), Theme.Text);
            _status = AddLabel(this, "Жду, пока браузер закроется…", 84, 52, 356, 22, Theme.Regular(9.5f), Theme.Muted);
            _bar = Place(this, new Bar(), 84, 88, 352, 6);
            ResumeLayout(false);
        }

        protected override bool ShowWithoutActivation => _quiet;

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                bool closed = await Task.Run(() => Running.WaitAllExit(TimeSpan.FromSeconds(20)));
                if (!closed)
                {
                    var answer = MessageBox.Show(this, "Чтобы закончить обновление, нужно закрыть все окна Снежного браузера.\n\nЗакрыть их сейчас? Вкладки сохранятся.",
                        Paths.DisplayName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    if (answer != DialogResult.OK) { ExitCode = 2; Close(); return; }
                    _status.Text = "Закрываю браузер…";
                    await Task.Run(Running.CloseAll);
                }
                await Task.Run(() => Installer.Install(new InstallOptions { IsUpdate = true },
                    (p, s) => Ui(() => { _bar.Value = p; _status.Text = s; })));
                _status.Text = "Готово, открываю браузер…";
                if (_launch) Installer.Launch("--updated" + (_quiet ? " --no-activate" : ""));
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                MessageBox.Show(this, "Не удалось обновить: " + Short(ex) + "\n\nБраузер остался прежней версии. Попробуйте позже или скачайте установщик заново.",
                    Paths.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (_launch && Installer.IsInstalled && !Running.Any) try { Installer.Launch(""); } catch { }
            }
            Close();
        }
    }

    /// <summary>Удаление из «Параметры → Приложения» (Uninstall.exe --uninstall).</summary>
    sealed class UninstallForm : DarkForm
    {
        readonly Label _title, _subtitle, _note;
        readonly CheckBox _data;
        readonly PillButton _remove, _cancel;
        bool _finished;

        public UninstallForm() : base(540, 262)
        {
            AddLogo(this, 28, 26, 52);
            _title = AddLabel(this, "Удалить Снежный браузер?", 96, 28, 420, 30, Theme.Semibold(14), Theme.Text);
            _subtitle = AddLabel(this, "Удалятся программа, ярлыки и запись в «Приложениях».", 98, 60, 420, 22, Theme.Regular(9.5f), Theme.Muted);
            _data = Place(this, MakeCheck("Удалить и мои данные: вкладки, историю, пароли и настройки", false), 28, 112, 490, 26);
            _note = AddLabel(this, "Если не отмечать, данные останутся и вернутся при новой установке.", 48, 140, 470, 22,
                Theme.Regular(9), Theme.Muted);
            _cancel = Place(this, new PillButton("Отмена"), 292, 196, 104, 38);
            _cancel.Click += (_, _) => Close();
            _remove = Place(this, PillButton.Danger("Удалить"), 408, 196, 104, 38);
            _remove.Click += (_, _) => Remove();
            CancelButton = _cancel;
            ResumeLayout(false);
        }

        async void Remove()
        {
            if (_finished) { Close(); return; }
            if (Running.Any)
            {
                var answer = MessageBox.Show(this, "Снежный браузер сейчас открыт. Закрыть его и продолжить?",
                    Paths.DisplayName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (answer != DialogResult.OK) return;
            }
            _remove.Enabled = _cancel.Enabled = _data.Enabled = false;
            _subtitle.Text = "Удаляю…";
            bool removeData = _data.Checked;
            try
            {
                await Task.Run(() =>
                {
                    if (Running.Any) Running.CloseAll();
                    Installer.Uninstall(removeData);
                });
                _title.Text = "Снежный браузер удалён";
                _subtitle.Text = removeData ? "Данные тоже удалены." : "Ваши данные остались: " + Paths.DataDir;
            }
            catch (Exception ex)
            {
                _title.Text = "Удалить не получилось";
                _title.ForeColor = Theme.Danger;
                _subtitle.Text = Short(ex);
            }
            _finished = true;
            _data.Visible = _note.Visible = _cancel.Visible = false;
            _remove.Fill = Theme.Accent;
            _remove.HoverFill = Theme.AccentHover;
            _remove.TextColor = Theme.OnAccent;
            _remove.Text = "Закрыть";
            _remove.Enabled = true;
            AcceptButton = _remove;
        }
    }
}

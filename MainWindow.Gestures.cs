using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SneznyjBrowser;

/// <summary>Жесты мыши: что делает каждый жест, след на экране и подпись «← Назад».</summary>
public partial class MainWindow
{
    public static readonly Dictionary<string, string> GestureNames = new()
    {
        ["L"] = "Назад",
        ["R"] = "Вперёд",
        ["UD"] = "Обновить страницу",
        ["DR"] = "Закрыть вкладку",
        ["U"] = "Новая вкладка",
        ["DU"] = "Вернуть закрытую вкладку",
        ["UL"] = "Вкладка слева",
        ["UR"] = "Вкладка справа",
        ["D"] = "Прокрутить в самый низ",
        ["RL"] = "Прокрутить в самый верх",
    };

    static readonly Dictionary<char, string> Arrows = new() { ['L'] = "←", ['R'] = "→", ['U'] = "↑", ['D'] = "↓" };

    Window? _trail;
    Polyline? _trailLine;
    Border? _trailLabel;
    TextBlock? _trailText;

    void StartGestures()
    {
        if (!_settings.Gestures || Gestures.Running) return;
        Gestures.Recognized -= OnGesture;
        Gestures.Progress -= OnGestureProgress;
        Gestures.Recognized += OnGesture;
        Gestures.Progress += OnGestureProgress;
        UpdateGestureTarget();
        if (!_gestureLayoutHooked)
        {
            _gestureLayoutHooked = true;
            LayoutUpdated += (_, _) => UpdateGestureTarget();
        }
        Gestures.Start(Dispatcher);
    }

    void StopGestures()
    {
        Gestures.Stop();
        HideTrail();
    }

    Rect _lastGestureRect;
    bool _gestureLayoutHooked;

    void UpdateGestureTarget()
    {
        if (!Gestures.Running && !_settings.Gestures) return;
        var rect = PresentationSource.FromVisual(PageHost) != null ? Native.ScreenRectPx(PageHost) : Rect.Empty;
        if (rect == _lastGestureRect) return;
        _lastGestureRect = rect;
        Gestures.SetTarget(Native.Handle(this), rect.IsEmpty ? new Rect(0, 0, 0, 0) : rect);
    }

    void OnGesture(string dirs)
    {
        HideTrail();
        if (!GestureNames.ContainsKey(dirs)) return;
        switch (dirs)
        {
            case "L": GoBack(); break;
            case "R": GoForward(); break;
            case "UD": Reload(); break;
            case "DR": CloseActiveTab(); break;
            case "U": OpenNewTab(); break;
            case "DU": ReopenClosedTab(); break;
            case "UL": CycleTab(-1); break;
            case "UR": CycleTab(1); break;
            case "D": _ = ActiveCore?.ExecuteScriptAsync("window.scrollTo({top: document.documentElement.scrollHeight, behavior: 'smooth'})"); break;
            case "RL": _ = ActiveCore?.ExecuteScriptAsync("window.scrollTo({top: 0, behavior: 'smooth'})"); break;
        }
    }

    void OnGestureProgress(Point? cursorPx, string dirs)
    {
        if (cursorPx is not { } px || !_settings.GestureTrail) { HideTrail(); return; }
        EnsureTrail();
        var page = PageRectDip();
        if (page.IsEmpty) return;
        if (!_trail!.IsVisible)
        {
            _trail.Left = page.Left;
            _trail.Top = page.Top;
            _trail.Width = page.Width;
            _trail.Height = page.Height;
            _trailLine!.Points.Clear();
            _trail.Show();
        }
        var dip = Native.PxToDip(this, px);
        var local = new Point(dip.X - _trail.Left, dip.Y - _trail.Top);
        _trailLine!.Points.Add(local);
        if (_trailLine.Points.Count > 600) _trailLine.Points.RemoveAt(0);

        string arrows = string.Concat(dirs.Select(c => Arrows[c]));
        string name = GestureNames.TryGetValue(dirs, out var n) ? n : dirs.Length > 0 ? "Нет такого жеста" : "";
        _trailText!.Text = arrows.Length > 0 ? $"{arrows}  {name}" : "";
        _trailLabel!.Visibility = arrows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(_trailLabel, Math.Clamp(local.X + 18, 0, Math.Max(0, _trail.Width - 220)));
        Canvas.SetTop(_trailLabel, Math.Clamp(local.Y + 14, 0, Math.Max(0, _trail.Height - 40)));
    }

    void EnsureTrail()
    {
        if (_trail != null) return;
        _trailLine = new Polyline { StrokeThickness = 3.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = 0.85 };
        _trailLine.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        _trailText = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold };
        _trailText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _trailLabel = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 6, 12, 7), BorderThickness = new Thickness(1), Child = _trailText };
        _trailLabel.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        _trailLabel.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
        var canvas = new Canvas();
        canvas.Children.Add(_trailLine);
        canvas.Children.Add(_trailLabel);
        _trail = new Window
        {
            Owner = this, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Content = canvas,
            WindowStartupLocation = WindowStartupLocation.Manual, FontFamily = FontFamily, IsHitTestVisible = false,
        };
        _trail.SourceInitialized += (_, _) => Native.MakeOverlay(_trail, clickThrough: true);
    }

    void HideTrail()
    {
        if (_trail?.IsVisible == true) _trail.Hide();
        _trailLine?.Points.Clear();
    }
}

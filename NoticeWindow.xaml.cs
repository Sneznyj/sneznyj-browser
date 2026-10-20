using System.Windows;
using System.Windows.Threading;

namespace SneznyjBrowser;

/// <summary>
/// Подсказки («Добавлено в закладки») и карточка «Новая версия» слева внизу.
/// Это отдельное окно: страницу рисует движок Edge в своём окне, и обычные элементы поверх неё не видны.
/// </summary>
public partial class NoticeWindow : Window
{
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    Action? _toastAction;
    Func<Rect>? _anchor;

    public NoticeWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        SourceInitialized += (_, _) => Native.MakeOverlay(this);
        SizeChanged += (_, _) => Reposition();
        _toastTimer.Tick += (_, _) => HideToast();
        ToastAction.Click += (_, _) =>
        {
            var a = _toastAction;
            HideToast();
            a?.Invoke();
        };
    }

    /// <summary>Где показываться: прямоугольник области страницы в единицах WPF экрана.</summary>
    public void SetAnchor(Func<Rect> anchor) => _anchor = anchor;

    public void Reposition()
    {
        if (_anchor == null || !IsVisible) return;
        var r = _anchor();
        if (r.IsEmpty) return;
        Left = r.Left + 2;
        Top = r.Bottom - ActualHeight + 8;
    }

    public void ShowToast(string text, string? actionText = null, Action? action = null, double seconds = 3)
    {
        ToastText.Text = text;
        _toastAction = action;
        ToastAction.Visibility = actionText != null ? Visibility.Visible : Visibility.Collapsed;
        ToastActionText.Text = actionText ?? "";
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(actionText != null ? Math.Max(seconds, 6) : seconds);
        _toastTimer.Start();
        Refresh();
    }

    public void HideToast()
    {
        _toastTimer.Stop();
        Toast.Visibility = Visibility.Collapsed;
        _toastAction = null;
        Refresh();
    }

    /// <summary>Показать окно, если есть что показывать, иначе спрятать.</summary>
    public void Refresh()
    {
        bool any = Toast.Visibility == Visibility.Visible || UpdateCard.Visibility == Visibility.Visible;
        bool ownerVisible = Owner is { IsVisible: true, WindowState: not WindowState.Minimized };
        if (any && ownerVisible)
        {
            if (!IsVisible) Show();
            UpdateLayout();
            Reposition();
        }
        else if (IsVisible) Hide();
    }
}

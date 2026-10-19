using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SneznyjBrowser;

/// <summary>Палитра Ctrl+K: одно поле для вкладок, истории, стартовых ссылок, команд и поиска.</summary>
public partial class PaletteWindow : Window
{
    readonly Func<string, List<PaletteItem>> _provider;
    bool _closing;

    public PaletteWindow(Window owner, Func<string, List<PaletteItem>> provider)
    {
        InitializeComponent();
        Owner = owner;
        _provider = provider;
        Loaded += (_, _) =>
        {
            Position();
            Query.Focus();
            Keyboard.Focus(Query);
        };
        // Ушли в другую программу — просто закрываемся, фокус не отнимаем.
        Deactivated += (_, _) => SafeClose(returnFocus: false);
        Query.TextChanged += (_, _) => Refresh();
        PreviewKeyDown += OnKey;
        Results.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(Results, d) is ListBoxItem item)
            {
                Results.SelectedItem = item.DataContext;
                RunSelected();
            }
        };
        Refresh();
    }

    void Position()
    {
        var owner = Owner;
        var source = PresentationSource.FromVisual(owner);
        if (source?.CompositionTarget == null) return;
        var m = source.CompositionTarget.TransformFromDevice;
        var topLeft = m.Transform(owner.PointToScreen(new Point(0, 0)));
        Left = topLeft.X + (owner.ActualWidth - ActualWidth) / 2;
        Top = topLeft.Y + 36;
    }

    void Refresh()
    {
        Placeholder.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var items = _provider(Query.Text).Take(50).ToList();
        Results.ItemsSource = items;
        Results.SelectedIndex = items.Count > 0 ? 0 : -1;
        if (items.Count > 0) Results.ScrollIntoView(items[0]);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        int count = Results.Items.Count;
        switch (e.Key)
        {
            case Key.Escape:
                SafeClose();
                e.Handled = true;
                break;
            case Key.Down when count > 0:
                Results.SelectedIndex = (Results.SelectedIndex + 1) % count;
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                Results.SelectedIndex = (Results.SelectedIndex - 1 + count) % count;
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.PageDown when count > 0:
                Results.SelectedIndex = Math.Min(count - 1, Results.SelectedIndex + 8);
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.PageUp when count > 0:
                Results.SelectedIndex = Math.Max(0, Results.SelectedIndex - 8);
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelected();
                e.Handled = true;
                break;
        }
    }

    void RunSelected()
    {
        if (Results.SelectedItem is not PaletteItem item) return;
        var owner = Owner;
        SafeClose();
        owner?.Dispatcher.BeginInvoke(item.Run, DispatcherPriority.Input);
    }

    void SafeClose(bool returnFocus = true)
    {
        if (_closing) return;
        _closing = true;
        // Сначала активируем окно браузера, иначе Windows может отдать фокус чужой программе.
        if (returnFocus) Owner?.Activate();
        Close();
    }
}

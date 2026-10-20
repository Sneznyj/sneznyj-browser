using System.Windows;
using System.Windows.Controls;

namespace SneznyjBrowser;

/// <summary>
/// Полоса вкладок как в Яндексе и Chrome: вкладки одинаковой ширины сжимаются, когда их много,
/// закреплённые — только значком, ярлычки групп и кнопка «+» — по своему размеру.
/// Если не помещаются даже самые узкие, полоса прокручивается колесом мыши.
/// </summary>
public sealed class TabStripPanel : Panel
{
    public const double MaxTab = 232, MinTab = 40, PinnedTab = 42;

    public static readonly DependencyProperty NarrowProperty = DependencyProperty.RegisterAttached(
        "Narrow", typeof(bool), typeof(TabStripPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty TinyProperty = DependencyProperty.RegisterAttached(
        "Tiny", typeof(bool), typeof(TabStripPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetNarrow(DependencyObject d) => (bool)d.GetValue(NarrowProperty);
    public static void SetNarrow(DependencyObject d, bool v) => d.SetValue(NarrowProperty, v);
    public static bool GetTiny(DependencyObject d) => (bool)d.GetValue(TinyProperty);
    public static void SetTiny(DependencyObject d, bool v) => d.SetValue(TinyProperty, v);

    double[] _widths = Array.Empty<double>();
    double _offset;

    public double Extent { get; private set; }

    public void ScrollBy(double delta)
    {
        _offset += delta;
        InvalidateArrange();
    }

    /// <summary>Прокрутить так, чтобы элемент с этим номером был виден целиком.</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= _widths.Length) return;
        double left = 0;
        for (int i = 0; i < index; i++) left += _widths[i];
        double right = left + _widths[index];
        double view = ActualWidth;
        if (left < _offset) _offset = left;
        else if (right > _offset + view) _offset = right - view;
        InvalidateArrange();
    }

    static BrowserTab? TabOf(UIElement child) => (child as FrameworkElement)?.DataContext as BrowserTab;

    protected override Size MeasureOverride(Size available)
    {
        double avail = double.IsInfinity(available.Width) ? 1400 : available.Width;
        int n = InternalChildren.Count;
        if (_widths.Length != n) _widths = new double[n];
        double fixedWidth = 0, height = 0;
        int flexible = 0;
        for (int i = 0; i < n; i++)
        {
            var child = InternalChildren[i];
            var tab = TabOf(child);
            if (tab == null)
            {
                SetNarrow(child, false);
                SetTiny(child, false);
                child.Measure(new Size(double.PositiveInfinity, available.Height));
                _widths[i] = child.DesiredSize.Width;
                fixedWidth += _widths[i];
            }
            else if (tab.IsPinned)
            {
                _widths[i] = PinnedTab;
                fixedWidth += PinnedTab;
            }
            else flexible++;
        }
        double each = flexible > 0 ? Math.Clamp(Math.Floor((avail - fixedWidth) / flexible), MinTab, MaxTab) : 0;
        bool narrow = each < 100, tiny = each < 58;
        for (int i = 0; i < n; i++)
        {
            var child = InternalChildren[i];
            var tab = TabOf(child);
            if (tab == null) { height = Math.Max(height, child.DesiredSize.Height); continue; }
            if (!tab.IsPinned) _widths[i] = each;
            SetNarrow(child, !tab.IsPinned && narrow);
            SetTiny(child, !tab.IsPinned && tiny);
            child.Measure(new Size(_widths[i], available.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        Extent = _widths.Sum();
        return new Size(Math.Min(Extent, avail), double.IsInfinity(available.Height) ? height : Math.Max(height, 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _offset = Math.Clamp(_offset, 0, Math.Max(0, Extent - finalSize.Width));
        double x = -_offset;
        for (int i = 0; i < InternalChildren.Count && i < _widths.Length; i++)
        {
            InternalChildren[i].Arrange(new Rect(x, 0, _widths[i], finalSize.Height));
            x += _widths[i];
        }
        return finalSize;
    }
}

/// <summary>Строка элементов, где всё, что не влезло, прячется (для панели закладок с кнопкой «»»).</summary>
public sealed class ClipRowPanel : Panel
{
    public ClipRowPanel() => ClipToBounds = true;

    /// <summary>Номер первого спрятанного элемента или -1, если влезло всё.</summary>
    public int FirstHidden { get; private set; } = -1;

    public event Action? OverflowChanged;

    protected override Size MeasureOverride(Size available)
    {
        double h = 0, w = 0;
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(new Size(double.PositiveInfinity, available.Height));
            h = Math.Max(h, c.DesiredSize.Height);
            w += c.DesiredSize.Width;
        }
        return new Size(double.IsInfinity(available.Width) ? w : Math.Min(w, available.Width), h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        int hidden = -1;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            double w = c.DesiredSize.Width;
            if (hidden < 0 && x + w > finalSize.Width + 0.5) hidden = i;
            if (hidden >= 0) c.Arrange(new Rect(finalSize.Width + 2000, 0, w, finalSize.Height));
            else c.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }
        if (hidden != FirstHidden)
        {
            FirstHidden = hidden;
            Dispatcher.BeginInvoke(() => OverflowChanged?.Invoke());
        }
        return finalSize;
    }
}

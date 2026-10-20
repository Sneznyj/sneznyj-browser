using System.Windows;
using System.Windows.Input;

namespace SneznyjBrowser;

/// <summary>Небольшое окно в стиле браузера: ввод названия или подтверждение.</summary>
public partial class InputDialog : Window
{
    InputDialog(Window owner, string title)
    {
        InitializeComponent();
        Owner = owner;
        TitleText.Text = title;
        Input.TextChanged += (_, _) =>
            HintText.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == this) DragMove(); };
        SourceInitialized += (_, _) => Native.StyleWindow(this);
    }

    public static string? Ask(Window owner, string title, string hint, string initial)
    {
        var d = new InputDialog(owner, title);
        d.HintText.Text = hint;
        d.Input.Text = initial;
        d.Loaded += (_, _) =>
        {
            d.Input.Focus();
            Keyboard.Focus(d.Input);
            d.Input.SelectAll();
        };
        return d.ShowDialog() == true ? d.Input.Text : null;
    }

    /// <summary>Два поля: например название и адрес закладки.</summary>
    public static (string First, string Second)? AskTwo(Window owner, string title, string label1, string value1, string label2, string value2)
    {
        var d = new InputDialog(owner, title);
        d.Label1.Text = label1;
        d.Label1.Visibility = Visibility.Visible;
        d.InputBox.Margin = new Thickness(0, 6, 0, 0);
        d.Input.Text = value1;
        d.Label2.Text = label2;
        d.Label2.Visibility = Visibility.Visible;
        d.Input2.Visibility = Visibility.Visible;
        d.Input2.Text = value2;
        d.Loaded += (_, _) =>
        {
            d.Input.Focus();
            Keyboard.Focus(d.Input);
            d.Input.SelectAll();
        };
        return d.ShowDialog() == true ? (d.Input.Text, d.Input2.Text) : null;
    }

    public static bool Confirm(Window owner, string title, string message, string okText)
    {
        var d = new InputDialog(owner, title);
        d.MessageText.Text = message;
        d.MessageText.Visibility = Visibility.Visible;
        d.InputBox.Visibility = Visibility.Collapsed;
        d.OkButton.Content = okText;
        d.OkButton.SetResourceReference(BackgroundProperty, "DangerBrush");
        d.OkButton.Foreground = System.Windows.Media.Brushes.White;
        return d.ShowDialog() == true;
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

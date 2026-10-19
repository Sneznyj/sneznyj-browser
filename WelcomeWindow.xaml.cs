using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;

namespace SneznyjBrowser;

/// <summary>Первый запуск: выбрать набор пространств и поисковик. Ничего личного автора браузера сюда не попадает.</summary>
public partial class WelcomeWindow : Window
{
    static readonly string[] EngineChoice = { "google", "yandex", "duckduckgo", "bing" };

    string _template = Defaults.Templates[0].Id;
    string _engine = "google";

    WelcomeWindow()
    {
        InitializeComponent();
        Logo.Data = Snowflake.Create(30);
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        foreach (var t in Defaults.Templates)
        {
            var id = t.Id;
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = t.Glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 20,
                Foreground = (Brush)FindResource("AccentBrush"), Margin = new Thickness(0, 0, 0, 10),
            });
            content.Children.Add(new TextBlock { Text = t.Title, FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
            content.Children.Add(new TextBlock
            {
                Text = t.Description, Foreground = (Brush)FindResource("MutedBrush"), FontSize = 12,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 16,
            });
            var card = new RadioButton
            {
                Style = (Style)FindResource("CardRadio"), GroupName = "template", Content = content,
                Margin = new Thickness(5, 0, 5, 0), IsChecked = id == _template, ToolTip = Preview(t),
            };
            AutomationProperties.SetName(card, t.Title);
            card.Checked += (_, _) => _template = id;
            card.MouseDoubleClick += (_, _) => { _template = id; Finish(); };
            TemplateList.Children.Add(card);
        }

        foreach (var key in EngineChoice)
        {
            var k = key;
            var chip = new RadioButton
            {
                Style = (Style)FindResource("ChipRadio"), GroupName = "engine", Content = UrlTools.EngineName(k),
                Margin = new Thickness(0, 0, 8, 8), IsChecked = k == _engine,
            };
            chip.Checked += (_, _) => _engine = k;
            EngineList.Children.Add(chip);
        }
    }

    static string Preview(WorkspaceTemplate t)
    {
        var lines = t.Create().Select(w => w.Links is { Count: > 0 }
            ? $"{w.Name}: {string.Join(", ", w.Links.Select(l => l.Title))}"
            : $"{w.Name}: пусто");
        return string.Join("\n", lines);
    }

    void Start_Click(object sender, RoutedEventArgs e) => Finish();
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Finish()
    {
        DialogResult = true;
    }

    /// <summary>Новый человек (нет session.json): показать окно и записать выбранное в настройки и сессию.</summary>
    public static void RunIfFirstStart(bool activate)
    {
        if (File.Exists(AppPaths.SessionFile)) return;
        var w = new WelcomeWindow { ShowActivated = activate };
        w.ShowDialog();

        var settings = Json.Load<SettingsData>(AppPaths.SettingsFile);
        settings.SearchEngine = w._engine;
        settings.PanelApps = Defaults.PanelApps(w._engine);
        settings.Normalize();
        var workspaces = Defaults.Workspaces(w._template);
        var session = new SessionData { Workspaces = workspaces, ActiveWorkspace = workspaces[0].Id };
        try
        {
            Json.Save(AppPaths.SettingsFile, settings);
            Json.Save(AppPaths.SessionFile, session);
        }
        catch { }
    }
}

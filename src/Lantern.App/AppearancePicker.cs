using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Lantern.App;
public partial class MainWindow
{
    private FrameworkElement AppearancePicker()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(Paragraph("Giao diện", bold: true));
        var modes = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 0, -4, 14) };
        panel.Children.Add(modes);
        var choices = new List<RadioButton>(); bool updating = false;
        void Change(string? mode = null, string? color = null)
        {
            if (updating) return;
            var previousMode = App.Settings.Theme; var previousColor = App.Settings.ColorTheme;
            try
            {
                App.Settings.Theme = mode ?? previousMode; App.Settings.ColorTheme = color ?? previousColor;
                App.ApplyTheme(App.Settings.Theme); App.SaveSettings(); renderKey = ""; Refresh();
            }
            catch (Exception ex)
            {
                App.Settings.Theme = previousMode; App.Settings.ColorTheme = previousColor; App.ApplyTheme(previousMode);
                updating = true;
                foreach (var choice in choices) choice.IsChecked = (string)choice.Tag == (choice.GroupName == "AppearanceMode" ? previousMode : previousColor);
                updating = false; ShowError(ex);
            }
        }
        RadioButton Choice(string id, string name, string group, object content, bool selected)
        {
            var button = new RadioButton { Tag = id, GroupName = group, Content = content, IsChecked = selected, Style = (Style)FindResource("AppearanceChoice"), Margin = new Thickness(4, 0, 4, 0) };
            AutomationProperties.SetName(button, name); choices.Add(button); return button;
        }
        foreach (var (id, name) in new[] { ("system", "Theo Windows"), ("light", "Sáng"), ("dark", "Tối") })
        {
            var button = Choice(id, name, "AppearanceMode", new TextBlock { Text = name, Margin = new Thickness(8, 9, 8, 9), TextAlignment = TextAlignment.Center }, App.Settings.Theme == id);
            button.Checked += (_, _) => Change(mode: id); modes.Children.Add(button);
        }
        panel.Children.Add(Paragraph("Bảng màu", bold: true));
        var colors = new UniformGrid { Columns = 5, Margin = new Thickness(-4, 0, -4, 0) }; panel.Children.Add(colors);
        foreach (var palette in ColorThemes.All)
        {
            var content = new StackPanel();
            var sample = new StackPanel { Margin = new Thickness(10, 13, 10, 13) };
            sample.Children.Add(new Border { Height = 9, Width = 36, CornerRadius = new CornerRadius(4), Background = Brushes.White, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) });
            sample.Children.Add(new Border { Height = 9, Width = 48, CornerRadius = new CornerRadius(4), Background = Brushes.White, Opacity = 0.95, HorizontalAlignment = HorizontalAlignment.Right });
            content.Children.Add(new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Accent)), CornerRadius = new CornerRadius(8), Child = sample });
            content.Children.Add(new TextBlock { Text = palette.Name, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 12, MinHeight = 34, Margin = new Thickness(0, 9, 0, 0) });
            var button = Choice(palette.Id, palette.Name, "ColorPalette", content, App.Settings.ColorTheme == palette.Id);
            button.Checked += (_, _) => Change(color: palette.Id); colors.Children.Add(button);
        }
        panel.Children.Add(Paragraph("Chọn để áp dụng và lưu ngay. Bạn có thể thử lần lượt các màu.", 12, muted: true));
        return panel;
    }
}

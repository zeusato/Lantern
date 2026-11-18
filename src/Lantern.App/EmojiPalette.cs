using System.Windows;
using System.Windows.Controls;

namespace Lantern.App;

internal sealed class EmojiPalette : StackPanel
{
    internal EmojiPalette(Action<string> insert)
    {
        var recent = new StackPanel { Name = "RecentEmojiSection", Margin = new Thickness(0, 6, 0, 4) };
        recent.Children.Add(new TextBlock { Text = "Dùng gần đây", FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 4) });
        var recentFaces = new WrapPanel { Name = "RecentEmojiChoices", Width = 328 }; recent.Children.Add(recentFaces); Children.Add(recent);
        Button Choice(EmojiAsset asset)
        {
            var button = new Button { Content = EmojiAssets.Icon(asset.Emoji, 30), Tag = asset.Emoji, Width = 41, Height = 42, Padding = new Thickness(4), Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = EmojiAssets.Tooltip(asset) };
            ToolTipService.SetInitialShowDelay(button, 200); ToolTipService.SetShowDuration(button, 15000);
            System.Windows.Automation.AutomationProperties.SetName(button, EmojiAssets.Tooltip(asset));
            button.Click += (_, _) => { EmojiUsage.Remember(asset.Emoji); RefreshRecent(); insert(asset.Emoji); };
            return button;
        }
        void RefreshRecent()
        {
            recentFaces.Children.Clear();
            foreach (var asset in EmojiUsage.Recent) recentFaces.Children.Add(Choice(asset));
            recent.Visibility = recentFaces.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        RefreshRecent();
        var search = new TextBox { Name = "EmojiSearch", Margin = new Thickness(0, 9, 0, 7), ToolTip = "Tìm tên hoặc ký tự chèn, ví dụ :)) hoặc /-heart" };
        System.Windows.Automation.AutomationProperties.SetName(search, "Tìm emoji hoặc ký tự chèn"); Children.Add(search);
        var categories = new ComboBox { Name = "EmojiCategory", ItemsSource = EmojiAssets.Catalog.Select(asset => asset.Category).Distinct().ToArray(), SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(categories, "Nhóm emoji"); Children.Add(categories);
        var faces = new WrapPanel { Name = "EmojiChoices", Width = 328 };
        Children.Add(new ScrollViewer { Content = faces, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var empty = new TextBlock { Text = "Không tìm thấy emoji.", Margin = new Thickness(0, 8, 0, 8), Visibility = Visibility.Collapsed }; Children.Add(empty);
        void Refresh()
        {
            faces.Children.Clear(); var query = search.Text.Trim();
            foreach (var asset in EmojiAssets.Catalog.Where(asset => query.Length == 0 ? asset.Category == (string?)categories.SelectedItem : asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || asset.Emoji.Contains(query, StringComparison.OrdinalIgnoreCase) || (asset.Aliases ?? []).Any(alias => alias.Contains(query, StringComparison.OrdinalIgnoreCase))))
            {
                faces.Children.Add(Choice(asset));
            }
            empty.Visibility = faces.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => Refresh(); categories.SelectionChanged += (_, _) => Refresh(); Refresh();
    }
}

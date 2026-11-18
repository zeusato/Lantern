using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Lantern.Core;

namespace Lantern.App;

internal sealed class GroupMemberPicker : StackPanel
{
    private readonly Peer[] available;
    private readonly HashSet<string> selected = [];
    private readonly int limit;
    private readonly StackPanel results = new() { Name = "MemberResults" };
    private readonly WrapPanel chosen = new() { Name = "SelectedMembers" };
    private readonly TextBlock count = new() { Margin = new Thickness(0, 8, 0, 5) };
    internal TextBox Search { get; } = new() { Name = "MemberSearch", Margin = new Thickness(0, 6, 0, 0) };
    internal string[] SelectedIds => available.Where(peer => selected.Contains(peer.Id)).Select(peer => peer.Id).ToArray();

    internal GroupMemberPicker(IEnumerable<Peer> peers, int maximum = 31)
    {
        available = peers.DistinctBy(peer => peer.Id).OrderBy(peer => peer.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(); limit = Math.Max(0, maximum);
        Children.Add(new TextBlock { Text = "Tìm thành viên", FontWeight = FontWeights.SemiBold }); Children.Add(Search);
        System.Windows.Automation.AutomationProperties.SetName(Search, "Tìm thành viên theo tên");
        Children.Add(count); Children.Add(new ScrollViewer { Content = chosen, MaxHeight = 84, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Children.Add(new ScrollViewer { Content = results, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 7, 0, 0) });
        Search.TextChanged += (_, _) => RefreshResults(); RefreshSelection(); RefreshResults();
    }
    private void RefreshResults()
    {
        results.Children.Clear(); var query = SearchKey(Search.Text.Trim());
        foreach (var peer in available.Where(peer => SearchKey(peer.DisplayName).Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var label = new DockPanel();
            var photo = AvatarImages.Get(peer.Avatar);
            if (photo is not null) label.Children.Add(new Image { Source = photo, Width = 28, Height = 28, Margin = new Thickness(0, 0, 9, 0) });
            label.Children.Add(new TextBlock { Text = peer.DisplayName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var choice = new CheckBox { Content = label, Tag = peer.Id, IsChecked = selected.Contains(peer.Id), Margin = new Thickness(3, 5, 4, 5), VerticalContentAlignment = VerticalAlignment.Center };
            choice.SetResourceReference(Control.ForegroundProperty, "TextBrush"); System.Windows.Automation.AutomationProperties.SetName(choice, peer.DisplayName);
            choice.Checked += (_, _) => { if (selected.Count >= limit && !selected.Contains(peer.Id)) { choice.IsChecked = false; return; } selected.Add(peer.Id); RefreshSelection(); };
            choice.Unchecked += (_, _) => { selected.Remove(peer.Id); RefreshSelection(); };
            results.Children.Add(choice);
        }
        if (results.Children.Count == 0) results.Children.Add(new TextBlock { Text = "Không tìm thấy thành viên phù hợp.", Margin = new Thickness(3, 10, 3, 10), TextWrapping = TextWrapping.Wrap });
        UpdateAvailability();
    }
    private void RefreshSelection()
    {
        count.Text = $"Đã chọn {selected.Count}/{limit}"; chosen.Children.Clear();
        foreach (var peer in available.Where(peer => selected.Contains(peer.Id)))
        {
            var chip = new Button { Content = new TextBlock { Text = peer.DisplayName + "  ×", TextTrimming = TextTrimming.CharacterEllipsis }, Tag = peer.Id, MaxWidth = 200, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 5, 5), ToolTip = "Bỏ chọn " + peer.DisplayName };
            chip.Click += (_, _) => { selected.Remove(peer.Id); RefreshSelection(); RefreshResults(); }; chosen.Children.Add(chip);
        }
        UpdateAvailability();
    }
    private void UpdateAvailability()
    {
        foreach (var choice in results.Children.OfType<CheckBox>()) choice.IsEnabled = choice.IsChecked == true || selected.Count < limit;
    }
    private static string SearchKey(string value) => string.Concat(value.Normalize(System.Text.NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)).Replace('đ', 'd').Replace('Đ', 'D');
}

internal sealed class GroupCreationForm : StackPanel
{
    internal TextBox NameInput { get; } = new() { Name = "GroupName", MaxLength = 64, Margin = new Thickness(0, 5, 0, 8) };
    internal Border AvatarHost { get; } = new();
    internal string Avatar { get; set; } = "";
    internal GroupMemberPicker Members { get; }
    private readonly TextBlock error = new() { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };

    internal GroupCreationForm(IEnumerable<Peer> peers)
    {
        Children.Add(new TextBlock { Text = "Tạo nhóm", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        Children.Add(new TextBlock { Text = "Tên nhóm", FontWeight = FontWeights.SemiBold }); Children.Add(NameInput);
        System.Windows.Automation.AutomationProperties.SetName(NameInput, "Tên nhóm");
        Children.Add(AvatarHost);
        Children.Add(new TextBlock { Text = "Chọn ít nhất 2 người. Bạn được thêm vào nhóm tự động.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        Members = new GroupMemberPicker(peers); Children.Add(Members); Children.Add(error);
    }
    internal bool Validate()
    {
        error.Text = string.IsNullOrWhiteSpace(NameInput.Text) || NameInput.Text.Any(char.IsControl) ? "Nhập tên nhóm hợp lệ." : Members.SelectedIds.Length < 2 ? "Chọn ít nhất 2 thành viên để tạo nhóm." : "";
        return error.Text.Length == 0;
    }
}

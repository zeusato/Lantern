using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lantern.Core;

namespace Lantern.App;

public partial class MainWindow
{
    private bool hasTimedPolls;
    private async void CreatePollClick(object sender, RoutedEventArgs e)
    {
        if (appLocking || ConversationProtected || !Composer.IsEnabled) return;
        var groupId = selectedId;
        if (!store.Groups().Any(g => g.Id == groupId && g.Members.Contains(identity.Id))) return;
        var panel = new StackPanel();
        panel.Children.Add(Paragraph("Tạo bình chọn", 22, true));
        panel.Children.Add(Paragraph("Chủ đề bình chọn", bold: true));
        var question = new TextBox { MaxLength = 200, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
        AutomationProperties.SetName(question, "Chủ đề bình chọn"); panel.Children.Add(question);
        panel.Children.Add(Paragraph("Các lựa chọn", bold: true));
        var options = new StackPanel(); panel.Children.Add(options);
        var inputs = new List<TextBox>();
        void AddOption()
        {
            if (inputs.Count >= 10) return;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var remove = new Button { Content = "×", ToolTip = "Bỏ lựa chọn", Width = 36, Padding = new Thickness(4), Margin = new Thickness(6, 0, 0, 0) }; DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var input = new TextBox { MaxLength = 120, ToolTip = "Lựa chọn " + (inputs.Count + 1) }; AutomationProperties.SetName(input, "Lựa chọn " + (inputs.Count + 1)); row.Children.Add(input); inputs.Add(input); options.Children.Add(row);
            remove.Click += (_, _) => { if (inputs.Count <= 2) return; inputs.Remove(input); options.Children.Remove(row); };
        }
        AddOption(); AddOption();
        var add = new Button { Content = "+ Thêm lựa chọn", HorizontalAlignment = HorizontalAlignment.Left }; add.Click += (_, _) => { AddOption(); inputs.Last().Focus(); }; panel.Children.Add(add);
        var multiple = new CheckBox { Content = "Cho phép chọn nhiều phương án", Margin = new Thickness(0, 16, 0, 12) }; panel.Children.Add(multiple);
        panel.Children.Add(Paragraph("Thời hạn bình chọn", bold: true));
        var deadline = new ComboBox { ItemsSource = new[] { "Không thời hạn", "30 phút", "1 giờ", "1 ngày", "7 ngày" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 10) }; panel.Children.Add(deadline);
        panel.Children.Add(Paragraph("Thành viên có thể đổi hoặc gỡ phiếu. Kết quả hiển thị người đã chọn. Người tạo có thể kết thúc bình chọn.", 12, muted: true));
        panel.Children.Add(Paragraph("Phiếu chờ được xác nhận khi máy người tạo online.", 12, muted: true));
        if (!ShowDialog("Tạo bình chọn", panel, "Tạo bình chọn", () =>
        {
            try { new ChatPoll { Question = question.Text.Trim(), Options = inputs.Select(i => i.Text.Trim()).ToList() }.Validate(); return true; }
            catch (Exception ex) { ShowToast(ex.Message); return false; }
        }, width: 540)) return;
        try
        {
            var minutes = new[] { 0, 30, 60, 1440, 10080 }[deadline.SelectedIndex];
            await node.CreatePollAsync(groupId, question.Text, inputs.Select(i => i.Text), multiple.IsChecked == true, minutes == 0 ? null : DateTimeOffset.UtcNow.AddMinutes(minutes));
            renderKey = ""; RefreshConversation(); ChatScroll.ScrollToEnd();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private FrameworkElement PollCard(ChatMessage message)
    {
        var poll = message.Poll!;
        hasTimedPolls |= poll.EndsAt.HasValue && !poll.IsClosed;
        var panel = new StackPanel { MinWidth = 220, MaxWidth = 420 };
        panel.Children.Add(Paragraph("BÌNH CHỌN" + (poll.IsClosed ? " · ĐÃ KẾT THÚC" : ""), 10, true, muted: true));
        panel.Children.Add(Paragraph(poll.Question, 16, true));
        panel.Children.Add(Paragraph(poll.Multiple ? "Chọn một hoặc nhiều phương án" : "Chọn một phương án", 11, muted: true));
        if (poll.EndsAt is { } ends) panel.Children.Add(Paragraph("Kết thúc: " + ends.LocalDateTime.ToString("dd/MM HH:mm"), 11, muted: true));
        var own = message.PendingPollVote ?? poll.Votes.GetValueOrDefault(identity.Id);
        var checks = new List<CheckBox>();
        var voters = poll.Votes.Count(v => v.Value.Choices.Length > 0);
        var canVote = !poll.IsClosed && store.Groups().Any(g => g.Id == message.GroupId && g.Members.Contains(identity.Id));
        foreach (var (option, index) in poll.Options.Select((o, i) => (o, i)))
        {
            var names = poll.Votes.Where(v => v.Value.Choices.Contains(index)).Select(v => v.Key == identity.Id ? "Bạn" : node.FindPeer(v.Key)?.DisplayName ?? "Thành viên").ToArray();
            var row = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
            var check = new CheckBox { Content = new TextBlock { Text = option, TextWrapping = TextWrapping.Wrap }, IsChecked = own?.Choices.Contains(index) == true, IsEnabled = canVote, Padding = new Thickness(2) };
            AutomationProperties.SetName(check, option);
            check.Checked += (_, _) => { if (!poll.Multiple) foreach (var other in checks.Where(c => c != check)) other.IsChecked = false; };
            checks.Add(check); row.Children.Add(check);
            var percent = voters == 0 ? 0 : (int)Math.Round(100d * names.Length / voters);
            row.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = percent, Height = 4, Margin = new Thickness(0, 5, 0, 3), ToolTip = string.Join(", ", names) });
            row.Children.Add(Paragraph($"{names.Length} lượt chọn · {percent}%", 10, muted: true)); panel.Children.Add(row);
        }
        panel.Children.Add(Paragraph($"{voters} người đã bình chọn", 11, muted: true));
        if (message.PendingPollVote is not null) panel.Children.Add(Paragraph("Phiếu đang chờ người tạo xác nhận…", 11, muted: true));
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(buttons);
        var submit = new Button { Content = own is null ? "Bình chọn" : "Cập nhật phiếu", IsEnabled = canVote, Margin = new Thickness(0, 0, 6, 0) };
        submit.Click += async (_, _) =>
        {
            submit.IsEnabled = false;
            try { await node.VotePollAsync(message.Id, checks.Select((c, i) => (c, i)).Where(x => x.c.IsChecked == true).Select(x => x.i)); renderKey = ""; RefreshConversation(); }
            catch (Exception ex) { submit.IsEnabled = canVote; ShowError(ex); }
        }; buttons.Children.Add(submit);
        var details = new Button { Content = "Xem kết quả" }; details.Click += (_, _) =>
        {
            var latest = store.Message(message.Id)?.Poll; if (latest is null || ConversationProtected || appLocking) return;
            var content = new StackPanel(); content.Children.Add(Paragraph(latest.Question, 20, true));
            for (var i = 0; i < latest.Options.Count; i++)
            {
                content.Children.Add(Paragraph(latest.Options[i], bold: true));
                var people = latest.Votes.Where(v => v.Value.Choices.Contains(i)).Select(v => v.Key == identity.Id ? "Bạn" : node.FindPeer(v.Key)?.DisplayName ?? "Thành viên").ToArray();
                content.Children.Add(Paragraph(people.Length == 0 ? "Chưa có lượt chọn" : string.Join(", ", people), 12, muted: true));
            }
            ShowDialog("Kết quả bình chọn", content, "Đóng");
        }; buttons.Children.Add(details);
        if (message.SenderId == identity.Id && canVote)
        {
            var close = new Button { Content = "Kết thúc", Margin = new Thickness(6, 0, 0, 0) };
            close.Click += async (_, _) =>
            {
                var content = new StackPanel(); content.Children.Add(Paragraph("Kết thúc bình chọn?", 20, true)); content.Children.Add(Paragraph("Thành viên sẽ không thể gửi thêm hoặc đổi phiếu."));
                if (!ShowDialog("Kết thúc bình chọn", content, "Kết thúc")) return;
                try { await node.ClosePollAsync(message.Id); renderKey = ""; RefreshConversation(); } catch (Exception ex) { ShowError(ex); }
            }; buttons.Children.Add(close);
        }
        return panel;
    }
}

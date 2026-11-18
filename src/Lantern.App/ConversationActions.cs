using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Lantern.Core;

namespace Lantern.App;

public partial class MainWindow
{
    private void ContactsContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (appLocking || node is null) return;
        var item = ItemsControl.ContainerFromElement(ContactsList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.Content is not ContactRow row) return;
        var menu = ConversationMenu(row); menu.PlacementTarget = item; menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true;
    }
    private ContextMenu ConversationMenu(ContactRow row)
    {
        var preference = store.Preference(row.Id);
        var menu = new ContextMenu();
        var pin = new MenuItem { Header = preference.Pinned ? "Bỏ ghim hội thoại" : "Ghim hội thoại" };
        pin.Click += (_, _) => { var p = store.Preference(row.Id); p.Pinned = !p.Pinned; store.SavePreference(p); Refresh(); }; menu.Items.Add(pin);
        var category = new MenuItem { Header = "Phân loại" }; menu.Items.Add(category);
        foreach (var label in new[] { "", "Công việc", "Cá nhân" }.Concat(store.Preferences().Select(p => p.Category)).Distinct())
        {
            var tag = new MenuItem { Header = label.Length == 0 ? "Không phân loại" : label, IsCheckable = true, IsChecked = preference.Category == label };
            tag.Click += (_, _) => { var p = store.Preference(row.Id); p.Category = label; store.SavePreference(p); Refresh(); }; category.Items.Add(tag);
        }
        var custom = new MenuItem { Header = "Tạo nhãn…" }; custom.Click += (_, _) =>
        {
            var input = new TextBox { MaxLength = 40 }; var panel = new StackPanel(); panel.Children.Add(Paragraph("Tên nhãn", 20, true)); panel.Children.Add(input);
            if (ShowDialog("Phân loại hội thoại", panel, "Lưu", () => input.Text.Trim().Length > 0, input)) { var p = store.Preference(row.Id); p.Category = input.Text.Trim(); store.SavePreference(p); Refresh(); }
        }; category.Items.Add(custom);
        var add = new MenuItem { Header = "Thêm vào nhóm", IsEnabled = !row.IsGroup && node.FindPeer(row.Id) is { Trusted: true, Blocked: false } };
        add.Click += (_, _) => AddContactToGroup(row.Id); menu.Items.Add(add);
        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "Xóa hội thoại" }; delete.Click += async (_, _) => await DeleteLocalConversation(row.Id); menu.Items.Add(delete);
        return menu;
    }
    private async void AddContactToGroup(string peerId)
    {
        var groups = store.Groups().Where(g => g.OwnerId == identity.Id && !g.Members.Contains(peerId) && g.Members.Count < 32).ToArray();
        if (groups.Length == 0) { ShowToast("Chưa có nhóm do bạn quản lý để thêm người này."); return; }
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Thêm vào nhóm", 23, true));
        var target = new ComboBox { ItemsSource = groups, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 14) }; panel.Children.Add(target);
        var history = new CheckBox { Content = "Cho người mới đọc tin nhắn cũ", IsChecked = false }; panel.Children.Add(history);
        if (!ShowDialog("Thêm vào nhóm", panel, "Thêm")) return;
        try { await node.AddGroupMembersAsync(((ChatGroup)target.SelectedItem).Id, [peerId], history.IsChecked == true); Refresh(); ShowToast("Đã thêm thành viên."); } catch (Exception ex) { ShowError(ex); }
    }
    private async Task DeleteLocalConversation(string id)
    {
        if (appLocking) return;
        if (selectedId != id) { selectedId = id; Refresh(); }
        if (!TryUnlockConversation()) return;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Xóa hội thoại ở phía bạn?", 23, true));
        panel.Children.Add(Paragraph("Lịch sử trên máy bạn sẽ bị xóa và hội thoại được ẩn khỏi danh sách. Đối tác vẫn giữ bản của họ. Hội thoại hiện lại khi có tin mới. Thao tác này không xóa nhóm chung.", 13));
        if (!ShowDialog("Xóa hội thoại", panel, "Xóa chỉ ở phía tôi")) return;
        try
        {
            CloseMediaViews(); await node.DeleteConversationHistoryAsync(id);
            var preference = store.Preference(id); preference.Hidden = true; store.SavePreference(preference);
            drafts.Remove(id); replyDrafts.Remove(id); selectedId = ""; renderKey = ""; Refresh();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void DeleteGroupClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking) return;
        var group = store.Groups().Find(g => g.Id == selectedId); if (group?.OwnerId != identity.Id) return;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Xóa nhóm trên mọi máy?", 23, true));
        panel.Children.Add(Paragraph("Nhóm “" + group.Name + "” cùng lịch sử và tệp do Lantern lưu sẽ bị xóa trên các máy. Máy đang offline sẽ xóa khi kết nối lại. Tệp đã lưu ra ngoài không bị xóa.", 13));
        var confirm = new TextBox(); panel.Children.Add(Paragraph("Nhập XÓA để xác nhận", 13, true)); panel.Children.Add(confirm);
        if (!ShowDialog("Xóa nhóm", panel, "Xóa nhóm", () => confirm.Text.Trim() == "XÓA", confirm)) return;
        try { CloseMediaViews(); await node.DeleteGroupAsync(group.Id); selectedId = ""; renderKey = ""; Refresh(); ShowToast("Đã xóa nhóm; lệnh sẽ tiếp tục đồng bộ tới máy offline."); } catch (Exception ex) { ShowError(ex); }
    }
    private async void LeaveGroupClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking) return;
        var group = store.Groups().Find(g => g.Id == selectedId); if (group is null || !group.Members.Contains(identity.Id)) return;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Rời nhóm “" + group.Name + "”?", 23, true));
        panel.Children.Add(Paragraph("Bạn sẽ không nhận tin mới. Lịch sử đang có được giữ lại trên máy ở chế độ chỉ đọc.", 13));
        ComboBox? successor = null;
        if (group.OwnerId == identity.Id)
        {
            var remaining = group.Members.Where(id => id != identity.Id).Select(id => new ContactRow(id, node.FindPeer(id)?.DisplayName ?? id[..12], "", "", false)).ToArray();
            if (remaining.Length == 0) panel.Children.Add(Paragraph("Bạn là thành viên cuối cùng. Rời nhóm sẽ xóa nhóm và lịch sử trên các máy.", 13, true));
            else
            {
                panel.Children.Add(Paragraph("Chuyển quyền chủ nhóm", 13, true));
                successor = new ComboBox { ItemsSource = new[] { new ContactRow("", "Chọn ngẫu nhiên", "", "", false) }.Concat(remaining), DisplayMemberPath = "Name", SelectedIndex = 0 }; panel.Children.Add(successor);
            }
        }
        if (!ShowDialog("Rời nhóm", panel, "Rời nhóm")) return;
        try { CloseMediaViews(); var next = await node.LeaveGroupAsync(group.Id, (successor?.SelectedItem as ContactRow)?.Id); Refresh(); ShowToast(next is null ? "Đã rời nhóm." : "Đã rời nhóm. Chủ nhóm mới: " + (node.FindPeer(next)?.DisplayName ?? next[..12])); } catch (Exception ex) { ShowError(ex); }
    }
    private async void ConversationRulesClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking || selectedId.Length == 0) return;
        var id = selectedId; var group = store.Groups().Find(g => g.Id == id); var rules = node.ConversationRules(id);
        bool canEdit = group is null || group.OwnerId == identity.Id;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Quyền hội thoại", 23, true));
        var capture = new CheckBox { Content = "Chặn chụp và ghi màn hình", IsChecked = rules.CaptureProtected, IsEnabled = canEdit, Margin = new Thickness(0, 8, 0, 8) };
        var forward = new CheckBox { Content = "Cho phép chuyển tiếp tin nhắn và tệp", IsChecked = rules.ForwardAllowed, IsEnabled = canEdit, Margin = new Thickness(0, 8, 0, 12) };
        capture.SetResourceReference(Control.ForegroundProperty, "TextBrush"); forward.SetResourceReference(Control.ForegroundProperty, "TextBrush"); panel.Children.Add(capture); panel.Children.Add(forward);
        panel.Children.Add(Paragraph("Đồng bộ cho mọi người trong hội thoại. Chống chụp áp dụng cho cửa sổ đang mở log; không ngăn được camera bên ngoài hoặc mọi công cụ chụp. Bản web sẽ ẩn log được bảo vệ vì trình duyệt không có khả năng chặn chụp.", 12, muted: true));
        if (!canEdit) panel.Children.Add(Paragraph("Chỉ chủ nhóm được thay đổi các quyền này.", 12, muted: true));
        if (!ShowDialog("Quyền hội thoại", panel, canEdit ? "Lưu thay đổi" : "Đóng") || !canEdit) return;
        try { await node.SetConversationRulesAsync(id, capture.IsChecked == true, forward.IsChecked == true); renderKey = ""; Refresh(); } catch (Exception ex) { ShowError(ex); }
    }
    private async Task ForwardMessage(ChatMessage message)
    {
        if (appLocking || ConversationProtected) return;
        var choices = node.Peers.Where(p => p.Trusted && !p.Blocked).Select(p => new ContactRow(p.Id, p.DisplayName, p.Initials, "", false))
            .Concat(store.Groups().Where(g => g.Members.Contains(identity.Id)).Select(g => new ContactRow(g.Id, g.Name, "#", "", true))).Where(c => c.Id != message.ConversationId && (!App.Vault.IsConversationLocked(c.Id) || unlockedConversations.Contains(c.Id))).ToArray();
        if (!node.ConversationRules(message.ConversationId).ForwardAllowed) { ShowToast("Hội thoại này không cho phép chuyển tiếp."); return; }
        if (choices.Length == 0) { ShowToast("Chưa có hội thoại khác đang mở khóa để chia sẻ."); return; }
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Chia sẻ sang hội thoại", 23, true));
        var target = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = 0 }; panel.Children.Add(target);
        if (!ShowDialog("Chuyển tiếp", panel, "Chia sẻ")) return;
        try { ShowToast("Đang chuẩn bị nội dung chia sẻ…"); await node.ForwardMessageAsync(message.Id, ((ContactRow)target.SelectedItem).Id, uiLifetime.Token); ShowToast("Đã chuyển tiếp."); } catch (Exception ex) { ShowError(ex); }
    }
    private void ShowPinnedMessagesClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking) return;
        var ids = store.PinnedMessages(selectedId); var panel = new StackPanel(); panel.Children.Add(Paragraph("Tin nhắn đã ghim", 23, true));
        foreach (var message in store.Messages(selectedId, limit: int.MaxValue).Where(m => ids.Contains(m.Id)))
        {
            var button = new Button { Content = new TextBlock { Text = Author(message) + " · " + Snippet(message), TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 5, 0, 5), HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => { InAppDialog.CancelAll(this); GoToMessage(message.Id); }; panel.Children.Add(button);
        }
        ShowDialog("Tin nhắn đã ghim", panel, "Đóng");
    }
    private void FocusComposer()
    {
        if (Mouse.RightButton == MouseButtonState.Pressed) return;
        Dispatcher.BeginInvoke(() => { if (!appLocking && !ConversationProtected && Composer.IsEnabled && !InAppDialog.IsOpen(this)) { Composer.Focus(); Composer.CaretIndex = Composer.Text.Length; } }, DispatcherPriority.Input);
    }
}

using System.Windows;
using System.Windows.Controls;

namespace Lantern.App;

public partial class MainWindow
{
    private string contactSection = "chats", directorySection = "friends";
    private void SetContactSection(string section)
    {
        if (appLocking) return;
        contactSection = section;
        if (section != "chats") directorySection = section;
        ContactSearch.Clear(); Refresh(); ContactsList.Focus();
    }
    private void DirectoryClick(object sender, RoutedEventArgs e) => SetContactSection(directorySection);
    private void FriendsClick(object sender, RoutedEventArgs e) => SetContactSection("friends");
    private void LanContactsClick(object sender, RoutedEventArgs e) => SetContactSection("lan");
    private void ShowContact(string id)
    {
        var peer = node.FindPeer(id);
        var hasHistory = store.ConversationActivity().ContainsKey(id) && !store.Preference(id).Hidden;
        SetContactSection(peer is null || hasHistory ? "chats" : peer.Trusted ? "friends" : "lan");
        ContactsList.SelectedItem = ContactsList.Items.OfType<ContactRow>().FirstOrDefault(row => row.Id == id);
    }
    private void RefreshDirectoryHeader(int online)
    {
        bool chats = contactSection == "chats", friends = contactSection == "friends";
        ContactSectionTitle.Text = chats ? "TRÒ CHUYỆN" : friends ? "BẠN BÈ" : "NGƯỜI TRONG LAN";
        DirectoryTabs.Visibility = chats ? Visibility.Collapsed : Visibility.Visible;
        ContactSearchHint.Text = chats ? "Tìm cuộc trò chuyện…" : "Tìm tên hoặc IP…";
        ContactSearch.ToolTip = ContactSearchHint.Text;
        System.Windows.Automation.AutomationProperties.SetName(ContactSearch, ContactSearchHint.Text);
        System.Windows.Automation.AutomationProperties.SetName(ContactsList, chats ? "Danh sách trò chuyện" : friends ? "Danh sách bạn bè" : "Danh sách người trong LAN");
        void Highlight(Button button, bool active) { if (active) button.SetResourceReference(Control.BackgroundProperty, "AccentSoftBrush"); else button.ClearValue(Control.BackgroundProperty); }
        Highlight(ChatsButton, chats); Highlight(DirectoryButton, !chats); Highlight(FriendsTab, friends); Highlight(LanContactsTab, contactSection == "lan");
        ChatsIcon.SetResourceReference(TextBlock.ForegroundProperty, chats ? "AccentBrush" : "TextBrush");
        DirectoryIcon.SetResourceReference(TextBlock.ForegroundProperty, !chats ? "AccentBrush" : "TextBrush");
        NoContactsTitle.Text = ContactSearch.Text.Length > 0 ? "Không tìm thấy kết quả" : chats ? "Chưa có cuộc trò chuyện" : friends ? "Chưa có bạn bè" : "Chưa lưu máy trong LAN";
        NoContactsHint.Text = chats ? "Mở Danh bạ để chọn bạn bè. Hội thoại xuất hiện ở đây khi có tin nhắn hoặc tệp được gửi, nhận." : friends ? "Các máy đã xác minh được lưu ở đây, kể cả khi chưa chat hoặc đã xóa hội thoại." : "Bấm Tìm máy trong LAN để quét. IP đã phát hiện được giữ lại; chọn máy để kết nối và xác minh.";
        OnlineCount.Text = $"{online} online";
    }
    private ContextMenu DirectoryMenu(ContactRow row)
    {
        var menu = new ContextMenu(); var peer = node.FindPeer(row.Id); if (peer is null) return menu;
        var open = new MenuItem { Header = peer.Trusted ? "Mở trò chuyện" : "Kết nối và xác minh" };
        open.Click += async (_, _) =>
        {
            try { if (!peer.Trusted && !peer.Connected) await node.ConnectAsync(peer.Address, peer.Port, peer.Id); ShowContact(peer.Id); }
            catch (Exception ex) { ShowError(ex); }
        }; menu.Items.Add(open);
        var copy = new MenuItem { Header = "Sao chép IP", IsEnabled = peer.Address.Length > 0 };
        copy.Click += (_, _) => { try { Clipboard.SetText($"{peer.Address}:{peer.Port}"); ShowToast("Đã sao chép IP."); } catch (Exception ex) { ShowError(ex); } }; menu.Items.Add(copy);
        if (peer.Trusted) { var group = new MenuItem { Header = "Thêm vào nhóm" }; group.Click += (_, _) => AddContactToGroup(peer.Id); menu.Items.Add(group); }
        return menu;
    }
}

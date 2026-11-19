using System.Windows;
using System.Windows.Controls;
using Lantern.Core;

namespace Lantern.App;
public partial class MainWindow
{
    private bool messageSelection;
    private string selectionConversation = "";
    private readonly HashSet<string> selectedMessages = [];
    private ContextMenu MessageMenu(ChatMessage message)
    {
        var menu = new ContextMenu();
        var reply = new MenuItem { Header = "Trả lời" }; reply.Click += (_, _) => { BeginReply(message); FocusComposer(); }; menu.Items.Add(reply);
        var share = new MenuItem { Header = "Chia sẻ", IsEnabled = node.ConversationRules(message.ConversationId).ForwardAllowed }; share.Click += async (_, _) => await ForwardMessage(message); menu.Items.Add(share);
        var copy = new MenuItem { Header = "Copy tin nhắn" }; copy.Click += (_, _) => { if (!appLocking && !ConversationProtected) Clipboard.SetText(message.Kind == "text" ? message.Text : message.Kind == "contact" ? message.Contact?.Name ?? "" : message.FileName); }; menu.Items.Add(copy);
        if (message.Kind == "image")
        {
            var copyImage = new MenuItem { Header = "Sao chép ảnh", IsEnabled = System.IO.File.Exists(message.FilePath), ToolTip = System.IO.File.Exists(message.FilePath) ? "Sao chép ảnh gốc vào clipboard" : "Ảnh cần tải xong trước khi sao chép" };
            copyImage.Click += (_, _) => CopyChatImage(message.Id); menu.Items.Add(copyImage);
        }
        var pins = store.PinnedMessages(message.ConversationId); bool pinned = pins.Contains(message.Id);
        var pin = new MenuItem { Header = pinned ? "Bỏ ghim tin nhắn" : "Ghim tin nhắn", IsEnabled = pinned || pins.Count < LocalStore.MaxPinnedMessages, ToolTip = !pinned && pins.Count >= LocalStore.MaxPinnedMessages ? "Tối đa 3 tin nhắn. Bỏ ghim một tin trước khi ghim thêm." : null };
        pin.Click += (_, _) => { if (appLocking || ConversationProtected) return; try { store.PinMessage(message.Id, !pinned); pinnedPreviewId = pinned ? "" : message.Id; renderKey = ""; RefreshConversation(); ShowToast(pinned ? "Đã bỏ ghim." : "Đã ghim tin nhắn lên đầu hội thoại."); } catch (Exception ex) { ShowError(ex); } }; menu.Items.Add(pin);
        menu.Items.Add(new Separator());
        var select = new MenuItem { Header = "Chọn nhiều tin nhắn" };
        select.Click += (_, _) => BeginMessageSelection(message.Id); menu.Items.Add(select);
        var delete = new MenuItem { Header = "Xóa chỉ ở phía tôi" };
        delete.Click += async (_, _) => await ConfirmDeleteMessages(message.ConversationId, new HashSet<string> { message.Id }); menu.Items.Add(delete);
        return menu;
    }
    private void CopyChatImage(string messageId)
    {
        if (appLocking || ConversationProtected || captureUnavailable) return;
        var message = store.Message(messageId);
        if (message is null || message.Kind != "image" || message.ConversationId != selectedId || message.ExpiresAt <= DateTimeOffset.UtcNow) return;
        try
        {
            if (!System.IO.File.Exists(message.FilePath)) { ShowToast("Ảnh chưa tải xong hoặc tệp không còn trên máy."); return; }
            ClipboardImages.CopyLocal(message.FilePath); ShowToast("Đã sao chép ảnh.");
        }
        catch (System.Runtime.InteropServices.ExternalException) { ShowToast("Clipboard đang bận. Đại ca thử sao chép lại."); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ResetMessageSelection() { messageSelection = false; selectionConversation = ""; selectedMessages.Clear(); SelectionBar.Visibility = Visibility.Collapsed; }
    private void UpdateSelectionCount() { SelectionCount.Text = $"Đã chọn {selectedMessages.Count} tin nhắn"; DeleteSelectedButton.IsEnabled = selectedMessages.Count > 0; }
    private void BeginMessageSelection(string? first = null)
    {
        if (selectedId.Length == 0 || ConversationProtected || appLocking) return;
        messageSelection = true; selectionConversation = selectedId; selectedMessages.Clear(); if (first is not null) selectedMessages.Add(first);
        SelectionBar.Visibility = Visibility.Visible; UpdateSelectionCount(); renderKey = ""; RefreshConversation();
    }
    private void SelectMessagesClick(object sender, RoutedEventArgs e) => BeginMessageSelection();
    private void CancelSelectionClick(object sender, RoutedEventArgs e) { ResetMessageSelection(); renderKey = ""; RefreshConversation(); }
    private async void DeleteSelectedClick(object sender, RoutedEventArgs e) => await ConfirmDeleteMessages(selectedId, selectedMessages.ToHashSet());
    private async void ClearHistoryClick(object sender, RoutedEventArgs e) => await ConfirmDeleteMessages(selectedId, null);
    private async Task ConfirmDeleteMessages(string conversation, HashSet<string>? ids)
    {
        if (conversation != selectedId || ConversationProtected || appLocking || ids?.Count == 0) return;
        var panel = new StackPanel();
        panel.Children.Add(Paragraph(ids is null ? "Dọn lịch sử hội thoại?" : $"Xóa {ids.Count} tin nhắn?", 23, true));
        panel.Children.Add(Paragraph("Xóa khỏi lịch sử của bạn và đồng bộ sang điện thoại đã ghép đôi. Bản ở máy đối tác vẫn còn. Tệp gốc bên ngoài workspace được giữ nguyên. Thao tác không thể hoàn tác.", 13));
        TextBox? confirm = null;
        if (ids is null) { panel.Children.Add(Paragraph("Nhập XÓA để xác nhận dọn toàn bộ lịch sử.", 13, true)); confirm = new TextBox(); panel.Children.Add(confirm); }
        if (!ShowDialog("Xác nhận xóa", panel, "Xóa lịch sử", () => confirm is null || confirm.Text.Trim() == "XÓA", confirm)) return;
        if (closing || appLocking || ConversationProtected || selectedId != conversation) return;
        try
        {
            CloseMediaViews(); foreach (var player in players) player.Close();
            if (ids is null) await node.DeleteConversationHistoryAsync(conversation); else await node.DeleteMessagesAsync(conversation, ids);
            if (ids is null || ids.Contains(replyDrafts.GetValueOrDefault(conversation, ""))) replyDrafts.Remove(conversation);
            ResetMessageSelection(); renderKey = ""; Refresh(); ShowToast("Đã xóa khỏi lịch sử của bạn.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
}

namespace Lantern.Core;

public sealed record MessageNotification(string Title, string Body);
public static class MessageNotifications
{
    public static MessageNotification Create(ChatMessage message, string sender, string mode, bool protectedConversation)
    {
        if (mode == "private" || protectedConversation) return new("Lantern · Tin nhắn mới", "Bạn có tin nhắn mới.");
        return new(sender, message.Kind == "text" ? message.Text : message.Kind == "contact" ? "Đã chia sẻ một liên hệ" : "Đã gửi " + (message.Kind == "image" ? "ảnh: " : message.Kind == "video" ? "video: " : "tệp: ") + message.FileName);
    }
}

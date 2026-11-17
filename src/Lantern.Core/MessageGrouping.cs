namespace Lantern.Core;

public static class MessageGrouping
{
    public static bool CanJoin(ChatMessage? previous, ChatMessage? next)
    {
        if (previous is null || next is null || previous.ConversationId != next.ConversationId || previous.Outgoing != next.Outgoing || previous.SenderId.Length == 0 || previous.SenderId != next.SenderId) return false;
        var gap = next.Time - previous.Time;
        return gap >= TimeSpan.Zero && gap <= TimeSpan.FromMinutes(5) && previous.Time.LocalDateTime.Date == next.Time.LocalDateTime.Date;
    }
}

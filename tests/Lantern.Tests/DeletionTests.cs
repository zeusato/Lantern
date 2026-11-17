using Lantern.Core;
internal static class DeletionTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        var directory = Path.Combine(root, "manual-deletion"); using var identity = new Identity(directory, "Delete fixture"); using var store = new LocalStore(directory); await using var node = new PeerNode(identity, store);
        var first = new ChatMessage { ConversationId = "a", Text = "one", Status = "delivered", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        var second = new ChatMessage { ConversationId = "a", Text = "two", Status = "delivered" };
        var other = new ChatMessage { ConversationId = "b", Text = "keep", Status = "delivered" };
        store.SaveIncomingMessage(first); store.SaveIncomingMessage(second); store.SaveIncomingMessage(other);
        await node.DeleteMessagesAsync("a", new HashSet<string> { first.Id, other.Id });
        check(store.Message(first.Id) is null && store.Message(second.Id) is not null && store.Message(other.Id) is not null, "Selected deletion is scoped to its conversation");
        check(store.UnreadCount() == 2 && store.WasDeleted(first.Id), "Selected deletion clears unread and stores replay tombstone");
        check(store.HistoryVersions().GetValueOrDefault("a") == 1 && !store.HistoryVersions().ContainsKey("b"), "Deletion increments only the affected mobile cache revision");
        store.SaveMessage(first); check(store.Message(first.Id) is null, "Late transfer or send completion cannot resurrect a deleted message");
        var source = Path.Combine(root, "original-delete-test.txt"); await File.WriteAllTextAsync(source, "original");
        var internalFile = Path.Combine(store.MediaPath, "owned.txt"); await File.WriteAllTextAsync(internalFile, "owned");
        store.SaveMessage(new ChatMessage { ConversationId = "a", Kind = "file", FilePath = source });
        store.SaveMessage(new ChatMessage { ConversationId = "a", Kind = "file", FilePath = internalFile });
        await node.DeleteConversationHistoryAsync("a");
        check(store.Messages("a").Count == 0 && store.Messages("b").Count == 1, "Clear history preserves other conversations");
        check(File.Exists(source) && !File.Exists(internalFile), "Deletion removes app-owned copies and preserves original files");
        check(store.UnreadCount() == 1 && !store.SaveIncomingMessage(second), "Clear history rejects replay and keeps other unread counts");
        check(new AppSettings().StartWithWindows && Codec.Decode<AppSettings>("{}").StartWithWindows, "Windows autostart defaults on for new and upgraded settings");
        check(!Codec.Decode<AppSettings>("{\"startWithWindows\":false}").StartWithWindows, "Explicit Windows autostart opt-out is preserved");
    }
}

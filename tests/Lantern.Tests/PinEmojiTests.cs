using Lantern.Core;
using Microsoft.Data.Sqlite;

internal static class PinEmojiTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "pin-emoji");
        var messages = Enumerable.Range(0, 5).Select(index => new ChatMessage { ConversationId = "chat", Text = "Message " + index }).ToArray();
        using (var store = new LocalStore(directory))
        {
            foreach (var message in messages) store.SaveMessage(message);
            foreach (var message in messages.Take(3)) store.PinMessage(message.Id, true);
            store.PinMessage(messages[0].Id, true);
            check(store.PinnedMessages("chat").SequenceEqual(messages.Take(3).Reverse().Select(message => message.Id)), "Pins retain pin order and repeated pinning is idempotent");
            bool rejected = false; try { store.PinMessage(messages[3].Id, true); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && store.PinnedMessages("chat").Count == 3, "A fourth pin is rejected without replacing existing pins");
            store.PinMessage(messages[0].Id, false); store.PinMessage(messages[3].Id, true);
            check(store.PinnedMessages("chat").First() == messages[3].Id, "Unpinning frees a slot and the new pin appears first");
            var other = new ChatMessage { ConversationId = "other", Text = "Other log" }; store.SaveMessage(other); store.PinMessage(other.Id, true);
            check(store.PinnedMessages("other").Count == 1, "Pin limits are independent for each conversation");
            store.DeleteMessages("chat", new HashSet<string> { messages[1].Id });
            check(!store.PinnedMessages("chat").Contains(messages[1].Id), "Deleting a pinned message removes its pin");
            messages[2].DisappearAfterSeconds = 10; store.SaveMessage(messages[2]);
            store.SetMessageExpiry(messages[2].Id, DateTimeOffset.UtcNow.AddSeconds(-1)); store.RemoveExpired(DateTimeOffset.UtcNow);
            check(!store.PinnedMessages("chat").Contains(messages[2].Id), "Expiring a message removes its pin");
        }
        using (var reopened = new LocalStore(directory)) check(reopened.PinnedMessages("chat").SequenceEqual(new[] { messages[3].Id }), "Pinned message selection persists across restarts");
        var legacy = Path.Combine(root, "legacy-pins");
        using (var store = new LocalStore(legacy)) foreach (var message in messages) store.SaveMessage(message);
        using (var db = new SqliteConnection($"Data Source={Path.Combine(legacy, "history.db")};Pooling=False"))
        {
            db.Open(); foreach (var message in messages) { using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO pinned_messages(id,conversation) VALUES($id,'chat')"; cmd.Parameters.AddWithValue("$id", message.Id); cmd.ExecuteNonQuery(); }
        }
        using (var upgraded = new LocalStore(legacy)) check(upgraded.PinnedMessages("chat").SequenceEqual(messages.Skip(2).Reverse().Select(message => message.Id)) && upgraded.Messages("chat").Count == 5, "Upgrade keeps the newest three legacy pins without deleting messages");
        check(Emoticons.Replace("/-heart /-strong :joy: :smile:") == "❤️ 👍 😂 😄", "Zalo and named shortcodes produce interoperable Unicode where available");
        check(Emoticons.Replace(":)) :v /-SHOWLOVE") == ":)) :v /-showlove", "Distinctive Zalo expressions preserve their canonical insertion codes");
        var literal = "https://example.test/:v C:\\:v `:v /-heart` ```\n/-heart\n``` abc:v";
        check(Emoticons.Replace(literal) == literal, "Zalo shortcuts preserve URLs, paths, code blocks and embedded text");
        check(Emoticons.Replace("/-heart", false) == "/-heart" && Emoticons.Replace("/-heart ", false) == "❤️ ", "New shortcodes wait for a delimiter while composing");
    }
}

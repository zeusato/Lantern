using System.Security.Cryptography;
using Lantern.Core;

internal static class BackupTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source"); var destination = Path.Combine(root, "new-machine"); var file = Path.Combine(root, "move.lanternbackup");
        const string password = "backup-password-123";
        using var vault = WorkspaceVault.Create(source, "original-password");
        using var identity = new Identity(source, "Backup user", vault);
        using var store = new LocalStore(source, vault);
        var external = Path.Combine(root, "outside.txt"); File.WriteAllText(external, "external attachment");
        var media = Path.Combine(store.MediaPath, "image.bin"); File.WriteAllBytes(media, RandomNumberGenerator.GetBytes(1200000));
        var chat = new string('a', 64);
        store.SavePeer(new Peer { Id = chat, Name = "Friend", Trusted = true, Address = "10.10.15.10", Port = 45853 });
        store.SaveIncomingMessage(new ChatMessage { Id = Guid.NewGuid().ToString("N"), ConversationId = chat, SenderId = chat, Kind = "text", Text = "Private history", Time = DateTimeOffset.UtcNow });
        foreach (var path in new[] { external, media, Path.Combine(root, "missing.txt") })
            store.SaveMessage(new ChatMessage { Id = Guid.NewGuid().ToString("N"), ConversationId = chat, SenderId = chat, Kind = "file", FilePath = path, FileName = Path.GetFileName(path), Time = DateTimeOffset.UtcNow });
        vault.SetConversationPassword(chat, "conversation-password");
        File.WriteAllText(Path.Combine(source, "settings.json"), "{\"Name\":\"Backup user\"}");
        Directory.CreateDirectory(Path.Combine(source, "stickers", "custom")); File.WriteAllText(Path.Combine(source, "stickers", "custom", "test.txt"), "sticker content");
        WorkspaceBackup.Export(file, password, identity, vault, store);
        check(File.Exists(file) && !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("Private history"), "Backup writes a single encrypted file while the original store stays open");
        WorkspaceBackup.Restore(file, password, destination);
        using var restoredVault = WorkspaceVault.Open(destination, password);
        using var restoredIdentity = new Identity(destination, "Backup user", restoredVault);
        using var restored = new LocalStore(destination, restoredVault);
        check(restoredIdentity.Id == identity.Id && restored.Peers().Single().Trusted, "Restore preserves identity and trusted contacts with new Windows protection");
        check(restored.Messages(limit: 20).Any(m => m.Text == "Private history") && restored.UnreadCount() == store.UnreadCount(), "Restore preserves encrypted history and unread state");
        check(restoredVault.VerifyConversation(chat, "conversation-password"), "Restore preserves conversation passwords");
        var paths = restored.Messages(limit: 20).Where(m => m.Kind == "file").ToDictionary(m => m.FileName, m => m.FilePath);
        check(File.ReadAllText(paths["outside.txt"]) == "external attachment" && File.ReadAllBytes(paths["image.bin"]).SequenceEqual(File.ReadAllBytes(media)), "Restore rebases internal and external attachments");
        check(paths["missing.txt"].StartsWith(restored.MediaPath) && !File.Exists(paths["missing.txt"]) && restored.Messages(limit: 20).Single(m => m.FileName == "missing.txt").Status == "offered", "Missing incoming files retain a valid destination so they can be downloaded again");
        check(File.ReadAllText(Path.Combine(destination, "stickers", "custom", "test.txt")) == "sticker content", "Restore includes custom stickers");
        check(store.Messages(limit: 20).Any(m => m.FilePath == external) && vault.VerifyAccountPassword("original-password"), "Export leaves original file paths and login password unchanged");
        bool refused = false;
        try { WorkspaceBackup.Restore(file, password, destination); } catch (InvalidOperationException) { refused = true; }
        check(refused, "Restore refuses to overwrite an existing workspace");
        foreach (var mode in new[] { "wrong-password", "tampered", "truncated" })
        {
            var candidate = Path.Combine(root, mode + ".lanternbackup"); var bytes = File.ReadAllBytes(file);
            if (mode == "tampered") bytes[80] ^= 1;
            if (mode == "truncated") bytes = bytes[..^20];
            File.WriteAllBytes(candidate, bytes); var target = Path.Combine(root, mode); bool rejected = false;
            try { WorkspaceBackup.Restore(candidate, mode == "wrong-password" ? "wrong-password" : password, target); }
            catch (Exception ex) when (ex is CryptographicException or IOException) { rejected = true; }
            check(rejected && WorkspaceBackup.CanRestore(target) && !Directory.EnumerateFileSystemEntries(target).Any(), "Restore rejects " + mode + " without installing partial data");
        }
    }
}

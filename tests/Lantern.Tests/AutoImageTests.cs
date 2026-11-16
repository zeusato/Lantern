using System.Security.Cryptography;
using Lantern.Core;

internal static class AutoImageTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        static async Task Until(Func<bool> ready)
        {
            var end = DateTime.UtcNow.AddSeconds(18);
            while (!ready()) { if (DateTime.UtcNow > end) throw new TimeoutException("Automatic image transfer"); await Task.Delay(35); }
        }
        using var senderIdentity = new Identity(Path.Combine(root, "image-sender"), "Sender");
        using var receiverIdentity = new Identity(Path.Combine(root, "image-receiver"), "Receiver");
        using var senderStore = new LocalStore(Path.Combine(root, "image-sender"));
        using var receiverStore = new LocalStore(Path.Combine(root, "image-receiver"));
        await using var sender = new PeerNode(senderIdentity, senderStore);
        await using var receiver = new PeerNode(receiverIdentity, receiverStore) { AutoReceiveImages = true };
        await sender.StartAsync(0, false, true); await receiver.StartAsync(0, false, true);
        await sender.ConnectAsync("127.0.0.1", receiver.Port, receiverIdentity.Id); await Until(() => receiver.IsConnected(senderIdentity.Id));
        await sender.TrustAsync(receiverIdentity.Id); await receiver.TrustAsync(senderIdentity.Id);
        int maxTransfers = 0;
        receiver.Changed += () => { var count = receiverStore.Messages().Count(m => m.Status == "receiving"); int before; do { before = Volatile.Read(ref maxTransfers); if (count <= before) break; } while (Interlocked.CompareExchange(ref maxTransfers, count, before) != before); };
        var source = Path.Combine(root, "auto-picture.png"); var bytes = RandomNumberGenerator.GetBytes(512 * 1024 + 73); await File.WriteAllBytesAsync(source, bytes);
        var burst = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => sender.OfferFileAsync(receiverIdentity.Id, source)));
        await Until(() => burst.All(m => receiverStore.Message(m.Id)?.Status == "received"));
        check(true, "A burst of five images downloads automatically from a paired peer");
        check(burst.All(m => File.ReadAllBytes(receiverStore.Message(m.Id)!.FilePath).SequenceEqual(bytes)), "Auto-received image bytes pass the normal SHA-256 transfer checks");
        check(maxTransfers is > 0 and <= 3, "Automatic images respect the three-transfer concurrency limit");
        var file = Path.Combine(root, "manual-document.txt"); var video = Path.Combine(root, "manual-video.mp4"); await File.WriteAllTextAsync(file, "Manual file"); await File.WriteAllTextAsync(video, "Manual video fixture");
        var offeredFile = await sender.OfferFileAsync(receiverIdentity.Id, file); var offeredVideo = await sender.OfferFileAsync(receiverIdentity.Id, video);
        await Until(() => receiverStore.Message(offeredFile.Id) is not null && receiverStore.Message(offeredVideo.Id) is not null); await Task.Delay(200);
        check(receiverStore.Message(offeredFile.Id)!.Status == "offered" && receiverStore.Message(offeredVideo.Id)!.Status == "offered" && !File.Exists(receiverStore.Message(offeredFile.Id)!.FilePath), "Documents and videos still require explicit acceptance");
        receiver.AutoReceiveImages = false;
        var pending = await sender.OfferFileAsync(receiverIdentity.Id, source); await Until(() => receiverStore.Message(pending.Id) is not null);
        receiverStore.UpdateMessage(pending.Id, m => m.DisappearAfterSeconds = 10); await receiver.MarkReadAsync(senderIdentity.Id);
        check(receiverStore.UnreadCount() == 1 && receiverStore.Message(pending.Id)!.ExpiresAt is null, "An undownloaded image remains unread and its expiry timer does not start");
        await File.WriteAllBytesAsync(receiverStore.Message(pending.Id)!.FilePath + ".part", bytes[..65536]); receiver.AutoReceiveImages = true;
        await Until(() => receiverStore.Message(pending.Id)?.Status == "received"); await receiver.MarkReadAsync(senderIdentity.Id);
        check(receiverStore.UnreadCount() == 0 && receiverStore.Message(pending.Id)!.ExpiresAt is not null, "Automatic image resume completes before reading starts its expiry timer");
    }
}

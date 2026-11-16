using Lantern.Core;

internal static class UiPeer
{
    public static async Task Run(string uiDirectory, string assetsDirectory)
    {
        uiDirectory = Path.GetFullPath(uiDirectory); assetsDirectory = Path.GetFullPath(assetsDirectory);
        using var identity = new Identity(Path.Combine(assetsDirectory, "peer-profile"), "Minh Anh · máy kiểm thử");
        using var store = new LocalStore(Path.Combine(assetsDirectory, "peer-profile"));
        using var uiIdentity = new Identity(uiDirectory, "Lantern QA");
        await using var node = new PeerNode(identity, store); await node.StartAsync(0, false, true);
        store.SavePeer(new Peer { Id = uiIdentity.Id, Name = "Lantern QA", Address = "127.0.0.1", Trusted = true });
        using (var gui = new LocalStore(uiDirectory)) gui.SavePeer(new Peer { Id = identity.Id, Name = identity.Name, Address = "127.0.0.1", Port = node.Port, Trusted = true });
        Console.WriteLine($"UI fixture listening on 127.0.0.1:{node.Port}. Code: {Identity.VerificationCode(identity.Id, uiIdentity.Id)}");
        bool greeted = false;
        var end = DateTime.UtcNow.AddMinutes(45);
        while (DateTime.UtcNow < end)
        {
            if (node.IsConnected(uiIdentity.Id))
            {
                if (node.FindPeer(uiIdentity.Id) is { Trusted: false }) await node.TrustAsync(uiIdentity.Id);
                if (!greeted)
                {
                    greeted = true;
                    await node.SendTextAsync(uiIdentity.Id, "Chào bạn 👋 Đây là cuộc trò chuyện kiểm thử giữa hai peer thật trên cùng máy.");
                    await node.SendTextAsync(uiIdentity.Id, "Mình gửi bản thiết kế và một video mẫu nhé. Bạn có thể xem ngay trong cuộc trò chuyện sau khi tải về.");
                    foreach (var file in new[] { "Bang-mau-Lantern.png", "Video-kiem-thu.mp4" })
                    { var path = Path.Combine(assetsDirectory, file); if (File.Exists(path)) await node.OfferFileAsync(uiIdentity.Id, path); }
                }
                foreach (var msg in store.Messages().Where(m => !m.Outgoing && m.Kind != "text" && m.Status == "offered")) await node.AcceptFileAsync(msg.Id);
                await node.MarkReadAsync(uiIdentity.Id);
            }
            await Task.Delay(300);
        }
    }
}

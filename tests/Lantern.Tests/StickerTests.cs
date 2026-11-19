using System.IO.Compression;
using Lantern.Core;

internal static class StickerTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aF0cAAAAASUVORK5CYII=");
        void Invalid(Action action, string label) { bool rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { rejected = true; } check(rejected, label); }
        var library = new StickerLibrary(Path.Combine(root, "author")); var pack = library.Create("Cảm xúc Đại ca");
        var item = library.Add(pack, "Cười vui vẻ", png); var reference = new StickerReference(pack.Id, pack.Name, item.Id, item.Name);
        var reopened = new StickerLibrary(Path.Combine(root, "author")); check(reopened.Packs().Single().Stickers.Single().Name == item.Name, "Pack and sticker names persist across restart");
        check(StickerLibrary.SearchKey("Cười ĐẠI ca").Contains("cuoi dai"), "Vietnamese sticker search ignores accents and case");
        Invalid(() => library.Create("  "), "Empty pack names are rejected");
        Invalid(() => library.Add(pack, "bad", new byte[30]), "Non-PNG/GIF files cannot masquerade as stickers");
        Invalid(() => library.AddRange(pack, [("Good", png), ("Bad", new byte[30])]), "Batch import validates every image before committing the pack");
        check(library.Packs().Single().Stickers.Count == 1, "A failed batch adds no partial stickers");
        var huge = png.ToArray(); huge[16] = 127; Invalid(() => library.Add(pack, "huge", huge), "Oversized decoded dimensions are rejected before image allocation");
        Invalid(() => new StickerReference("../escape", "A", "ok", "B").Validate(), "Sticker identifiers reject traversal");
        var archive = Path.Combine(root, "roundtrip.lanternstickers"); library.Export(pack, archive);
        var receiving = new StickerLibrary(Path.Combine(root, "receiver")); var imported = receiving.Import(archive);
        check(imported.Id != pack.Id && File.ReadAllBytes(receiving.PathFor(imported, imported.Stickers.Single())).SequenceEqual(png), "Pack export/import preserves original bytes and creates an independent editable copy");
        imported.Name = "Tên mới"; imported.Stickers[0].Name = "Tìm kiếm mới"; receiving.Save(imported);
        check(receiving.Packs().Single().Name == "Tên mới" && library.Packs().Single().Name == pack.Name, "Editing an imported pack does not mutate its source");
        var traversal = Path.Combine(root, "traversal.lanternstickers"); File.Copy(archive, traversal);
        using (var zip = ZipFile.Open(traversal, ZipArchiveMode.Update)) zip.CreateEntry("../outside.txt");
        Invalid(() => receiving.Import(traversal), "Pack imports reject entries outside the manifest");
        var corrupt = Path.Combine(root, "corrupt.lanternstickers"); File.Copy(archive, corrupt);
        using (var zip = ZipFile.Open(corrupt, ZipArchiveMode.Update)) { zip.GetEntry(item.File)!.Delete(); using var output = zip.CreateEntry(item.File).Open(); var altered = png.ToArray(); altered[^1] ^= 1; output.Write(altered); }
        Invalid(() => receiving.Import(corrupt), "Pack import verifies every asset hash");
        check(receiving.Packs().Count == 1, "Failed imports leave no partial pack");
        var seed = new StickerLibrary(Path.Combine(root, "seed")); seed.InstallBundled(pack, _ => png); seed.Delete(pack); seed.InstallBundled(pack, _ => throw new Exception("Should not reinstall"));
        check(seed.Packs().Count == 0, "Deleted bundled packs stay deleted after restart");
        var metadataPack = new StickerPack { Id = "zalo-99", Name = "Zalo · 99", Stickers = [new() { Id = "zalo-9", Name = "Sticker 9", File = item.File }] };
        seed.InstallBundled(metadataPack, _ => png); metadataPack.Name = "Tên bộ chính xác"; metadataPack.Stickers[0].Name = "Ngủ ngon"; metadataPack.Stickers[0].Keywords = ["ngủ ngon", "good night"];
        seed.InstallBundled(metadataPack, _ => throw new Exception("Must not reload media")); var upgraded = seed.Packs().Single();
        check(upgraded.Name == metadataPack.Name && upgraded.Stickers[0].Name == "Ngủ ngon", "Bundled metadata upgrades existing placeholder names without copying images");
        upgraded.Name = "Tên riêng"; upgraded.Stickers[0].Name = "Tên sticker riêng"; seed.Save(upgraded); seed.InstallBundled(metadataPack, _ => png);
        check(seed.Packs().Single().Name == "Tên riêng" && seed.Packs().Single().Stickers[0].Name == "Tên sticker riêng", "Metadata updates preserve user names");
        var search = new StickerSearch(seed.Packs()); check(search.Suggest("Chúc NGU NGON!").Count == 1 && search.Suggest("good night").Count == 1 && search.Suggest("người").Count == 0 && search.Suggest("").Count == 0, "Suggestions match normalized trailing phrases with word boundaries");
        var nullItem = Codec.Decode<StickerPack>("{\"Id\":\"valid\",\"Name\":\"Valid\",\"Stickers\":[null]}"); Invalid(() => receiving.Save(nullItem), "Malformed null manifest items are rejected");

        var identities = Enumerable.Range(0, 3).Select(i => new Identity(Path.Combine(root, "peer" + i), "Sticker peer " + i)).ToArray();
        var stores = Enumerable.Range(0, 3).Select(i => new LocalStore(Path.Combine(root, "peer" + i))).ToArray();
        var nodes = Enumerable.Range(0, 3).Select(i => new PeerNode(identities[i], stores[i]) { AutoReceiveImages = true }).ToArray();
        async Task Until(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(15); while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException("Sticker transfer"); await Task.Delay(40); } }
        try
        {
            foreach (var node in nodes) await node.StartAsync(0, false, true);
            for (int a = 0; a < 3; a++) for (int b = a + 1; b < 3; b++)
            {
                await nodes[a].ConnectAsync("127.0.0.1", nodes[b].Port, identities[b].Id); await Until(() => nodes[b].IsConnected(identities[a].Id));
                await nodes[a].TrustAsync(identities[b].Id); await nodes[b].TrustAsync(identities[a].Id);
            }
            var sent = await nodes[0].OfferFileAsync(identities[1].Id, library.PathFor(pack, item), sticker: reference);
            await Until(() => stores[1].Message(sent.Id)?.Status == "received"); var received = stores[1].Message(sent.Id)!;
            check(received.Sticker == reference && received.Kind == "image" && File.ReadAllBytes(received.FilePath).SequenceEqual(png), "TLS sticker delivery preserves metadata and auto-downloads verified media with legacy image fallback");
            var forwarded = await nodes[1].ForwardMessageAsync(sent.Id, identities[2].Id); await Until(() => stores[2].Message(forwarded.Id)?.Status == "received");
            check(stores[2].Message(forwarded.Id)!.Sticker == reference, "Forwarded stickers retain their pack and searchable name");
            var group = await nodes[0].CreateGroupAsync("Sticker group", [identities[1].Id, identities[2].Id]); await Until(() => stores.Skip(1).All(s => s.Groups().Any(g => g.Id == group.Id)));
            var grouped = await nodes[0].OfferFileAsync(group.Id, library.PathFor(pack, item), sticker: reference); await Until(() => stores.Skip(1).All(s => s.Message(grouped.Id)?.Status == "received"));
            check(true, "Group members automatically receive the same sticker");
            var sentPack = await nodes[0].OfferFileAsync(identities[1].Id, archive); await Until(() => stores[1].Message(sentPack.Id) is not null); await nodes[1].AcceptFileAsync(sentPack.Id); await Until(() => stores[1].Message(sentPack.Id)?.Status == "received");
            var sharedPack = receiving.Import(stores[1].Message(sentPack.Id)!.FilePath); check(sharedPack.Stickers.Single().Name == item.Name, "Whole packs transfer over the existing verified file channel and can be imported");
            using var history = new LocalStore(Path.Combine(root, "peer1")); check(history.Message(sent.Id)!.Sticker == reference, "Sticker identity survives persisted chat reload");
        }
        finally { foreach (var node in nodes) await node.DisposeAsync(); foreach (var store in stores) store.Dispose(); foreach (var identity in identities) identity.Dispose(); }
    }
}

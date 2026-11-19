using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Lantern.Core;

public sealed class StickerItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public List<string> Keywords { get; set; } = [];
}

public sealed class StickerPack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<StickerItem> Stickers { get; set; } = [];
    public override string ToString() => Name;
}

public sealed record StickerReference(string PackId, string PackName, string StickerId, string Name)
{
    public void Validate()
    {
        StickerLibrary.CheckId(PackId); StickerLibrary.CheckId(StickerId);
        StickerLibrary.CheckName(PackName); StickerLibrary.CheckName(Name);
    }
}

/// <summary>Local, portable packs. Media is content addressed; manifests are replaced atomically.</summary>
public sealed class StickerLibrary
{
    public const int MaxImageBytes = 8 * 1024 * 1024, MaxPackBytes = 128 * 1024 * 1024, MaxStickers = 500;
    private readonly string root;
    public StickerLibrary(string workspace) { root = Path.Combine(workspace, "stickers"); Directory.CreateDirectory(root); }
    public static void CheckId(string id) { if (id is null || id.Length is < 1 or > 80 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException("Mã sticker không hợp lệ."); }
    public static string CheckName(string name) { if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || name.Any(char.IsControl)) throw new InvalidDataException("Tên cần có 1–100 ký tự, không chứa ký tự điều khiển."); return name.Trim(); }
    private string DirectoryFor(string id) { CheckId(id); return Path.Combine(root, id); }
    public string PathFor(StickerPack pack, StickerItem item)
    {
        CheckFile(item.File); return Path.Combine(DirectoryFor(pack.Id), item.File);
    }
    private static void CheckFile(string file)
    {
        if (file is null || file.Length != 68 || !file[..64].All(Uri.IsHexDigit) || file[64..] is not (".png" or ".gif")) throw new InvalidDataException("Tên tệp sticker không hợp lệ.");
    }
    public IReadOnlyList<StickerPack> Packs()
    {
        var packs = new List<StickerPack>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var manifest = Path.Combine(dir, "pack.json"); if (!System.IO.File.Exists(manifest)) continue;
            try { var pack = ReadManifest(manifest); if (pack.Id == Path.GetFileName(dir)) packs.Add(pack); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentException) { }
        }
        return packs.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    private static StickerPack ReadManifest(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Bộ sticker quá lớn.");
        var pack = Codec.Decode<StickerPack>(System.IO.File.ReadAllText(path)); Validate(pack); return pack;
    }
    private static void Validate(StickerPack pack)
    {
        if (pack is null) throw new InvalidDataException("Thiếu bộ sticker.");
        CheckId(pack.Id); CheckName(pack.Name);
        if (pack.Stickers is null || pack.Stickers.Count > MaxStickers) throw new InvalidDataException("Mỗi bộ tối đa 500 sticker.");
        var ids = new HashSet<string>();
        foreach (var item in pack.Stickers) { if (item is null) throw new InvalidDataException("Sticker trống."); CheckId(item.Id); CheckName(item.Name); CheckFile(item.File); if (item.Keywords is null || item.Keywords.Count > 40) throw new InvalidDataException("Tối đa 40 từ khóa mỗi sticker."); foreach (var keyword in item.Keywords) CheckName(keyword); if (!ids.Add(item.Id)) throw new InvalidDataException("Mã sticker bị trùng."); }
    }
    public void Save(StickerPack pack)
    {
        Validate(pack); var dir = DirectoryFor(pack.Id); Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
        try { System.IO.File.WriteAllText(tmp, Codec.Encode(pack)); System.IO.File.Move(tmp, Path.Combine(dir, "pack.json"), true); }
        finally { System.IO.File.Delete(tmp); }
    }
    public StickerPack Create(string name) { var pack = new StickerPack { Name = CheckName(name) }; Save(pack); return pack; }
    public void Delete(StickerPack pack) { var dir = DirectoryFor(pack.Id); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    public StickerItem Add(StickerPack pack, string name, byte[] bytes)
        => AddRange(pack, [(name, bytes)]).Single();
    public IReadOnlyList<StickerItem> AddRange(StickerPack pack, IEnumerable<(string Name, byte[] Bytes)> images)
    {
        Validate(pack); var additions = new List<(StickerItem Item, byte[] Bytes)>(); long total = 0;
        foreach (var file in pack.Stickers.Select(i => i.File).Distinct()) total += new FileInfo(Path.Combine(DirectoryFor(pack.Id), file)).Length;
        foreach (var (name, bytes) in images)
        {
            if (pack.Stickers.Count + additions.Count >= MaxStickers) throw new InvalidDataException("Mỗi bộ tối đa 500 sticker.");
            var extension = ValidateImage(bytes); total += bytes.Length;
            if (total > MaxPackBytes) throw new InvalidDataException("Mỗi bộ tối đa 128 MB.");
            additions.Add((new StickerItem { Name = CheckName(name), File = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension }, bytes));
        }
        var dir = DirectoryFor(pack.Id); bool existed = Directory.Exists(dir); var created = new List<string>();
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (item, bytes) in additions)
            {
                var path = PathFor(pack, item);
                if (!System.IO.File.Exists(path)) { created.Add(path); System.IO.File.WriteAllBytes(path, bytes); }
            }
            pack.Stickers.AddRange(additions.Select(a => a.Item)); Save(pack); return additions.Select(a => a.Item).ToArray();
        }
        catch
        {
            foreach (var (item, _) in additions) pack.Stickers.Remove(item);
            foreach (var file in created) System.IO.File.Delete(file);
            if (!existed) Directory.Delete(dir, true); throw;
        }
    }
    public static string ValidateImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 24 or > MaxImageBytes) throw new InvalidDataException("Sticker cần là PNG/GIF, tối đa 8 MB.");
        int w, h; string extension;
        if (bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        { w = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4)); h = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4)); extension = ".png"; }
        else if (bytes[..6].SequenceEqual("GIF89a"u8) || bytes[..6].SequenceEqual("GIF87a"u8))
        { w = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2)); h = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2)); extension = ".gif"; }
        else throw new InvalidDataException("Chỉ nhận ảnh PNG hoặc GIF thực sự.");
        if (w is < 1 or > 2048 || h is < 1 or > 2048) throw new InvalidDataException("Sticker tối đa 2048 × 2048 pixel.");
        return extension;
    }
    public static byte[] ReadImage(string path)
    {
        if (new FileInfo(path).Length > MaxImageBytes) throw new InvalidDataException("Sticker tối đa 8 MB.");
        var bytes = System.IO.File.ReadAllBytes(path); ValidateImage(bytes); return bytes;
    }
    public static string SearchKey(string text) => new(text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    public void Export(StickerPack pack, string destination)
    {
        Validate(pack); var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("pack.json").Open())) writer.Write(Codec.Encode(pack));
                long total = 0;
                foreach (var item in pack.Stickers.DistinctBy(i => i.File))
                {
                    var bytes = ReadImage(PathFor(pack, item)); total += bytes.Length;
                    if (total > MaxPackBytes) throw new InvalidDataException("Bộ sticker vượt quá 128 MB.");
                    using var output = zip.CreateEntry(item.File, CompressionLevel.Fastest).Open(); output.Write(bytes);
                }
            }
            System.IO.File.Move(temp, destination, true);
        }
        finally { System.IO.File.Delete(temp); }
    }
    public StickerPack Import(string archive)
    {
        if (new FileInfo(archive).Length > MaxPackBytes + 1024 * 1024) throw new InvalidDataException("Bộ sticker quá lớn.");
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > MaxStickers + 1 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count) throw new InvalidDataException("Gói sticker không hợp lệ.");
        var manifest = zip.GetEntry("pack.json") ?? throw new InvalidDataException("Thiếu danh mục sticker.");
        if (manifest.Length > 1024 * 1024) throw new InvalidDataException("Danh mục quá lớn.");
        var pack = Codec.Decode<StickerPack>(Encoding.UTF8.GetString(ReadEntry(manifest, 1024 * 1024))); Validate(pack);
        pack.Id = Guid.NewGuid().ToString("N"); // Import is a copy; never overwrite another user's edits.
        var allowed = pack.Stickers.Select(i => i.File).Append("pack.json").ToHashSet(StringComparer.Ordinal);
        if (zip.Entries.Any(e => !allowed.Contains(e.FullName)) || zip.Entries.Sum(e => e.Length) > MaxPackBytes) throw new InvalidDataException("Gói chứa tệp ngoài danh mục hoặc quá lớn.");
        var dir = DirectoryFor(pack.Id); Directory.CreateDirectory(dir);
        try
        {
            foreach (var item in pack.Stickers.DistinctBy(i => i.File))
            {
                var entry = zip.GetEntry(item.File) ?? throw new InvalidDataException("Thiếu ảnh sticker.");
                if (entry.Length > MaxImageBytes) throw new InvalidDataException("Ảnh quá lớn.");
                var bytes = ReadEntry(entry, MaxImageBytes);
                var ext = ValidateImage(bytes);
                if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ext != item.File) throw new InvalidDataException("Ảnh sticker sai mã kiểm tra.");
                System.IO.File.WriteAllBytes(PathFor(pack, item), bytes);
            }
            Save(pack); return pack;
        }
        catch { Directory.Delete(dir, true); throw; }
    }
    private static byte[] ReadEntry(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length > limit) throw new InvalidDataException("Tệp trong bộ sticker quá lớn.");
        using var input = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[81920];
        int read; while ((read = input.Read(buffer)) > 0) { if (output.Length + read > limit) throw new InvalidDataException("Dữ liệu giải nén quá lớn."); output.Write(buffer, 0, read); }
        if (output.Length != entry.Length) throw new InvalidDataException("Tệp sticker thiếu dữ liệu.");
        return output.ToArray();
    }
    public void InstallBundled(string source)
    {
        if (!Directory.Exists(source)) return;
        foreach (var manifest in Directory.EnumerateFiles(source, "pack.json", SearchOption.AllDirectories))
        {
            var pack = ReadManifest(manifest);
            InstallBundled(pack, file => ReadImage(Path.Combine(Path.GetDirectoryName(manifest)!, file)));
        }
    }
    public void InstallBundled(StickerPack pack, Func<string, byte[]> read)
    {
            Validate(pack); var receipt = Path.Combine(root, pack.Id + ".installed");
            var dir = DirectoryFor(pack.Id);
            if (System.IO.File.Exists(receipt))
            {
                // Upgrade metadata only. Preserve removed packs/items and names edited by the user.
                var manifest = Path.Combine(dir, "pack.json");
                if (!System.IO.File.Exists(manifest)) return;
                var current = ReadManifest(manifest); bool changed = false;
                if (current.Name == "Zalo · " + pack.Id.Replace("zalo-", "") && current.Name != pack.Name) { current.Name = pack.Name; changed = true; }
                foreach (var item in current.Stickers)
                {
                    var source = pack.Stickers.FirstOrDefault(s => s.Id == item.Id && s.File == item.File); if (source is null) continue;
                    if (item.Name == "Sticker " + item.Id.Replace("zalo-", "") && item.Name != source.Name) { item.Name = source.Name; changed = true; }
                    var keywords = item.Keywords.Union(source.Keywords).Take(40).ToList();
                    if (!item.Keywords.SequenceEqual(keywords)) { item.Keywords = keywords; changed = true; }
                }
                if (changed) Save(current);
                return;
            }
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                try
                {
                    foreach (var item in pack.Stickers.DistinctBy(i => i.File))
                    {
                        var bytes = read(item.File);
                        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ValidateImage(bytes) != item.File) throw new InvalidDataException("Sticker đóng gói sai mã kiểm tra.");
                        System.IO.File.WriteAllBytes(PathFor(pack, item), bytes);
                    }
                    Save(pack);
                }
                catch { Directory.Delete(dir, true); throw; }
            }
            System.IO.File.WriteAllText(receipt, "1");
    }
}

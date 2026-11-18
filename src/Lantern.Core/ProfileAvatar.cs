using System.Buffers.Binary;

namespace Lantern.Core;

public static class ProfileAvatar
{
    public const int Size = 192;
    public const int MaxBytes = 160 * 1024;
    public static void Validate(string data)
    {
        if (data is null || data.Length > (MaxBytes + 2) / 3 * 4) throw new InvalidDataException("Ảnh đại diện vượt giới hạn.");
        if (data.Length == 0) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); } catch (FormatException) { throw new InvalidDataException("Ảnh đại diện không hợp lệ."); }
        if (bytes.Length is < 33 or > MaxBytes || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) != Size || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) != Size)
            throw new InvalidDataException("Ảnh đại diện cần là PNG vuông 192 × 192.");
    }
}

public sealed partial class LocalStore
{
    public string Avatar => ReadAll<string>("profile").FirstOrDefault() ?? "";
    public void SaveAvatar(string data) { ProfileAvatar.Validate(data); Save("profile", "avatar", Codec.Encode(data)); }
}

public sealed partial class PeerNode
{
    public async Task UpdateAvatarAsync(string data)
    {
        Store.SaveAvatar(data); Changed?.Invoke();
        foreach (var peer in Peers.Where(p => p.Trusted && !p.Blocked))
            await SendTo(peer.Id, new WireMessage { Type = "profile", Name = Identity.Name, Data = data });
    }
}

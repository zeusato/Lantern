using System.Buffers.Binary;
using System.IO.Compression;
using Lantern.Core;

internal static class GroupAvatarFixture
{
    // An actual high-entropy PNG exercises large photos through signed group actions.
    internal static readonly string Image = Create();
    private static string Create()
    {
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            byte[] length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, data.Length); png.Write(length);
            var bytes = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); png.Write(bytes);
            uint crc = uint.MaxValue;
            foreach (var value in bytes) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u); }
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); png.Write(length);
        }
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, ProfileAvatar.Size); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), ProfileAvatar.Size); header[8] = 8; header[9] = 6; Chunk("IHDR", header);
        using var pixels = new MemoryStream(); var random = new Random(42);
        using (var compressor = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
            for (int y = 0; y < ProfileAvatar.Size; y++) { var row = new byte[ProfileAvatar.Size * 4 + 1]; random.NextBytes(row.AsSpan(1)); row[0] = 0; compressor.Write(row); }
        Chunk("IDAT", pixels.ToArray()); Chunk("IEND", []); return Convert.ToBase64String(png.ToArray());
    }
}

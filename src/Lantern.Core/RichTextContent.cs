using System.Text.Json;

namespace Lantern.Core;

// A small, versioned data format, never HTML, XAML or an executable document.
// Text remains the canonical fallback used by search, notifications and older clients.
public sealed class RichTextContent
{
    public int Version { get; set; } = 1;
    public List<RichParagraph> Paragraphs { get; set; } = [];
    public string PlainText() => string.Join("\n", Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));
    public bool HasFormatting => Paragraphs.Any(p => p.List != "" || p.Indent != 0 || p.Runs.Any(r => r.Bold || r.Italic || r.Underline || r.Strike || r.Size != 14 || r.Color != ""));

    public void Validate(string text)
    {
        if (Version != 1 || Paragraphs is null || Paragraphs.Count is < 1 or > 256 || text.Length > 16000)
            throw new InvalidDataException("Nội dung định dạng vượt giới hạn.");
        var count = 0;
        foreach (var p in Paragraphs)
        {
            if (p is null || p.Runs is null || p.List is not ("" or "bullet" or "number") || p.Indent is < 0 or > 4 || p.Number is < 1 or > 256)
                throw new InvalidDataException("Đoạn văn không hợp lệ.");
            count += p.Runs.Count;
            if (count > 512) throw new InvalidDataException("Tin nhắn có quá nhiều vùng định dạng.");
            foreach (var r in p.Runs)
                if (r is null || r.Text is null || r.Text.Length > 16000 || r.Size is not (12 or 14 or 18 or 24) ||
                    r.Color is null || r.Color.Length != 0 && (r.Color.Length != 7 || r.Color[0] != '#' || !r.Color.AsSpan(1).ToArray().All(Uri.IsHexDigit)))
                    throw new InvalidDataException("Định dạng chữ không hợp lệ.");
        }
        if (PlainText() != text || JsonSerializer.SerializeToUtf8Bytes(this, Codec.Json).Length > 96 * 1024)
            throw new InvalidDataException("Định dạng không khớp nội dung hoặc quá lớn.");
    }
}

public sealed class RichParagraph
{
    public List<RichRun> Runs { get; set; } = [];
    public string List { get; set; } = "";
    public int Indent { get; set; }
    public int Number { get; set; } = 1;
}

public sealed record RichRun
{
    public string Text { get; set; } = "";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public int Size { get; set; } = 14;
    public string Color { get; set; } = "";
}

using System.Text.RegularExpressions;

namespace Lantern.Core;

/// <summary>Offline suggestions from pack metadata and user-assigned names.</summary>
public sealed class StickerSearch
{
    private readonly (StickerPack Pack, StickerItem Sticker, string[] Terms)[] entries;
    private static string Phrase(string text) => Regex.Replace(StickerLibrary.SearchKey(text), @"[^\p{L}\p{N}]+", " ").Trim();
    public StickerSearch(IEnumerable<StickerPack> packs) => entries = packs.SelectMany(p => p.Stickers.Select(s =>
        (p, s, s.Keywords.Append(s.Name.StartsWith("Sticker ", StringComparison.Ordinal) ? "" : s.Name).Select(Phrase).Where(k => k.Length >= 2).Distinct().ToArray()))).ToArray();
    public IReadOnlyList<(StickerPack Pack, StickerItem Sticker)> Suggest(string text, int limit = 20)
    {
        var query = Phrase(text.Length > 256 ? text[^256..] : text);
        if (query.Length < 2) return [];
        return entries.Select(e => (Entry: e, Score: e.Terms.Where(t => query == t || query.EndsWith(" " + t, StringComparison.Ordinal)).Select(t => t.Length).DefaultIfEmpty(0).Max()))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(Math.Clamp(limit, 0, 40)).Select(x => (x.Entry.Pack, x.Entry.Sticker)).ToArray();
    }
}

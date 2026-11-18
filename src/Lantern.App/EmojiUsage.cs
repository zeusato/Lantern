using System.Diagnostics;
using System.IO;

namespace Lantern.App;

internal static class EmojiUsage
{
    internal const int Limit = 10;

    internal static IReadOnlyList<EmojiAsset> Recent => (App.Settings.RecentEmoji ?? [])
        .Where(token => !string.IsNullOrEmpty(token)).Select(EmojiAssets.Find).OfType<EmojiAsset>()
        .DistinctBy(asset => asset.Emoji).Take(Limit).ToArray();

    internal static void Remember(params string[] tokens)
    {
        var recent = Recent.Select(asset => asset.Emoji).ToList();
        foreach (var token in tokens)
        {
            if (EmojiAssets.Find(token) is not { } asset) continue;
            recent.Remove(asset.Emoji); recent.Insert(0, asset.Emoji);
        }
        var updated = recent.Take(Limit).ToList();
        if (updated.SequenceEqual(App.Settings.RecentEmoji ?? [])) return;
        App.Settings.RecentEmoji = updated;
        try { App.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Trace.TraceWarning("Could not save recent emoji: {0}", ex.Message); }
    }
}

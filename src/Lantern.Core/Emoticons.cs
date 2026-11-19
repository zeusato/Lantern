using System.Text.RegularExpressions;

namespace Lantern.Core;

public static class Emoticons
{
    private static readonly Dictionary<string, string> Replacements = LoadReplacements();
    private static Dictionary<string, string> LoadReplacements()
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        [":)"] = "🙂", [":-)"] = "🙂", [":D"] = "😃", [":-D"] = "😃",
        [";)"] = "😉", [";-)"] = "😉", [":("] = "🙁", [":-("] = "🙁",
        [":P"] = "😛", [":-P"] = "😛", [":O"] = "😮", [":-O"] = "😮",
        [":'("] = "😢", [":-|"] = "😐", [":|"] = "😐", ["<3"] = "❤️", ["</3"] = "💔"
        };
        using var stream = typeof(Emoticons).Assembly.GetManifestResourceStream("Lantern.Core.emoji-shortcodes.json")!;
        foreach (var pair in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!) replacements[pair.Key] = pair.Value;
        return replacements;
    }
    // Only standalone tokens; preserve URLs, paths, inline code and fenced code.
    private static readonly string Prefix = @"(?<code>```[\s\S]*?(?:```|$)|`[^`\r\n]*(?:`|$))|(?<!\S)(?<face>" + string.Join("|", Replacements.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + ")";
    private static readonly Regex Complete = new(Prefix + @"(?=$|\s|[!?,.])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
    private static readonly Regex WhileTyping = new(Prefix + @"(?=\s|[!?,.])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));

    public static string Replace(string text, bool includeTrailing = true) => (includeTrailing ? Complete : WhileTyping).Replace(text, match => match.Groups["face"].Success ? Replacements[match.Groups["face"].Value] : match.Value);
    public static IReadOnlyList<(int Start, int Length, string Text)> Changes(string text, bool includeTrailing = true) =>
        (includeTrailing ? Complete : WhileTyping).Matches(text).Cast<Match>().Where(m => m.Groups["face"].Success)
        .Select(m => (m.Index, m.Length, Replacements[m.Groups["face"].Value])).ToArray();
}

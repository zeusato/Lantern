using System.Text.RegularExpressions;

namespace Lantern.Core;

public static class Emoticons
{
    private static readonly Dictionary<string, string> Replacements = new(StringComparer.OrdinalIgnoreCase)
    {
        [":)"] = "🙂", [":-)"] = "🙂", [":D"] = "😃", [":-D"] = "😃",
        [";)"] = "😉", [";-)"] = "😉", [":("] = "🙁", [":-("] = "🙁",
        [":P"] = "😛", [":-P"] = "😛", [":O"] = "😮", [":-O"] = "😮",
        [":'("] = "😢", [":-|"] = "😐", [":|"] = "😐", ["<3"] = "❤️", ["</3"] = "💔"
    };
    // Only standalone tokens; preserve URLs, paths, inline code and fenced code.
    private static readonly string Prefix = @"(?<code>```[\s\S]*?(?:```|$)|`[^`\r\n]*(?:`|$))|(?<!\S)(?<face>" + string.Join("|", Replacements.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + ")";
    private static readonly Regex Complete = new(Prefix + @"(?=$|\s|[!?,.])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
    private static readonly Regex WhileTyping = new(Prefix + @"(?=\s|[!?,.])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));

    public static string Replace(string text, bool includeTrailing = true) => (includeTrailing ? Complete : WhileTyping).Replace(text, match => match.Groups["face"].Success ? Replacements[match.Groups["face"].Value] : match.Value);
}

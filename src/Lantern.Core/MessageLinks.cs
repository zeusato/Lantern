using System.Text.RegularExpressions;

namespace Lantern.Core;

public sealed record MessageLink(int Start, int Length, Uri Uri);

public static class MessageLinks
{
    private static readonly Regex Pattern = new("(?<![\\p{L}\\p{N}_@])(?:https?://|www\\.)[^\\s<>\"'“”‘’\\x00-\\x1f]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    public static IEnumerable<MessageLink> Find(string text)
    {
        foreach (Match match in Pattern.Matches(text))
        {
            var value = match.Value;
            while (value.Length > 0)
            {
                var last = value[^1];
                if (".,;:!?".Contains(last) || last == ')' && value.Count(c => c == ')') > value.Count(c => c == '(') ||
                    last == ']' && value.Count(c => c == ']') > value.Count(c => c == '[') || last == '}' && value.Count(c => c == '}') > value.Count(c => c == '{')) value = value[..^1];
                else break;
            }
            var address = value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + value : value;
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.Host.Length > 0)
                yield return new MessageLink(match.Index, value.Length, uri);
        }
    }
}

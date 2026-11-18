using Lantern.Core;

internal static class MessageLinkTests
{
    public static void Run(Action<bool, string> check)
    {
        var text = "Đọc 👋 (https://example.com/wiki/Test_(one)), rồi www.example.org/a?x=1&y=2#ok.\nhttp://10.10.14.108:8080/test";
        var links = MessageLinks.Find(text).ToArray();
        check(links.Length == 3 && text.Substring(links[0].Start, links[0].Length) == "https://example.com/wiki/Test_(one)", "Chat links preserve balanced URL parentheses and exclude sentence punctuation");
        check(links[1].Uri.AbsoluteUri == "https://www.example.org/a?x=1&y=2#ok" && links[2].Uri.Port == 8080, "Chat links support www, queries, anchors, local addresses and ports");
        check(!MessageLinks.Find("file:///C:/secret.txt javascript:alert(1) mailto:a@example.com example.com").Any(), "Chat does not launch file paths or executable URI schemes");
        check(MessageLinks.Find("HTTPS://EXAMPLE.COM/a\nhttps://example.com/b").Count() == 2, "Chat recognizes case-insensitive links across lines");
    }
}

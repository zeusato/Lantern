using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.Core;

namespace Lantern.App;

internal sealed record EmojiAsset(string Emoji, string Name, string File, string Source, string Shortcode = "", string[]? Aliases = null, string Category = "Cảm xúc");

internal static class EmojiAssets
{
    internal static readonly IReadOnlyList<EmojiAsset> Catalog = LoadCatalog();
    private static readonly Dictionary<string, EmojiAsset> ByToken = IndexCatalog();
    private static readonly Regex InlineTokens = new(@"(?<code>```[\s\S]*?(?:```|$)|`[^`\r\n]*(?:`|$))|(?<!\S)(?<face>" + string.Join('|', ByToken.Keys.Where(key => key.All(c => c <= 127)).OrderByDescending(key => key.Length).Select(Regex.Escape)) + @")(?=$|\s|[!?,.])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
    private static readonly Dictionary<string, BitmapSource> Images = [];
    private static IReadOnlyList<EmojiAsset> LoadCatalog()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Lantern;component/Assets/Emoji/zalo-catalog.json"))!.Stream;
        using var reader = new StreamReader(stream); return Codec.Decode<List<EmojiAsset>>(reader.ReadToEnd());
    }
    private static Dictionary<string, EmojiAsset> IndexCatalog()
    {
        var result = new Dictionary<string, EmojiAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Catalog) foreach (var token in new[] { item.Emoji, item.Shortcode }.Concat(item.Aliases ?? []))
            if (token.Length > 0) result.TryAdd(token.Replace("\uFE0F", ""), item);
        return result;
    }
    internal static EmojiAsset? Find(string emoji) => ByToken.GetValueOrDefault(emoji.Replace("\uFE0F", ""));
    internal static string ForInsertion(string text, int start, int length, string token)
    {
        if (!token.All(c => c <= 127)) return token;
        var before = start > 0 && !char.IsWhiteSpace(text[start - 1]) ? " " : "";
        var end = start + length;
        var after = end < text.Length && !char.IsWhiteSpace(text[end]) && !"!?,.".Contains(text[end]) ? " " : "";
        return before + token + after;
    }
    internal static string[] UsedIn(string text)
    {
        var tokens = InlineTokens.Matches(text).Cast<Match>().ToArray();
        var excluded = MessageLinks.Find(text).Select(link => (Start: link.Start, End: link.Start + link.Length))
            .Concat(tokens.Where(token => token.Groups["code"].Success).Select(token => (Start: token.Index, End: token.Index + token.Length))).ToArray();
        bool IsExcluded(int index) => excluded.Any(range => index >= range.Start && index < range.End);
        var used = tokens.Where(token => token.Groups["face"].Success && !IsExcluded(token.Index))
            .Select(token => (Index: token.Index, Emoji: Find(token.Value)!.Emoji)).ToList();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
            if (!IsExcluded(elements.ElementIndex) && Find(elements.GetTextElement()) is { } asset)
                used.Add((elements.ElementIndex, asset.Emoji));
        return used.OrderBy(item => item.Index).Select(item => item.Emoji).ToArray();
    }
    internal static string Tooltip(EmojiAsset asset) => asset.Name + "\nKý tự chèn: " + (asset.Shortcode.Length > 0 ? asset.Shortcode : asset.Emoji);
    internal static Image Icon(string emoji, double size)
    {
        var asset = Find(emoji) ?? throw new ArgumentException("Unknown packaged emoji.");
        if (!Images.TryGetValue(asset.File, out var bitmap))
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Lantern;component/Assets/Emoji/" + asset.File))!.Stream;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); Images[asset.File] = bitmap = image;
        }
        var icon = new Image { Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform, Tag = emoji, ToolTip = Tooltip(asset) };
        AutomationProperties.SetName(icon, asset.Name); return icon;
    }
    internal static RichTextBox MessageText(string text, Action<Uri>? openLink = null)
    {
        var standalone = Find(text.Trim()) is not null;
        double iconSize = standalone ? 48 : 22;
        var paragraph = new System.Windows.Documents.Paragraph { Margin = new Thickness(0), LineHeight = standalone ? 56 : 23 };
        var measure = new TextBlock { FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), FontSize = 14, TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(0) };
        var buffer = new StringBuilder();
        void Flush() { if (buffer.Length > 0) { var value = buffer.ToString(); paragraph.Inlines.Add(new Run(value)); measure.Inlines.Add(new Run(value)); buffer.Clear(); } }
        void AddIcon(string token)
        {
            Flush(); paragraph.Inlines.Add(new InlineUIContainer(Icon(token, iconSize)) { BaselineAlignment = BaselineAlignment.Center });
            measure.Inlines.Add(new InlineUIContainer(Icon(token, iconSize)) { BaselineAlignment = BaselineAlignment.Center });
        }
        void AppendLiteral(string value)
        {
            var elements = StringInfo.GetTextElementEnumerator(value);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                if (Find(element) is not null)
                {
                    AddIcon(element);
                }
                else buffer.Append(element);
            }
            Flush();
        }
        void AppendText(string value)
        {
            int at = 0;
            foreach (Match token in InlineTokens.Matches(value))
            {
                AppendLiteral(value[at..token.Index]);
                if (token.Groups["face"].Success) AddIcon(token.Value);
                else { buffer.Append(token.Value); Flush(); }
                at = token.Index + token.Length;
            }
            AppendLiteral(value[at..]);
        }
        var offset = 0;
        foreach (var match in openLink is null ? Enumerable.Empty<MessageLink>() : MessageLinks.Find(text))
        {
            AppendText(text[offset..match.Start]);
            var value = text.Substring(match.Start, match.Length);
            var link = new Hyperlink(new Run(value)) { NavigateUri = match.Uri, ToolTip = match.Uri.AbsoluteUri, Cursor = Cursors.Hand };
            link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            link.RequestNavigate += (_, e) => { e.Handled = true; openLink!(match.Uri); };
            paragraph.Inlines.Add(link); measure.Inlines.Add(new Run(value));
            offset = match.Start + match.Length;
        }
        AppendText(text[offset..]);
        var document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), TextAlignment = TextAlignment.Left, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), FontSize = 14 };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");
        var box = new ContentSizedText(document, measure)
        {
            IsReadOnly = true, IsReadOnlyCaretVisible = false, IsDocumentEnabled = true, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0, MinWidth = 0, FontSize = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
        {
            if (!box.Selection.IsEmpty) Clipboard.SetText(SelectionText(box.Selection.Start, box.Selection.End)); e.Handled = true;
        }, (_, e) => { e.CanExecute = !box.Selection.IsEmpty; e.Handled = true; }));
        return box;
    }
    private sealed class ContentSizedText(FlowDocument document, TextBlock measure) : RichTextBox(document)
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // RichTextBox otherwise requests all available width, even for a single word.
            // RichTextBox assigns document insets internally; include them and the caret
            // so a short word or trailing emoji does not wrap at the measured edge.
            var padding = Document.PagePadding;
            var horizontalPadding = (double.IsNaN(padding.Left) ? 0 : padding.Left) + (double.IsNaN(padding.Right) ? 0 : padding.Right);
            var width = Math.Min(availableSize.Width, Math.Ceiling(measure.DesiredSize.Width) + horizontalPadding + 4);
            var measured = base.MeasureOverride(new Size(width, availableSize.Height));
            return new Size(width, measured.Height);
        }
    }
    internal static string SelectionText(TextPointer start, TextPointer end)
    {
        var result = new StringBuilder(); var cursor = start;
        while (cursor.CompareTo(end) < 0)
        {
            var context = cursor.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.Text)
            {
                var text = cursor.GetTextInRun(LogicalDirection.Forward); int length = Math.Min(text.Length, cursor.GetOffsetToPosition(end)); result.Append(text.AsSpan(0, length)); cursor = cursor.GetPositionAtOffset(length)!; continue;
            }
            if (context == TextPointerContext.EmbeddedElement && cursor.GetAdjacentElement(LogicalDirection.Forward) is Image { Tag: string emoji }) result.Append(emoji);
            if (context == TextPointerContext.ElementEnd && cursor.Parent is System.Windows.Documents.Paragraph && cursor.GetNextContextPosition(LogicalDirection.Forward)?.CompareTo(end) < 0) result.AppendLine();
            var next = cursor.GetNextContextPosition(LogicalDirection.Forward); if (next is null) break; cursor = next;
        }
        return result.ToString().TrimEnd('\r', '\n');
    }
}

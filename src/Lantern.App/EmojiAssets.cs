using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.Core;

namespace Lantern.App;

internal sealed record EmojiAsset(string Emoji, string Name, string File, string Source);

internal static class EmojiAssets
{
    internal static readonly IReadOnlyList<EmojiAsset> Catalog = LoadCatalog();
    private static readonly Dictionary<string, BitmapSource> Images = [];
    private static IReadOnlyList<EmojiAsset> LoadCatalog()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Lantern;component/Assets/Emoji/catalog.json"))!.Stream;
        using var reader = new StreamReader(stream); return Codec.Decode<List<EmojiAsset>>(reader.ReadToEnd());
    }
    internal static EmojiAsset? Find(string emoji) => Catalog.FirstOrDefault(item => item.Emoji.Replace("\uFE0F", "") == emoji.Replace("\uFE0F", ""));
    internal static Image Icon(string emoji, double size)
    {
        var asset = Find(emoji) ?? throw new ArgumentException("Unknown packaged emoji.");
        if (!Images.TryGetValue(asset.File, out var bitmap))
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Lantern;component/Assets/Emoji/" + asset.File))!.Stream;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); Images[asset.File] = bitmap = image;
        }
        var icon = new Image { Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform, Tag = emoji, ToolTip = asset.Name };
        AutomationProperties.SetName(icon, asset.Name); return icon;
    }
    internal static RichTextBox MessageText(string text, Action<Uri>? openLink = null)
    {
        var paragraph = new System.Windows.Documents.Paragraph { Margin = new Thickness(0), LineHeight = 23 };
        var measure = new TextBlock { FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), FontSize = 14, TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(0) };
        var buffer = new StringBuilder();
        void Flush() { if (buffer.Length > 0) { var value = buffer.ToString(); paragraph.Inlines.Add(new Run(value)); measure.Inlines.Add(new Run(value)); buffer.Clear(); } }
        void AppendText(string value)
        {
            var elements = StringInfo.GetTextElementEnumerator(value);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                if (Find(element) is not null)
                {
                    Flush(); paragraph.Inlines.Add(new InlineUIContainer(Icon(element, 22)) { BaselineAlignment = BaselineAlignment.Center });
                    measure.Inlines.Add(new InlineUIContainer(Icon(element, 22)) { BaselineAlignment = BaselineAlignment.Center });
                }
                else buffer.Append(element);
            }
            Flush();
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

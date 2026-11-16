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
    internal static RichTextBox MessageText(string text)
    {
        var paragraph = new System.Windows.Documents.Paragraph { Margin = new Thickness(0), LineHeight = 23 };
        var buffer = new StringBuilder();
        void Flush() { if (buffer.Length > 0) { paragraph.Inlines.Add(new Run(buffer.ToString())); buffer.Clear(); } }
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (Find(element) is not null) { Flush(); paragraph.Inlines.Add(new InlineUIContainer(Icon(element, 22)) { BaselineAlignment = BaselineAlignment.Center }); }
            else buffer.Append(element);
        }
        Flush();
        var document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), TextAlignment = TextAlignment.Left, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), FontSize = 14 };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");
        var box = new RichTextBox(document)
        {
            IsReadOnly = true, IsReadOnlyCaretVisible = false, IsDocumentEnabled = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0, MinWidth = 34, FontSize = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
        {
            if (!box.Selection.IsEmpty) Clipboard.SetText(SelectionText(box.Selection.Start, box.Selection.End)); e.Handled = true;
        }, (_, e) => { e.CanExecute = !box.Selection.IsEmpty; e.Handled = true; }));
        return box;
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

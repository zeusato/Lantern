using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Lantern.Core;
using Paragraph = System.Windows.Documents.Paragraph;

namespace Lantern.App;

public sealed class RichComposer : RichTextBox
{
    public RichComposer()
    {
        FontSize = 14;
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(BorderBrushProperty, "LineBrush");
        Document = RichDocuments.Create(Plain(""));
        PreviewTextInput += (_, e) => { if (Text.Length - SelectionLength + e.Text.Length > 16000) e.Handled = true; };
        // Accept clipboard text, never untrusted XAML or embedded document objects.
        DataObject.AddPastingHandler(this, (_, e) =>
        {
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                var value = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? "";
                var available = Math.Max(0, 16000 - Text.Length + SelectionLength);
                if (value.Length > available) value = value[..available];
                e.DataObject = new DataObject(DataFormats.UnicodeText, value); e.FormatToApply = DataFormats.UnicodeText;
            }
            else e.CancelCommand();
        });
    }

    public string Text { get => Export().PlainText(); set => Load(Plain(value)); }
    public string SelectedText { get => Selection.Text; set => Selection.Text = value; }
    public int SelectionStart => Offset(Selection.Start);
    public int SelectionLength => Math.Max(0, Offset(Selection.End) - SelectionStart);
    public int CaretIndex { get => Offset(CaretPosition); set { var point = At(value); Selection.Select(point, point); } }
    public void Clear() => Load(Plain(""));
    public void Select(int start, int length) => Selection.Select(At(start), At(start + length));
    public RichTextContent Export() => RichDocuments.Read(Document);
    public void Load(RichTextContent content)
    {
        Document = RichDocuments.Create(content);
        // Undo must never restore another conversation's draft or a sent message.
        IsUndoEnabled = false; IsUndoEnabled = true;
        CaretPosition = Document.ContentEnd;
    }
    private static RichTextContent Plain(string text) => new() { Paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(s => new RichParagraph { Runs = [new() { Text = s }] }).ToList() };

    private List<TextPointer> Positions()
    {
        var positions = new List<TextPointer>();
        Paragraph? previous = null;
        foreach (var p in RichDocuments.Paragraphs(Document.Blocks))
        {
            if (previous is not null) positions.Add(previous.ContentEnd);
            for (var at = p.ContentStart; at is not null && at.CompareTo(p.ContentEnd) < 0;)
            {
                if (at.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var value = at.GetTextInRun(LogicalDirection.Forward);
                    for (var i = 0; i < value.Length; i++) positions.Add(at.GetPositionAtOffset(i)!);
                    at = at.GetPositionAtOffset(value.Length);
                }
                else
                {
                    if (at.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && at.GetAdjacentElement(LogicalDirection.Forward) is LineBreak) positions.Add(at);
                    at = at.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            previous = p;
        }
        positions.Add(previous?.ContentEnd ?? Document.ContentStart);
        return positions;
    }
    private TextPointer At(int offset) { var positions = Positions(); return positions[Math.Clamp(offset, 0, positions.Count - 1)]; }
    private int Offset(TextPointer point)
    {
        var positions = Positions();
        for (var i = 0; i < positions.Count; i++) if (positions[i].CompareTo(point) >= 0) return i;
        return positions.Count - 1;
    }

    public void ReplaceEmoticons(bool includeTrailing)
    {
        var original = Text;
        var changes = Emoticons.Changes(original, includeTrailing);
        if (changes.Count == 0) return;
        var start = SelectionStart; var end = start + SelectionLength;
        int Map(int value) => value + changes.Where(c => c.Start + c.Length <= value).Sum(c => c.Text.Length - c.Length);
        BeginChange();
        try
        {
            foreach (var change in changes.Reverse()) { Select(change.Start, change.Length); Selection.Text = change.Text; }
            Select(Map(start), Math.Max(0, Map(end) - Map(start)));
        }
        finally { EndChange(); }
    }
}

internal static class RichDocuments
{
    internal static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
    {
        foreach (var block in blocks)
            if (block is Paragraph p) yield return p;
            else if (block is System.Windows.Documents.List list)
                foreach (var item in list.ListItems)
                    foreach (var child in Paragraphs(item.Blocks)) yield return child;
            else if (block is Section section)
                foreach (var child in Paragraphs(section.Blocks)) yield return child;
    }

    internal static RichTextContent Read(FlowDocument document)
    {
        var content = new RichTextContent();
        foreach (var p in Paragraphs(document.Blocks))
        {
            var list = (p.Parent as ListItem)?.Parent as System.Windows.Documents.List;
            var paragraph = new RichParagraph { List = list is null ? "" : list.MarkerStyle == TextMarkerStyle.Decimal ? "number" : "bullet", Indent = Math.Clamp((int)Math.Round(p.Margin.Left / 24), 0, 4) };
            if (list is not null && paragraph.List == "number") paragraph.Number = list.StartIndex + list.ListItems.Cast<ListItem>().TakeWhile(item => item != p.Parent).Count();
            void Add(string text, TextElement element)
            {
                var decorations = new TextDecorationCollection();
                for (var ancestor = element; ancestor is Inline inline; ancestor = inline.Parent as TextElement)
                    if (inline.TextDecorations is { } inherited) decorations.Add(inherited);
                var run = new RichRun { Text = text, Bold = element.FontWeight >= FontWeights.Bold, Italic = element.FontStyle == FontStyles.Italic,
                    Underline = decorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true,
                    Strike = decorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true,
                    Size = element.FontSize >= 21 ? 24 : element.FontSize >= 16 ? 18 : element.FontSize < 13 ? 12 : 14,
                    Color = element.Foreground is SolidColorBrush brush && document.Foreground is SolidColorBrush defaultBrush && brush.Color != defaultBrush.Color ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}" : "" };
                var previous = paragraph.Runs.LastOrDefault();
                if (previous is not null && previous with { Text = text } == run) previous.Text += text;
                else paragraph.Runs.Add(run);
            }
            void ReadInlines(InlineCollection inlines)
            {
                foreach (var inline in inlines)
                    if (inline is Run run) Add(run.Text, run);
                    else if (inline is LineBreak line) Add("\n", line);
                    else if (inline is Span span) ReadInlines(span.Inlines);
            }
            ReadInlines(p.Inlines);
            content.Paragraphs.Add(paragraph);
        }
        if (content.Paragraphs.Count == 0) content.Paragraphs.Add(new());
        return content;
    }

    internal static FlowDocument Create(RichTextContent content, Action<Uri>? openLink = null, bool display = false)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), FontSize = 14, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), TextAlignment = TextAlignment.Left };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");
        System.Windows.Documents.List? currentList = null;
        string previousList = ""; int previousIndent = -1;
        foreach (var source in content.Paragraphs)
        {
            var paragraph = new Paragraph { Margin = new Thickness(source.Indent * 24, 0, 0, 4) };
            var paragraphText = string.Concat(source.Runs.Select(r => r.Text));
            var links = display && openLink is not null ? MessageLinks.Find(paragraphText).ToArray() : [];
            var runOffset = 0;
            foreach (var value in source.Runs)
            {
                var span = new Span { FontWeight = value.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = value.Italic ? FontStyles.Italic : FontStyles.Normal, FontSize = value.Size };
                var decorations = new TextDecorationCollection();
                if (value.Underline) decorations.Add(TextDecorations.Underline);
                if (value.Strike) decorations.Add(TextDecorations.Strikethrough);
                span.TextDecorations = decorations;
                if (value.Color.Length > 0) span.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value.Color));
                if (display)
                {
                    var end = runOffset + value.Text.Length;
                    for (var at = runOffset; at < end;)
                    {
                        var link = links.FirstOrDefault(l => at >= l.Start && at < l.Start + l.Length);
                        var next = link is not null ? Math.Min(end, link.Start + link.Length) : Math.Min(end, links.Where(l => l.Start > at).Select(l => l.Start).DefaultIfEmpty(end).Min());
                        var segment = paragraphText[at..next];
                        if (link is not null)
                        {
                            var hyperlink = new Hyperlink(new Run(segment)) { NavigateUri = link.Uri, ToolTip = link.Uri.AbsoluteUri, Cursor = Cursors.Hand };
                            hyperlink.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                            var uri = link.Uri; hyperlink.RequestNavigate += (_, e) => { e.Handled = true; openLink!(uri); }; span.Inlines.Add(hyperlink);
                        }
                        else
                        {
                            var rendered = EmojiAssets.MessageText(segment, inlineOnly: true);
                            var inlines = ((Paragraph)rendered.Document.Blocks.FirstBlock).Inlines;
                            while (inlines.FirstInline is { } inline) { inlines.Remove(inline); span.Inlines.Add(inline); }
                        }
                        at = next;
                    }
                }
                else span.Inlines.Add(new Run(value.Text));
                paragraph.Inlines.Add(span);
                runOffset += value.Text.Length;
            }
            if (source.List == "") { document.Blocks.Add(paragraph); currentList = null; }
            else
            {
                if (currentList is null || previousList != source.List || previousIndent != source.Indent || source.List == "number" && currentList.StartIndex + currentList.ListItems.Count != source.Number)
                {
                    currentList = new System.Windows.Documents.List { MarkerStyle = source.List == "number" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, StartIndex = source.Number, Margin = new Thickness(0), Padding = new Thickness(22, 0, 0, 0) };
                    document.Blocks.Add(currentList);
                }
                currentList.ListItems.Add(new ListItem(paragraph) { Margin = new Thickness(0) });
            }
            previousList = source.List; previousIndent = source.Indent;
        }
        return document;
    }

    internal static RichTextBox MessageText(ChatMessage message, Action<Uri> openLink)
    {
        if (message.RichText is not { } rich) return EmojiAssets.MessageText(message.Text, openLink);
        try { rich.Validate(message.Text); }
        catch (System.IO.InvalidDataException) { return EmojiAssets.MessageText(message.Text, openLink); }
        var box = new SizedMessage(Create(rich, openLink, display: true), rich) { IsReadOnly = true, IsDocumentEnabled = true, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0, MinWidth = 0, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { if (!box.Selection.IsEmpty) Clipboard.SetText(EmojiAssets.SelectionText(box.Selection.Start, box.Selection.End)); e.Handled = true; }, (_, e) => { e.CanExecute = !box.Selection.IsEmpty; e.Handled = true; }));
        return box;
    }

    private sealed class SizedMessage(FlowDocument document, RichTextContent rich) : RichTextBox(document)
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            double desired = 0;
            foreach (var p in rich.Paragraphs)
            {
                var measure = new TextBlock { FontFamily = Document.FontFamily, FontSize = 14 };
                foreach (var run in p.Runs) measure.Inlines.Add(new Run(run.Text) { FontSize = run.Size, FontWeight = run.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = run.Italic ? FontStyles.Italic : FontStyles.Normal });
                measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                desired = Math.Max(desired, measure.DesiredSize.Width + p.Indent * 24 + (p.List == "" ? 0 : 30));
            }
            // Inline emoji are wider than their Unicode glyphs.
            desired += rich.Paragraphs.Max(p => p.Runs.Sum(r => EmojiAssets.UsedIn(r.Text).Length)) * 22;
            var width = Math.Min(availableSize.Width, Math.Ceiling(desired) + 8);
            var result = base.MeasureOverride(new Size(width, availableSize.Height));
            return new Size(width, result.Height);
        }
    }
}

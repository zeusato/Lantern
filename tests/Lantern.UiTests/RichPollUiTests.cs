using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.App;
using Lantern.Core;
using static Lantern.App.App;

internal sealed partial class UiHarness
{
    private async Task RunRichPollUi(string? output)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = Path.GetFullPath(output ?? Path.Combine(Path.GetTempPath(), "Lantern-rich-ui-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        var workspace = Path.Combine(root, "workspace");
        SetApp("DataDirectory", workspace); SetApp("TestMode", true); SetApp("Settings", new AppSettings { Name = "QA", Onboarded = true, AutoUpdates = false, MessageToasts = false });
        SetApp("Vault", WorkspaceVault.Create(workspace, "synthetic QA password")); ApplyTheme("light");
        MainWindow? window = null;
        try
        {
            var rich = new RichTextContent { Paragraphs = [new() { Runs = [new() { Text = "Kế hoạch nhóm", Bold = true, Size = 24, Color = "#408BE5" }] }, new() { List = "number", Runs = [new() { Text = "Nội dung tiếng Việt", Italic = true, Underline = true, Strike = true }] }, new() { List = "number", Indent = 1, Number = 2, Runs = [new() { Text = "Thảo luận 🙂 https://example.com" }] }] };
            var editor = new RichComposer(); editor.Measure(new Size(500, 240)); editor.Arrange(new Rect(0, 0, 500, 240)); editor.UpdateLayout(); editor.Load(rich);
            Check(Codec.Encode(editor.Export()) == Codec.Encode(rich), "Composer round trips every style, list and indent without losing metadata");
            editor.Select(0, "Kế hoạch".Length); Check(editor.SelectedText == "Kế hoạch", "Selection offsets address Unicode text");
            editor.CaretIndex = editor.Text.Length; Check(editor.SelectionStart == editor.Text.Length, "Caret can reach the final paragraph");
            var inline = new RichTextContent { Paragraphs = [new() { Runs = [new() { Text = "Đậm ", Bold = true }, new() { Text = ":) " }, new() { Text = "nghiêng", Italic = true }] }] };
            editor.Load(inline); editor.CaretIndex = editor.Text.Length; editor.ReplaceEmoticons(false);
            Check(editor.Text == "Đậm 🙂 nghiêng" && editor.Export().Paragraphs[0].Runs.First().Bold && editor.Export().Paragraphs[0].Runs.Last().Italic, "Emoticon replacement preserves adjacent rich formatting");
            editor.Undo(); Check(editor.Text == "Đậm :) nghiêng", "Emoticon conversion is one undoable edit");
            editor.Load(rich); Check(!editor.CanUndo, "Restoring a draft clears undo from the previous chat");
            var rendered = RichDocuments.MessageText(new ChatMessage { Text = rich.PlainText(), RichText = rich }, _ => { });
            rendered.Measure(new Size(340, 2000)); rendered.Arrange(new Rect(0, 0, 340, rendered.DesiredSize.Height));
            Check(RichDocuments.Paragraphs(rendered.Document.Blocks).Count() == 3 && rendered.Document.Blocks.OfType<System.Windows.Documents.List>().Count() == 2, "Chat history renders paragraphs, numbered lists and indent levels");
            Check(EmojiAssets.SelectionText(rendered.Document.ContentStart, rendered.Document.ContentEnd).Replace("\r\n", "\n") == rich.PlainText(), "Copying a rich message preserves its text, emoji and link");
            Check(rendered.Document.Blocks.OfType<System.Windows.Documents.List>().Select(l => l.StartIndex).SequenceEqual(new[] { 1, 2 }), "Numbered lists retain their numbering across indentation changes");
            var styledLink = new RichTextContent { Paragraphs = [new() { Runs = [new() { Text = "https://exa", Bold = true }, new() { Text = "mple.com/path", Italic = true }] }] };
            var linkDocument = RichDocuments.Create(styledLink, _ => { }, display: true);
            var links = RichDocuments.Paragraphs(linkDocument.Blocks).SelectMany(p => p.Inlines.OfType<Span>()).SelectMany(span => span.Inlines.OfType<Hyperlink>()).ToArray();
            Check(links.Length == 2 && links.All(l => l.NavigateUri.AbsoluteUri == "https://example.com/path"), "Links split by text formatting retain their complete target");
            var peerId = new string('a', 64); var secondId = new string('b', 64);
            using (var seed = new LocalStore(workspace, Vault))
            {
                seed.SavePeer(new Peer { Id = peerId, Name = "Minh Anh", Trusted = true, Address = "127.0.0.1" });
                seed.SavePeer(new Peer { Id = secondId, Name = "Hoàng", Trusted = true, Address = "127.0.0.1" });
            }
            window = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
            var store = Field<LocalStore>(window, "store"); var node = Field<PeerNode>(window, "node");
            var group = await node.CreateGroupAsync("Nhóm thiết kế", [peerId, secondId]);
            var richMessage = new ChatMessage { ConversationId = group.Id, GroupId = group.Id, SenderId = peerId, SenderName = "Minh Anh", Text = rich.PlainText(), RichText = rich, Status = "read" }; store.SaveMessage(richMessage);
            var poll = new ChatPoll { Question = "Chọn lịch duyệt thiết kế?", Options = ["Thứ hai · 09:00", "Thứ ba · 14:00", "Thứ tư · 10:00"], Multiple = true, Votes = new() { [peerId] = new() { Revision = 1, Choices = [0, 2] } } };
            var pollMessage = new ChatMessage { ConversationId = group.Id, GroupId = group.Id, SenderId = node.Identity.Id, Outgoing = true, Text = poll.Summary(), Poll = poll, Status = "read" }; store.SaveMessage(pollMessage);
            Call(window, "Refresh");
            var contacts = (ListBox)window.FindName("ContactsList"); Call(window, "ShowContact", group.Id);
            var composer = (RichComposer)window.FindName("Composer"); composer.Load(rich);
            Check(((Button)window.FindName("PollButton")).Visibility == Visibility.Visible, "Group conversations expose the create-poll action");
            Call(window, "ShowContact", peerId);
            Check(composer.Text.Length == 0 && ((Button)window.FindName("PollButton")).Visibility == Visibility.Collapsed, "Direct chats have their own draft and hide group polls");
            Call(window, "ShowContact", group.Id);
            Check(Codec.Encode(composer.Export()) == Codec.Encode(rich), "Switching conversations restores the full rich draft");
            var toolbar = (WrapPanel)window.FindName("FormattingToolbar");
            if (toolbar.Visibility != Visibility.Visible) Call(window, "FormatClick", window, new RoutedEventArgs());
            Check(toolbar.Children.OfType<Button>().Count() == 13, "Advanced formatting exposes all thirteen editing actions");
            var before = composer.Text; Call(window, "FormatClick", window, new RoutedEventArgs());
            Check(toolbar.Visibility == Visibility.Collapsed && composer.Text == before && composer.Export().HasFormatting, "Hiding formatting preserves document content and styles");
            Call(window, "FormatClick", window, new RoutedEventArgs()); Call(window, "ExpandComposerClick", window, new RoutedEventArgs());
            Check(Field<bool>(window, "composerExpanded") && composer.Text == before, "Expanding the composer preserves the same document");
            var shell = (FrameworkElement)window.Content;
            ((Panel)shell).SetResourceReference(Panel.BackgroundProperty, "CanvasBrush");
            void Layout(double width, double height) { shell.Measure(new Size(width, height)); shell.Arrange(new Rect(0, 0, width, height)); shell.UpdateLayout(); }
            Layout(1280, 820); Snapshot(shell, Path.Combine(root, "rich-polls-light.png"));
            ApplyTheme("dark"); typeof(MainWindow).GetField("renderKey", Private)!.SetValue(window, ""); Call(window, "RefreshConversation"); Layout(1280, 820); Snapshot(shell, Path.Combine(root, "rich-polls-dark.png"));
            Call(window, "ExpandComposerClick", window, new RoutedEventArgs());
            window.Width = 780; window.Height = 580; Call(window, "UpdateLayoutWidths"); ((ColumnDefinition)window.FindName("DetailsColumn")).Width = new GridLength(0); ((FrameworkElement)window.FindName("DetailsPanel")).Visibility = Visibility.Collapsed; ((ColumnDefinition)window.FindName("ContactsColumn")).Width = new GridLength(240);
            Layout(780, 550); Snapshot(shell, Path.Combine(root, "rich-polls-compact.png"));
            Check(composer.ActualWidth > 200 && toolbar.ActualHeight >= 30 && ((Button)window.FindName("SendButton")).TranslatePoint(new Point(), shell).Y < 550, "Compact layout keeps the composer, wrapped formatting tools and send button visible");
            await (Task)Call(window, "SendMessage")!;
            Check(composer.Text.Length == 0 && !composer.CanUndo && store.Messages(group.Id).Any(m => m.Outgoing && m.RichText is not null), "Sending stores rich text and clears the draft and undo history");
            var imagePixels = Enumerable.Range(0, 800 * 2).SelectMany(i => new byte[] { 30, 120, 210, (byte)(i % 2 == 0 ? 128 : 255) }).ToArray();
            var copyBitmap = BitmapSource.Create(800, 2, 96, 96, PixelFormats.Bgra32, null, imagePixels, 800 * 4);
            var imagePath = Path.Combine(root, "copy-image.png"); var pngEncoder = new PngBitmapEncoder(); pngEncoder.Frames.Add(BitmapFrame.Create(copyBitmap));
            using (var outputImage = File.Create(imagePath)) pngEncoder.Save(outputImage);
            var imageMessage = new ChatMessage { ConversationId = group.Id, GroupId = group.Id, Kind = "image", FilePath = imagePath, FileName = "copy-image.png", SenderId = peerId, Status = "received" }; store.SaveMessage(imageMessage);
            var imageMenu = (ContextMenu)Call(window, "MessageMenu", imageMessage)!;
            Check(imageMenu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Sao chép ảnh").IsEnabled, "Received images expose Copy Image in their context menu");
            var clipboardData = ClipboardImages.CopyData(ClipboardImages.LoadLocal(imagePath, 0));
            using (var pngData = (Stream)clipboardData.GetData("PNG", false)!)
            {
                var copied = ClipboardImages.ReadPng(pngData);
                Check(copied.PixelWidth == 800 && Pixels(copied).SequenceEqual(imagePixels) && clipboardData.GetDataPresent(DataFormats.Bitmap), "Clipboard offers full-resolution Bitmap plus PNG retaining color and transparency");
            }
            imageMessage.FilePath = Path.Combine(root, "not-downloaded.png");
            var missingMenu = (ContextMenu)Call(window, "MessageMenu", imageMessage)!;
            Check(!missingMenu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Sao chép ảnh").IsEnabled, "Images without a local file cannot copy an empty clipboard payload");
            Vault.SetConversationPassword(group.Id, "synthetic password"); Call(window, "RefreshConversation");
            Check(!composer.IsEnabled && ((Panel)window.FindName("MessagesPanel")).Children.Count == 0, "Locked conversations hide rich messages and poll results");
            Console.WriteLine($"{passed} rich/poll UI checks passed. Artifacts: {root}");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally
        {
            if (window is not null) { Call(window, "WindowClosing", window, new System.ComponentModel.CancelEventArgs()); await Until(() => Field<bool>(window, "closeReady"), "Headless cleanup"); }
            Vault.Dispose(); Shutdown(Environment.ExitCode);
        }
    }
}

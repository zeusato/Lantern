using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lantern.App;
using Lantern.Core;
using static Lantern.App.App;

internal sealed partial class UiHarness
{
    // Render the real controls without showing or activating a native window.
    private async Task RunPinsAndEmoji(string? output)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = Path.GetFullPath(output ?? Path.Combine(Path.GetTempPath(), "Lantern-pin-ui-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        var workspace = Path.Combine(root, "workspace");
        SetApp("DataDirectory", workspace); SetApp("TestMode", true); SetApp("Settings", new AppSettings { Name = "QA", Onboarded = true, AutoUpdates = false, MessageToasts = false });
        SetApp("Vault", WorkspaceVault.Create(workspace, "synthetic QA password")); ApplyTheme("light");
        MainWindow? window = null;
        void Preview(FrameworkElement element, string filename)
        {
            element.UpdateLayout(); var size = new Size(element.ActualWidth, element.ActualHeight);
            var backdrop = new DrawingVisual(); using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle((Brush)Resources["CanvasBrush"], null, new Rect(size));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32); bitmap.Render(backdrop); bitmap.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(root, filename)); encoder.Save(file);
        }
        try
        {
            Check(EmojiAssets.Catalog.Count > 1300 && EmojiAssets.Catalog.Count(asset => asset.Category == "Cảm xúc Zalo") == 88, "Imported catalog includes the complete Zalo expressions and Unicode emoji");
            Check(EmojiAssets.Catalog.All(asset => EmojiAssets.Icon(asset.Emoji, 30).Source is BitmapSource { PixelWidth: > 0, PixelHeight: > 0 }), "Every catalog entry loads its packaged bitmap");
            Check(EmojiAssets.Catalog.All(asset => asset.File.StartsWith("Zalo/")) && PeerNode.ReactionEmoji.All(emoji => EmojiAssets.Find(emoji) is not null), "The old emoji catalog is fully replaced, including every supported reaction");
            Check(EmojiAssets.Find("😂")?.File == EmojiAssets.Find(":')")?.File && EmojiAssets.Find("❤️")?.File == EmojiAssets.Find("/-heart")?.File, "Reaction Unicode and Zalo insertion codes share the matching artwork");
            string inserted = ""; var picker = new EmojiPalette(token => inserted = token);
            picker.Measure(new Size(350, 500)); picker.Arrange(new Rect(0, 0, 350, 500)); picker.UpdateLayout();
            var choices = Visuals<Button>(picker).Where(button => button.Tag is string).ToArray();
            Check(Visuals<WrapPanel>(picker).Single(panel => panel.Name == "RecentEmojiChoices").Children.Count == 0, "A fresh profile has no fabricated recent emoji");
            Check(choices.Length == 88 && choices.All(button => button.ToolTip is string text && text.Contains("Ký tự chèn:") && text.Contains(EmojiAssets.Find((string)button.Tag)!.Shortcode)), "Every picker icon has a tooltip with its insertion code");
            var laugh = choices.Single(button => button.Tag as string == ":))"); laugh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(inserted == ":))", "Choosing a Zalo expression inserts its exact code");
            Check(Visuals<WrapPanel>(picker).Single(panel => panel.Name == "RecentEmojiChoices").Children.OfType<Button>().Single().Tag as string == ":))", "Choosing an emoji immediately adds it to the recent section above the catalog");
            Visuals<TextBox>(picker).Single().Text = "/-heart";
            Check(Visuals<Button>(picker).Count(button => button.Tag as string == "/-heart") == 1, "Emoji search accepts insertion codes");
            var text = "Chào :)) :v /-heart :smile: 😂 — `:v /-heart` https://example.test/:v";
            var rendered = EmojiAssets.MessageText(text, _ => { });
            var paragraph = (Paragraph)rendered.Document.Blocks.FirstBlock!;
            Check(paragraph.Inlines.OfType<InlineUIContainer>().Count() == 5 && paragraph.Inlines.OfType<Hyperlink>().Count() == 1, "Shortcodes render as emoji while code spans and links stay intact");
            Check(EmojiAssets.SelectionText(rendered.Document.ContentStart, rendered.Document.ContentEnd) == text, "Copying emoji-rich text preserves original Unicode, shortcodes and URL");
            Check(EmojiAssets.UsedIn(text).SequenceEqual(new[] { ":))", ":v", "/-heart", EmojiAssets.Find(":smile:")!.Emoji, EmojiAssets.Find("😂")!.Emoji }), "Typed emoji and insertion codes are tracked in reading order without code spans or URLs");
            Check(EmojiAssets.UsedIn("`😎` ```\n/-heart 😂\n``` https://example.test/😎").Length == 0, "Recent usage ignores Unicode emoji inside code and URLs");
            Check(EmojiAssets.ForInsertion("hello world", 5, 1, ":))") == " :)) " && EmojiAssets.ForInsertion("", 0, 0, ":))") == ":))", "Picking a shortcode beside text preserves the boundaries needed to render it as an emoji");
            Settings.RecentEmoji.Clear();
            EmojiUsage.Remember("❤️", ":v", "/-HEART");
            Check(EmojiUsage.Recent.Select(asset => asset.Emoji).SequenceEqual(new[] { "/-heart", ":v" }), "Reusing an emoji moves it first and aliases do not create duplicate recent entries");
            Settings.RecentEmoji.Clear(); var eleven = EmojiAssets.Catalog.Take(11).Select(asset => asset.Emoji).ToArray();
            EmojiUsage.Remember(eleven);
            Check(EmojiUsage.Recent.Select(asset => asset.Emoji).SequenceEqual(eleven.Reverse().Take(10)), "Recent emoji are newest first with a strict ten-item limit");
            SetApp("Settings", Codec.Decode<AppSettings>(File.ReadAllText(Path.Combine(workspace, "settings.json"))));
            var reopenedPicker = new EmojiPalette(token => inserted = token); reopenedPicker.Measure(new Size(350, 580)); reopenedPicker.Arrange(new Rect(0, 0, 350, 580)); reopenedPicker.UpdateLayout();
            var recentButtons = Visuals<WrapPanel>(reopenedPicker).Single(panel => panel.Name == "RecentEmojiChoices").Children.OfType<Button>().ToArray();
            Check(recentButtons.Select(button => (string)button.Tag).SequenceEqual(eleven.Reverse().Take(10)) && recentButtons.All(button => button.ToolTip is string tip && tip.Contains("Ký tự chèn:")), "Reopening the picker after reloading settings retains ten recent emoji and their code tooltips");
            recentButtons[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(EmojiUsage.Recent.Count == 10 && EmojiUsage.Recent[0].Emoji == (string)recentButtons[^1].Tag && inserted == EmojiUsage.Recent[0].Emoji, "Selecting from recent usage inserts the emoji and moves it first without duplication");
            Preview(reopenedPicker, "zalo-emoji-picker.png");
            Image[] InlineImages(string value) => ((Paragraph)EmojiAssets.MessageText(value).Document.Blocks.FirstBlock!).Inlines.OfType<InlineUIContainer>().Select(inline => (Image)inline.Child).ToArray();
            Check(InlineImages("😎").Single().Width == 48 && InlineImages(" 8-) ").Single().Width == 48, "A standalone Unicode emoji or Zalo insertion code renders at 48 pixels");
            Check(InlineImages("sss 😎").Single().Width == 22 && InlineImages("😎 😎").All(icon => icon.Width == 22), "Text mixed with emoji and multiple emoji retain inline sizing");
            var samples = new StackPanel { Width = 220, Margin = new Thickness(14) };
            foreach (var sample in new[] { "8-)", "sss 8-)" }) samples.Children.Add(new Border { Child = EmojiAssets.MessageText(sample), Background = (Brush)Resources["OwnBrush"], Padding = new Thickness(12), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) });
            samples.Measure(new Size(250, 300)); samples.Arrange(new Rect(new Point(), samples.DesiredSize)); samples.UpdateLayout(); Preview(samples, "standalone-emoji.png");
            var peerId = new string('a', 64);
            using (var seed = new LocalStore(workspace, Vault)) seed.SavePeer(new Peer { Id = peerId, Name = "Minh Anh", Trusted = true, Address = "127.0.0.1" });
            window = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
            var store = Field<LocalStore>(window, "store");
            var messages = Enumerable.Range(0, 165).Select(index => new ChatMessage { ConversationId = peerId, SenderId = peerId, Text = "Nội dung kiểm thử " + index, Time = DateTimeOffset.UtcNow.AddMinutes(index - 200), Status = "read" }).ToArray();
            foreach (var message in messages) store.SaveMessage(message);
            typeof(MainWindow).GetField("selectedId", Private)!.SetValue(window, peerId);
            var shell = (FrameworkElement)window.Content;
            void Layout() { shell.Measure(new Size(1280, 760)); shell.Arrange(new Rect(0, 0, 1280, 760)); shell.UpdateLayout(); }
            Call(window, "RefreshConversation"); Layout();
            var panel = (Panel)window.FindName("MessagesPanel"); var bar = (Border)window.FindName("PinnedMessageBar"); var summary = (TextBlock)window.FindName("PinnedMessageSummary");
            Check(bar.Visibility == Visibility.Collapsed && !panel.Children.OfType<FrameworkElement>().Any(row => row.Tag as string == messages[0].Id), "Unpinned chats hide the banner and older messages start outside the loaded page");
            store.PinMessage(messages[0].Id, true); Call(window, "RefreshConversation"); Layout();
            Check(bar.Visibility == Visibility.Visible && summary.Text.Contains(messages[0].Text) && ((Button)window.FindName("PinnedMessagesDropdown")).Visibility == Visibility.Collapsed, "One pin appears below the conversation header without a dropdown");
            var scroll = (ScrollViewer)window.FindName("ChatScroll"); var position = bar.TranslatePoint(new Point(), shell); scroll.ScrollToEnd(); Layout();
            Check(bar.TranslatePoint(new Point(), shell) == position && bar.TranslatePoint(new Point(0, bar.ActualHeight), shell).Y <= scroll.TranslatePoint(new Point(), shell).Y, "Pinned banner stays above the scrolling history");
            ((Button)window.FindName("PinnedMessagePreview")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); Layout();
            Check(panel.Children.OfType<FrameworkElement>().Any(row => row.Tag as string == messages[0].Id) && Field<Border?>(window, "highlightedMessage") is not null && scroll.VerticalOffset < scroll.ScrollableHeight, "Clicking a pin loads, scrolls to and highlights an older message");
            store.PinMessage(messages[80].Id, true); store.PinMessage(messages[160].Id, true); Call(window, "RefreshConversation"); Layout();
            var dropdown = (Button)window.FindName("PinnedMessagesDropdown"); Check(dropdown.Visibility == Visibility.Visible && (string)dropdown.Content == "+2 ghim ⌄", "Three pins show the additional pin count");
            var menu = (ContextMenu)Call(window, "BuildPinnedMessageMenu", dropdown)!;
            Check(menu.Items.OfType<MenuItem>().Select(item => (string)item.Tag).SequenceEqual(new[] { messages[160].Id, messages[80].Id, messages[0].Id }), "Pin dropdown lists exactly three messages in pin order");
            menu.Items.OfType<MenuItem>().Single(item => item.Tag as string == messages[80].Id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Layout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Check(summary.Text.Contains(messages[80].Text), "Choosing another pin updates the banner and navigation target");
            var messageMenu = (ContextMenu)Call(window, "MessageMenu", messages[150])!;
            Check(!messageMenu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Ghim tin nhắn").IsEnabled, "Context menu prevents a fourth pin and explains the limit");
            Preview(shell, "pinned-messages.png");
            Vault.SetConversationPassword(peerId, "synthetic conversation password"); Call(window, "RefreshConversation");
            Check(bar.Visibility == Visibility.Collapsed && summary.Text.Length == 0 && menu.Items.Count == 0, "Locking a chat clears pinned previews and dropdown contents");
            Field<HashSet<string>>(window, "unlockedConversations").Add(peerId); Call(window, "RefreshConversation");
            store.DeleteMessages(peerId, new HashSet<string> { messages[80].Id }); Call(window, "RefreshConversation");
            Check(bar.Visibility == Visibility.Visible && !summary.Text.Contains(messages[80].Text), "Deleting the selected pin falls back to a remaining pin");
            store.PinMessage(messages[0].Id, false); store.PinMessage(messages[160].Id, false); Call(window, "RefreshConversation");
            Check(bar.Visibility == Visibility.Collapsed, "Removing the last pin hides the banner");
            CheckGroupCreation(window, Preview);
            Console.WriteLine($"{passed} chat UI checks passed without opening or activating a desktop window. Artifacts: {root}");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally
        {
            // The never-shown fixture has no asynchronous listener shutdown; invoke
            // cleanup before entering WPF's native Window.Close event instead.
            if (window is not null) { Call(window, "WindowClosing", window, new System.ComponentModel.CancelEventArgs()); await Until(() => Field<bool>(window, "closeReady"), "Headless fixture cleanup"); }
            Vault.Dispose(); Shutdown(Environment.ExitCode);
        }
    }
}

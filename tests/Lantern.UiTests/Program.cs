using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Net;
using System.Net.Http;
using Lantern.App;
using Lantern.Core;
using static Lantern.App.App;

internal sealed class UiHarness : Application
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private int passed;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    private static void SetApp(string name, object value) => typeof(App).GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.SetValue(null, value);
    private void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS UI: " + name); passed++; }
    private static async Task Until(Func<bool> test, string name)
    {
        var end = DateTime.UtcNow.AddSeconds(8);
        while (!test()) { if (DateTime.UtcNow > end) throw new TimeoutException(name); await Task.Delay(60); }
    }
    private static void Snapshot(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T value) yield return value; foreach (var value2 in Visuals<T>(child)) yield return value2; }
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels;
    }
    private static void ActivateFixture(Window window)
    {
        window.Hide(); window.Show();
        // Re-show only the QA host after owned fixture windows close; no user window is touched.
        window.Activate();
        SetFocus(new System.Windows.Interop.WindowInteropHelper(window).Handle);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);
    // Isolated fixture host: production startup/password handling is not altered or shipped with a bypass.
    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = e.Args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "Lantern-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var workspace = Path.Combine(root, "workspace");
        SetApp("DataDirectory", workspace); SetApp("TestMode", true); SetApp("Settings", new AppSettings { Name = "Linh · Kiểm thử", Onboarded = true, Theme = "light", AutoUpdates = false });
        SetApp("Vault", WorkspaceVault.Create(workspace, "synthetic QA password only")); ApplyTheme("light");
        using (var seed = new LocalStore(workspace, Vault)) seed.SavePeer(new Peer { Id = new string('c', 64), Name = "Hội thoại khác", Trusted = true, Address = "127.0.0.1" });
        MainWindow? window = null;
        try
        {
            using var remoteIdentity = new Identity(Path.Combine(root, "remote"), "Minh Anh"); using var remoteStore = new LocalStore(Path.Combine(root, "remote")); await using var remote = new PeerNode(remoteIdentity, remoteStore); await remote.StartAsync(0, false, true);
            window = new MainWindow(); MainWindow = window; window.Show();
            var node = Field<PeerNode>(window, "node"); var store = Field<LocalStore>(window, "store");
            await Until(() => node.Port > 0, "Main window load");
            await node.ConnectAsync("127.0.0.1", remote.Port, remoteIdentity.Id); await Until(() => remote.IsConnected(node.Identity.Id), "UI peer connect"); await node.TrustAsync(remoteIdentity.Id); await remote.TrustAsync(node.Identity.Id);
            window.WindowState = WindowState.Minimized; await Task.Delay(200);
            var first = await remote.SendTextAsync(node.Identity.Id, "Chào Linh 👋 Cùng xem bản thiết kế nhé! https://example.com/design");
            await Until(() => store.UnreadCount() == 1, "Minimized unread"); await Task.Delay(350);
            var tray = Field<object>(window, "tray"); Check(Field<int>(tray, "lastCount") == 1, "Tray updates while main window is minimized");
            Check(window.TaskbarItemInfo.Overlay is not null && window.TaskbarItemInfo.Description.Contains("1 tin"), "Taskbar icon shows the unread badge while minimized");
            var list = (ListBox)window.FindName("ContactsList"); Check(list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id).Unread == 1, "Conversation row shows unread count");
            Vault.SetConversationPassword(remoteIdentity.Id, "synthetic conversation password");
            Check(Vault.VerifyConversation(remoteIdentity.Id, "synthetic conversation password"), "Synthetic conversation credential verifies");
            Field<HashSet<string>>(window, "unlockedConversations").Add(remoteIdentity.Id);
            window.WindowState = WindowState.Normal; window.Activate(); Call(window, "Refresh");
            list.SelectedItem = list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id);
            await Until(() => store.UnreadCount() == 0, "Visible read count");
            await Until(() => window.TaskbarItemInfo.Overlay is null, "Taskbar badge clears"); Check(true, "Reading all messages clears the taskbar badge");
            list.SelectedItem = list.Items.OfType<ContactRow>().Single(r => r.Id == new string('c', 64));
            list.SelectedItem = list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id);
            Check(Field<HashSet<string>>(window, "unlockedConversations").Contains(remoteIdentity.Id) && ((TextBox)window.FindName("Composer")).IsEnabled, "Conversation remains unlocked when switching away and back");
            node.SetPeerAlias(remoteIdentity.Id, "Minh Anh · Thiết kế"); Call(window, "Refresh");
            Check(((TextBlock)window.FindName("ChatTitle")).Text == "Minh Anh · Thiết kế", "Local alias appears in chat header");
            var composer = (TextBox)window.FindName("Composer"); composer.Text = "Chào :) "; Check(composer.Text == "Chào 🙂 ", "Composer replaces completed emoticons");
            Call(window, "BeginReply", store.Message(first.Id)!); composer.Text = "Em nhận rồi :D"; await (Task)Call(window, "SendMessage")!;
            await Until(() => remoteStore.Messages().Any(m => m.ReplyToId == first.Id && m.Text == "Em nhận rồi 😃"), "Quoted emoji delivery"); Check(true, "Reply composer sends quote and expands trailing emoticon");
            await (Task)Call(window, "ToggleReaction", first.Id, "❤️")!; await Until(() => remoteStore.Message(first.Id)!.Reactions.ContainsKey(node.Identity.Id), "UI reaction delivery"); Check(true, "Reaction button updates remote message");
            Call(window, "Refresh");
            MessageReactions LiveReactions() => Visuals<MessageReactions>(((Panel)window.FindName("MessagesPanel")).Children.OfType<FrameworkElement>().Single(view => view.Tag as string == first.Id)).Single();
            var liveDock = LiveReactions(); Visuals<Button>(liveDock).Single(button => button.Name == "ReactionTrigger").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var reactionPalette = Field<Popup>(liveDock, "palette");
            await Task.Delay(60); Snapshot((FrameworkElement)reactionPalette.Child, Path.Combine(root, "reaction-picker.png"));
            Visuals<Button>(reactionPalette.Child).Single(button => button.Content is Image { Tag: "😂" }).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => remoteStore.Message(first.Id)!.Reactions.GetValueOrDefault(node.Identity.Id) == "😂", "New picker delivers reaction"); Check(true, "Bubble-edge reaction picker sends the selected colored emoji");
            Call(window, "Refresh"); Visuals<Button>(LiveReactions()).Single(button => button.Name == "ReactionSummary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => remoteStore.Message(first.Id)!.Reactions.GetValueOrDefault(node.Identity.Id) == "", "Summary removes own reaction"); Check(true, "Clicking the reaction count removes the user's own reaction");
            await (Task)Call(window, "ToggleReaction", first.Id, "❤️")!;
            var pixels = new byte[64 * 48 * 4]; for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 100; pixels[i + 1] = 190; pixels[i + 2] = 35; pixels[i + 3] = 0; }
            var clipboardImage = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
            var normalized = ClipboardImages.NormalizeBitmap(clipboardImage);
            Check(Pixels(normalized).Take(4).SequenceEqual(new byte[] { 100, 190, 35, 255 }), "Zero-alpha clipboard bitmap preserves RGB and becomes visible");
            var premultiplied = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Pbgra32, null, pixels, 64 * 4);
            Check(Pixels(ClipboardImages.NormalizeBitmap(premultiplied)).Take(4).SequenceEqual(new byte[] { 100, 190, 35, 255 }), "Invalid zero-alpha Pbgra32 is repaired before unpremultiplication");
            var legacy = Path.Combine(root, "fixture_Anh-dan-legacy.png"); var originalEncoder = new PngBitmapEncoder(); originalEncoder.Frames.Add(BitmapFrame.Create(clipboardImage)); using (var output = File.Create(legacy)) originalEncoder.Save(output);
            var originalBytes = File.ReadAllBytes(legacy); Check(Pixels(ClipboardImages.LoadLocal(legacy, 0))[3] == 255 && originalBytes.SequenceEqual(File.ReadAllBytes(legacy)), "Legacy clipboard image previews recover without rewriting original file");
            using (var png = new MemoryStream(originalBytes)) Check(Pixels(ClipboardImages.ReadPng(png))[3] == 0, "Explicit PNG clipboard data preserves intended transparency");
            await (Task)Call(window, "SendClipboardImage", normalized)!;
            await Until(() => remoteStore.Messages().Any(m => m.Kind == "image"), "Pasted image offer");
            var image = remoteStore.Messages().Last(m => m.Kind == "image"); await remote.AcceptFileAsync(image.Id); await Until(() => remoteStore.Message(image.Id)?.Status == "received", "Pasted image transfer");
            Check(File.Exists(remoteStore.Message(image.Id)!.FilePath), "Clipboard image follows normal PNG transfer and receipt");
            Check(Pixels(ClipboardImages.LoadLocal(remoteStore.Message(image.Id)!.FilePath, 0)).Take(4).SequenceEqual(new byte[] { 100, 190, 35, 255 }), "Received clipboard PNG retains visible colored pixels");
            Call(window, "Refresh"); Check(((Panel)window.FindName("SharedLinks")).Children.Count == 1 && ((Panel)window.FindName("SharedMedia")).Children.Count == 1, "Info panel separates shared links and media");
            var thumbnail = ((Panel)window.FindName("SharedMedia")).Children.OfType<MediaThumbnail>().Single(); await Until(() => Visuals<Image>(thumbnail).Any(view => view.Source is not null), "Thumbnail loads");
            Check(thumbnail.ActualWidth == thumbnail.ActualHeight && thumbnail.ActualWidth == 78, "Media panel displays square image thumbnails");
            var richText = EmojiAssets.MessageText("Chào 👋, cảm ơn ❤️!"); richText.SelectAll(); Check(EmojiAssets.SelectionText(richText.Selection.Start, richText.Selection.End) == "Chào 👋, cảm ơn ❤️!", "Copying a colored emoji message preserves original Unicode text");
            Check(richText.Document.FontFamily.Source.Contains("Segoe UI") && richText.Document.FontSize == 14, "Colored emoji messages retain the app typography");
            var iconPixels = Pixels((BitmapSource)EmojiAssets.Icon("😀", 30).Source); Check(Enumerable.Range(0, iconPixels.Length / 4).Any(i => iconPixels[i * 4 + 3] > 100 && Math.Abs(iconPixels[i * 4] - iconPixels[i * 4 + 2]) > 40), "Fluent emoji assets contain actual color pixels");
            var fakeHost = new StackPanel(); var hover = new MessageActions(fakeHost, () => { });
            Check(hover.Visibility == Visibility.Collapsed, "Message action toolbar is hidden at rest");
            fakeHost.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent }); Check(hover.Visibility == Visibility.Visible, "Hover reveals quote and reaction controls");
            fakeHost.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent }); Check(hover.Visibility == Visibility.Collapsed, "Leaving the message hides its actions");
            await remote.SetPresenceAsync("idle"); await Task.Delay(350); Check(list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id).Presence == "idle", "Presence indicator receives idle state");
            await remote.SetPresenceAsync("dnd"); await Task.Delay(350); Check(list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id).PresenceColor == "#D94A53", "Do-not-disturb indicator is red");
            var gallery = new StackPanel { Margin = new Thickness(20) };
            FrameworkElement Sample(string text, bool outgoing, bool reacted = false)
            {
                var message = new ChatMessage { ConversationId = remoteIdentity.Id, SenderId = outgoing ? node.Identity.Id : remoteIdentity.Id, Text = text, Outgoing = outgoing, Status = "delivered" };
                if (reacted) message.Reactions[node.Identity.Id] = "❤️";
                var view = (FrameworkElement)Call(window, "MessageBubble", message)!; gallery.Children.Add(view); return view;
            }
            var shortView = Sample("OK", false, true); var ownView = Sample("Em nhận rồi 👍", true, true); var emojiView = Sample("👍", false);
            var multilineView = Sample("Một dòng\nDòng thứ hai dài hơn", true); var longView = Sample(string.Concat(Enumerable.Repeat("Tin dài sẽ tự xuống dòng, không làm rộng cả bong bóng. ", 8)), false);
            var galleryWindow = new Window { Owner = window, Width = 640, Height = 680, Content = new ScrollViewer { Content = gallery, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } }; galleryWindow.Show(); galleryWindow.UpdateLayout();
            Border Bubble(FrameworkElement view) => (Border)((StackPanel)view).Children.OfType<Grid>().Single().Children[0];
            var shortBubble = Bubble(shortView); var ownBubble = Bubble(ownView); var longBubble = Bubble(longView);
            Check(shortBubble.ActualWidth < 90 && ownBubble.ActualWidth > shortBubble.ActualWidth + 30 && Bubble(emojiView).ActualWidth < 80, "Short and emoji-only bubbles fit their content instead of filling the row");
            Check(shortBubble.ActualHeight < 55 && ownBubble.ActualHeight < 55 && Bubble(multilineView).ActualHeight < 80, "Natural-width text and emoji stay on their intended lines without accidental wrapping");
            var incomingLeft = shortBubble.TranslatePoint(new Point(), gallery).X; var outgoingRight = ownBubble.TranslatePoint(new Point(ownBubble.ActualWidth, 0), gallery).X;
            Check(incomingLeft < 1 && Math.Abs(outgoingRight - gallery.ActualWidth) < 1, "Incoming bubbles align left and outgoing bubbles align right");
            Check(longBubble.ActualWidth <= 560 && longBubble.ActualHeight > ownBubble.ActualHeight * 2 && Bubble(multilineView).ActualHeight > ownBubble.ActualHeight, "Long text wraps at the maximum width and explicit newlines remain visible");
            var dock = Visuals<MessageReactions>(shortView).Single(); var dockPosition = dock.TranslatePoint(new Point(), shortBubble);
            Check(dockPosition.Y < shortBubble.ActualHeight && dockPosition.Y + dock.ActualHeight > shortBubble.ActualHeight && dock.Parent is Canvas, "Reaction count and picker sit on the lower bubble edge without a separate row");
            var shortHeight = shortBubble.ActualHeight; var longHeight = longBubble.ActualHeight;
            galleryWindow.Width = 360; galleryWindow.UpdateLayout(); Check(longBubble.ActualWidth <= gallery.ActualWidth - 30 + 1 && longBubble.ActualHeight > longHeight && shortBubble.ActualHeight == shortHeight, "Bubbles reflow without clipping when the window narrows"); galleryWindow.Width = 640; galleryWindow.UpdateLayout();
            Snapshot(galleryWindow, Path.Combine(root, "bubble-layout.png")); galleryWindow.Close(); window.Activate();
            var badgeView = new Image { Source = TaskbarBadge.Create(100), Width = 64, Height = 64 }; badgeView.Measure(new Size(64, 64)); badgeView.Arrange(new Rect(0, 0, 64, 64)); Snapshot(badgeView, Path.Combine(root, "taskbar-badge.png"));
            Snapshot(window, Path.Combine(root, "chat-light.png"));
            var chatPanel = (Panel)window.FindName("MessagesPanel"); var messageView = chatPanel.Children.OfType<FrameworkElement>().First(view => view.Tag as string == first.Id); var heightBefore = messageView.ActualHeight;
            messageView.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent }); window.UpdateLayout(); Check(messageView.ActualHeight == heightBefore, "Hover toolbar overlays the message without adding a row");
            ((ScrollViewer)window.FindName("ChatScroll")).ScrollToHome(); await Task.Delay(80); Snapshot(window, Path.Combine(root, "chat-hover.png"));
            Call(window, "EmojiClick", window, new RoutedEventArgs()); await Task.Delay(100); var popup = Field<Popup>(window, "emojiPopup"); Check(popup.IsOpen, "Emoji picker opens"); Snapshot((FrameworkElement)popup.Child, Path.Combine(root, "emoji-picker.png")); popup.IsOpen = false;
            ApplyTheme("dark"); Call(window, "Refresh"); window.UpdateLayout();
            var visibleDocument = Visuals<RichTextBox>(chatPanel).First().Document;
            Check(((SolidColorBrush)visibleDocument.Foreground).Color == ((SolidColorBrush)Resources["TextBrush"]).Color, "Message document foreground follows dark theme"); Snapshot(window, Path.Combine(root, "chat-dark.png")); ApplyTheme("light"); Call(window, "Refresh");
            ((Button)window.FindName("HeaderLockButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!composer.IsEnabled, "Header lock immediately protects the conversation");
            var infoTabs = (TabControl)window.FindName("InfoTabs"); var detailContent = (FrameworkElement)window.FindName("DetailsContent"); var detailLock = (FrameworkElement)window.FindName("DetailsLocked");
            window.UpdateLayout(); Check(!infoTabs.IsEnabled && detailContent.Visibility == Visibility.Collapsed && detailLock.Visibility == Visibility.Visible && !((Button)window.FindName("ShareContactButton")).IsEnabled && !((Button)window.FindName("ConversationPasswordButton")).IsEnabled, "Locked conversations hide and disable the entire information pane");
            Check(((Panel)window.FindName("SharedMedia")).Children.Count == 0 && ((Panel)window.FindName("SharedFiles")).Children.Count == 0 && ((Panel)window.FindName("SharedLinks")).Children.Count == 0 && ((TextBox)window.FindName("MessageSearch")).Text.Length == 0, "Locking clears shared content and search text immediately");
            Exception? protectionFailure = null;
            _ = Dispatcher.InvokeAsync(() =>
            {
                var details = Windows.OfType<Window>().Single(w => w.Title == "Thông tin hội thoại");
                try { Check(!infoTabs.IsEnabled && !detailContent.IsVisible && detailLock.IsVisible, "Detached compact information pane remains protected"); }
                catch (Exception ex) { protectionFailure = ex; }
                details.Close();
            }, System.Windows.Threading.DispatcherPriority.Background);
            Call(window, "CompactDetails"); if (protectionFailure is not null) throw protectionFailure;
            _ = Dispatcher.InvokeAsync(() =>
            {
                var unlockDialog = Visuals<Border>(window).Single(view => view.Name == "InAppDialog");
                try
                {
                    Check(Windows.Count == 1, "Conversation password opens inside the existing app window");
                    var secret = Visuals<PasswordBox>(unlockDialog).Single(); Check(secret.IsKeyboardFocused, "Conversation unlock automatically focuses the password input");
                    Snapshot(window, Path.Combine(root, "conversation-unlock.png")); secret.Password = "wrong conversation password";
                    Visuals<Button>(unlockDialog).Single(button => button.Content as string == "Mở khóa").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(InAppDialog.IsOpen(window) && !infoTabs.IsEnabled, "Wrong conversation password keeps the in-app prompt and information locked"); secret.Password = "synthetic conversation password";
                    Visuals<Button>(unlockDialog).Single(button => button.Content as string == "Mở khóa").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { protectionFailure = ex; InAppDialog.CancelAll(window); }
            }, System.Windows.Threading.DispatcherPriority.Background);
            bool conversationUnlocked = (bool)Call(window, "TryUnlockConversation")!; if (protectionFailure is not null) throw protectionFailure;
            Check(conversationUnlocked && infoTabs.IsEnabled && detailContent.IsVisible && !detailLock.IsVisible, "Correct conversation password restores access to every information tab");
            ((Button)window.FindName("HeaderLockButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await remote.SendTextAsync(node.Identity.Id, "Tin mới khi đang khóa"); await Until(() => store.UnreadCount() == 1 && Field<int>(tray, "lastCount") == 1, "Locked incoming badge"); Check(true, "Locked conversation keeps incoming message unread");
            Check(!Settings.MessageToasts, "Message notifications remain opt-in");
            Exception? settingsFailure = null;
            _ = Dispatcher.BeginInvoke(() =>
            {
                var dialog = Visuals<Border>(window).Single(view => view.Name == "InAppDialog");
                try
                {
                    var mode = Visuals<ComboBox>(dialog).Single(combo => combo.Items.Count == 2);
                    Check(!mode.IsEnabled && mode.Items.Cast<string>().SequenceEqual(new[] { "Hiện người gửi và nội dung", "Chỉ báo có tin nhắn mới" }), "Settings offer both toast modes while notifications default to off");
                    var enable = Visuals<CheckBox>(dialog).Single(box => box.Content as string == "Hiện thông báo Windows khi có tin nhắn mới"); enable.IsChecked = true; enable.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); mode.SelectedIndex = 1;
                    var closeToTray = Visuals<CheckBox>(dialog).Single(box => box.Content as string == "Đóng cửa sổ xuống khay hệ thống"); Check(closeToTray.IsChecked == false, "Close-to-tray setting defaults to off"); closeToTray.IsChecked = true;
                    Snapshot(dialog, Path.Combine(root, "toast-settings.png"));
                    ApplyTheme("dark"); Snapshot(dialog, Path.Combine(root, "settings-dark.png"));
                    var settingsScroll = Visuals<ScrollViewer>(dialog).First(); settingsScroll.ScrollToBottom(); Snapshot(dialog, Path.Combine(root, "settings-bottom.png"));
                    var originalHeight = window.Height; window.Height = window.MinHeight; window.UpdateLayout(); Snapshot(dialog, Path.Combine(root, "settings-compact.png")); window.Height = originalHeight; ApplyTheme("light");
                    Visuals<Button>(dialog).Single(button => button.Content as string == "Lưu thay đổi").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { settingsFailure = ex; InAppDialog.CancelAll(window); }
            });
            Call(window, "SettingsClick", window, new RoutedEventArgs());
            if (settingsFailure is not null) throw settingsFailure;
            Check(Settings.MessageToasts && Settings.MessageToastMode == "private", "Saving settings applies the private toast mode");
            Check(Settings.CloseToTray && Codec.Decode<AppSettings>(File.ReadAllText(Path.Combine(workspace, "settings.json"))).CloseToTray, "Close-to-tray choice is saved to disk");
            Exception? mobileFailure = null;
            _ = Dispatcher.InvokeAsync(() =>
            {
                var dialog = Visuals<Border>(window).Single(view => view.Name == "InAppDialog");
                try
                {
                    Check(Windows.Count == 1 && Field<MobileServer>(window, "mobile").Port > 0, "Mobile device management is embedded and starts only after PC login");
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var qr = Visuals<Border>(window).Single(view => view.Tag as string == "Ghép đôi điện thoại");
                            Check(Visuals<Image>(qr).Single().Source is BitmapSource { PixelWidth: > 200 }, "Pairing renders a real QR image in the app");
                            Snapshot(window, Path.Combine(root, "mobile-qr.png"));
                            Visuals<Button>(qr).Single(button => button.Content as string == "Đóng mã QR").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch (Exception ex) { mobileFailure = ex; InAppDialog.CancelAll(window); }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                    Visuals<Button>(dialog).Single(button => button.Content as string == "＋ Ghép đôi điện thoại bằng QR").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Visuals<Button>(dialog).Single(button => button.Content as string == "Xong").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { mobileFailure = ex; InAppDialog.CancelAll(window); }
            }, System.Windows.Threading.DispatcherPriority.Background);
            Call(window, "MobileSettings"); if (mobileFailure is not null) throw mobileFailure;
            ApplyTheme("dark"); Call(window, "Refresh"); Snapshot(window, Path.Combine(root, "chat-locked-dark.png"));
            using var previewService = new LinkPreviews(new PreviewFixture(File.ReadAllBytes(remoteStore.Message(image.Id)!.FilePath)));
            var previewCard = new LinkPreviewCard(new Uri("https://example.test/design"), previewService, () => { });
            var previewWindow = new Window { Owner = window, Content = previewCard, Width = 290, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner }; previewWindow.Show();
            await Until(() => Visuals<Image>(previewCard).Any(view => view.Source is not null), "OG image loads");
            Check(Visuals<TextBlock>(previewCard).Any(view => view.Text == "Thiết kế & màu sắc"), "Link cards show Open Graph image and decoded title"); Snapshot(previewCard, Path.Combine(root, "link-preview.png")); previewWindow.Close();
            if (e.Args.Length > 1) { var actualBytes = File.ReadAllBytes(e.Args[1]); Check(Pixels(ClipboardImages.LoadLocal(e.Args[1], 0))[3] == 255 && actualBytes.SequenceEqual(File.ReadAllBytes(e.Args[1])), "Reported clipboard image recovers without changing the user's original file"); }
            if (e.Args.Length > 2) { var videoThumb = await Task.Run(() => ShellThumbnail.Load(e.Args[2])); Check(videoThumb is { PixelWidth: > 0, PixelHeight: > 0 }, "Windows creates a real MP4 video thumbnail"); }
            Settings.MessageToasts = false;
            Field<HashSet<string>>(window, "unlockedConversations").Add(remoteIdentity.Id); ActivateFixture(window); await Task.Delay(100); Call(window, "Refresh");
            await Until(() => store.UnreadCount() == 0, "Read before hiding");
            var autoImage = await remote.OfferFileAsync(node.Identity.Id, remoteStore.Message(image.Id)!.FilePath);
            await Until(() => store.Message(autoImage.Id)?.Status == "received", "Incoming image auto-download");
            await Until(() => ((Panel)window.FindName("MessagesPanel")).Children.OfType<FrameworkElement>().Where(view => view.Tag as string == autoImage.Id).Any(view => Visuals<Image>(view).Any(picture => picture.Source is BitmapSource)), "Incoming image displays"); Check(true, "Received images automatically download and render in the chat without accepting");
            await Until(() => store.UnreadCount() == 0, "Visible auto image read");
            window.Close(); Check(!window.IsVisible && !Field<bool>(window, "closing") && node.IsConnected(remoteIdentity.Id), "Close button hides to tray while keeping the peer connected");
            await remote.SendTextAsync(node.Identity.Id, "Tin nhận khi chạy dưới tray"); await Until(() => store.UnreadCount() == 1 && Field<int>(tray, "lastCount") == 1, "Hidden receive"); Check(true, "Hidden app receives messages and updates unread tray badge");
            Call(window, "RestoreFromTray"); await Until(() => window.IsVisible && store.UnreadCount() == 0, "Tray restore"); Check(composer.IsEnabled, "Opening from tray restores the same unlocked session");
            Call(window, "LockApp"); await Task.Delay(100); Check(Windows.Count == 1 && Visuals<LoginView>(window).Count() == 1, "Locking the app shows an embedded login with only one taskbar window"); window.Close();
            Check(!window.IsVisible && Field<bool>(window, "appLocking") && node.IsConnected(remoteIdentity.Id), "Closing the unlock prompt hides the app and keeps it locked");
            ActivateFixture(window); Call(window, "RestoreFromTray"); await Task.Delay(100);
            var login = Visuals<LoginView>(window).Single();
            Check(login.IsVisible && ((FrameworkElement)window.FindName("Shell")).Visibility == Visibility.Collapsed, "Restoring a locked tray session requires the password again");
            var secret = Visuals<PasswordBox>(login).Single(); Check(secret.IsKeyboardFocused, "App unlock automatically focuses the password input");
            Check(Visuals<TextBlock>(login).Any(block => block.Text == $"Chào mừng\n{Settings.Name} trở lại."), "Login greeting shows the account display name"); Snapshot(window, Path.Combine(root, "login-greeting.png")); secret.Password = "wrong workspace password";
            Visuals<Button>(login).Single(button => button.Content as string == "Mở khóa").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Visuals<TextBlock>(login).Any(block => block.Foreground == Brushes.IndianRed && block.Text.Length > 0), "Wrong password rejected");
            Check(Field<bool>(window, "appLocking") && ((FrameworkElement)window.FindName("Shell")).Visibility == Visibility.Collapsed, "Wrong app password cannot reveal chat");
            await Until(() => Visuals<Button>(login).Single(button => button.Content as string == "Mở khóa").IsEnabled, "Password retry available"); secret.Password = "synthetic QA password only";
            Visuals<Button>(login).Single(button => button.Content as string == "Mở khóa").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !Field<bool>(window, "appLocking"), "App password accepted");
            Check(window.IsVisible && !Field<bool>(window, "appLocking"), "Real password verification unlocks the restored session");
            window.Close();
            var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult();
            Field<System.Windows.Forms.ContextMenuStrip>(tray, "menu").Items.Cast<System.Windows.Forms.ToolStripItem>().Single(item => item.Text == "Thoát Lantern").PerformClick(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await Until(() => !remote.IsConnected(node.Identity.Id), "Exit disconnects peer"); Check(Field<bool>(tray, "disposed"), "Tray Exit really closes the hidden window, disconnects and removes the icon"); window = null;
            Settings.CloseToTray = false; Vault.Dispose(); window = new MainWindow(requireLogin: true); MainWindow = window; window.Show(); ActivateFixture(window); await Task.Delay(120);
            Check(Field<PeerNode>(window, "node") is null && Windows.Count == 1 && Visuals<LoginView>(window).Single().IsVisible, "Cold startup stays in one window and does not start networking before authentication");
            var startupHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var startupLogin = Visuals<LoginView>(window).Single(); var startupPassword = Visuals<PasswordBox>(startupLogin).Single(); Check(startupPassword.IsKeyboardFocused, "Cold startup automatically focuses the password field");
            startupPassword.Password = "synthetic QA password only"; Visuals<Button>(startupLogin).Single(button => button.Content as string == "Mở khóa").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Field<PeerNode>(window, "node") is { Port: > 0 }, "Second startup"); Check(new System.Windows.Interop.WindowInteropHelper(window).Handle == startupHandle && Windows.Count == 1 && !Visuals<LoginView>(window).Any(), "Successful startup login reuses the same native app window");
            var closedDefault = new TaskCompletionSource(); window.Closed += (_, _) => closedDefault.TrySetResult(); window.Close(); await closedDefault.Task.WaitAsync(TimeSpan.FromSeconds(8)); Check(true, "Close button exits normally when close-to-tray is disabled"); window = null;
            Vault.Dispose(); SetApp("DataDirectory", Path.Combine(root, "new-workspace")); SetApp("Settings", new AppSettings { Name = "Workspace mới", AutoUpdates = false });
            window = new MainWindow(requireLogin: true); MainWindow = window; window.Show(); ActivateFixture(window); await Task.Delay(120);
            var createLogin = Visuals<LoginView>(window).Single(); foreach (var input in Visuals<PasswordBox>(createLogin)) input.Password = "synthetic new workspace password";
            Visuals<Button>(createLogin).Single(button => button.Content as string == "Tạo workspace").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Field<PeerNode>(window, "node") is { Port: > 0 }, "New workspace session");
            Check(WorkspaceVault.Exists(DataDirectory) && Settings.Onboarded && Windows.Count == 1, "Creating a workspace authenticates and starts chat in the same app window");
            var createdClosed = new TaskCompletionSource(); window.Closed += (_, _) => createdClosed.TrySetResult(); window.Close(); await createdClosed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            window = new MainWindow(requireLogin: true); MainWindow = window; window.Show(); await Task.Delay(100); window.Close();
            Check(!window.IsVisible && Field<PeerNode>(window, "node") is null, "Closing the initial login exits without opening the history or network listener"); window = null;
            Console.WriteLine($"{passed} UI checks passed. Screenshots: {root}");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; if (window is not null) Call(window, "ExitApplication"); await Task.Delay(700); }
        Vault.Dispose(); Shutdown(Environment.ExitCode);
    }
    private sealed class PreviewFixture(byte[] image) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            bool isImage = request.RequestUri!.AbsolutePath == "/cover.png";
            HttpContent content = isImage ? new ByteArrayContent(image) : new StringContent("<meta property='og:title' content='Thiết kế &amp; màu sắc'><meta property='og:image' content='/cover.png'>");
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(isImage ? "image/png" : "text/html"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new UiHarness();
        var source = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xml"));
        var dictionary = source.Descendants().Single(element => element.Name.LocalName == "Application.Resources").Elements().Single();
        dictionary.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
        app.Run(); return Environment.ExitCode;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lantern.App;
using Lantern.Core;
using static Lantern.App.App;

internal sealed partial class UiHarness
{
    private async Task RunStickerUi(string? output)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = Path.GetFullPath(output ?? Path.Combine(Path.GetTempPath(), "Lantern-sticker-ui-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        SetApp("DataDirectory", Path.Combine(root, "workspace")); SetApp("TestMode", true); SetApp("Settings", new AppSettings { Name = "QA", Onboarded = true, AutoUpdates = false, MessageToasts = false });
        SetApp("Vault", WorkspaceVault.Create(DataDirectory, "synthetic QA password")); ApplyTheme("light"); MainWindow? window = null;
        void Preview(FrameworkElement element, double width, double height, string filename)
        {
            var surface = new Border { Child = element, Background = (Brush)Resources["SurfaceBrush"], Padding = new Thickness(16) };
            surface.Measure(new Size(width, height)); surface.Arrange(new Rect(0, 0, width, height)); surface.UpdateLayout();
            // Exercise Loaded handlers in the same dispatcher without showing a native window.
            foreach (var image in Visuals<StickerImage>(surface)) { image.Source = ClipboardImages.LoadLocal(Field<string>(image, "path"), 512); }
            Snapshot(surface, Path.Combine(root, filename)); surface.Child = null;
        }
        try
        {
            var pixels = Enumerable.Repeat((byte)255, 64 * 64 * 4).ToArray();
            for (int y = 16; y < 48; y++) for (int x = 16; x < 48; x++) { int p = (y * 64 + x) * 4; pixels[p] = 40; pixels[p + 1] = 60; pixels[p + 2] = 220; }
            var source = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
            var cut = StickerImaging.RemoveEdgeBackground(source, 25); var cutPixels = Pixels(cut);
            Check(cutPixels[3] == 0 && cutPixels[(32 * 64 + 32) * 4 + 3] == 255, "Background removal clears connected background while retaining the subject");
            var selection = StickerImaging.Select(source, [new Point(16, 16), new Point(48, 16), new Point(32, 48)]);
            Check(Pixels(selection)[3] == 0 && Pixels(selection)[(24 * 64 + 32) * 4 + 3] == 255, "Freeform selection produces a transparent cutout");
            var outlined = StickerImaging.OutlineAndTrim(cut, 6); var outlinedPixels = Pixels(outlined);
            Check(outlined.PixelWidth == 46 && outlined.PixelHeight == 46 && outlinedPixels[3] == 0 && outlinedPixels[(7 * 46 + 4) * 4 + 2] == 255, "White outline adds padding without clipping and retains transparent corners");
            using (var png = new MemoryStream(StickerImaging.Png(outlined))) { var decoded = BitmapFrame.Create(png); Check(decoded.PixelWidth == 46, "Crafted sticker exports as a decodable PNG"); }
            var peer = new string('a', 64); using (var store = new LocalStore(DataDirectory, Vault)) store.SavePeer(new Peer { Id = peer, Name = "Sticker QA", Trusted = true });
            window = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
            await (Task)Call(window, "PrepareStickerLibraryAsync")!;
            var library = (StickerLibrary)typeof(MainWindow).GetProperty("Stickers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            var packs = library.Packs(); Check(packs.Count == 260 && packs.Sum(p => p.Stickers.Count) == 1993, "All bundled Zalo packs install from embedded resources without external asset directories");
            int checkedImages = 0, animated = 0;
            foreach (var pack in packs) foreach (var item in pack.Stickers)
            {
                var bytes = StickerLibrary.ReadImage(library.PathFor(pack, item));
                if (item.File.EndsWith(".gif")) { var frames = StickerImage.DecodeGif(bytes); CheckFrames(frames); animated++; }
                else { using var png = new MemoryStream(bytes); _ = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); }
                checkedImages++;
            }
            Check(checkedImages == 1993 && animated == 1404, "Every bundled image decodes and all 1404 animations satisfy playback bounds");
            void CheckFrames(List<(BitmapSource Bitmap, int Delay)> frames) { if (frames.Count < 2 || frames.Any(f => f.Delay < 20 || f.Bitmap.PixelWidth < 1)) throw new Exception("Invalid animation"); }
            var own = library.Create("Bộ kiểm thử"); var ownItem = library.Add(own, "Bông hoa kiểm thử", StickerImaging.Png(outlined));
            var picker = new StickerPalette(library, (_, _) => { }, () => { });
            Preview(picker, 382, 440, "sticker-picker-ready.png");
            Check(Visuals<StickerImage>(picker).Any() && !Visuals<ComboBox>(picker).Any(), "Picker immediately shows usable stickers and thumbnail pack navigation without a dropdown");
            Visuals<TextBox>(picker).Single().Text = "bong hoa kiem thu"; Preview(picker, 382, 460, "sticker-picker-search.png");
            Check(Visuals<StickerImage>(picker).Count() == 1, "Picker finds named stickers with accent-insensitive search across packs");
            var workshop = new StickerWorkshop(window, library, null); Field<ComboBox>(workshop, "packs").SelectedItem = Field<ComboBox>(workshop, "packs").Items.Cast<StickerPack>().First(p => p.Id == "zalo-0"); Preview(workshop, 728, 720, "sticker-workshop.png");
            var editor = new StickerEditor(source); Preview(editor, 740, 680, "sticker-editor.png");
            ApplyTheme("dark"); Preview(new StickerWorkshop(window, library, null), 728, 650, "sticker-workshop-dark.png"); ApplyTheme("light");
            var itemWithAnimation = packs.SelectMany(p => p.Stickers.Select(s => (Pack: p, Sticker: s))).First(x => x.Sticker.File.EndsWith(".gif"));
            var message = new ChatMessage { ConversationId = peer, SenderId = peer, Kind = "image", FilePath = library.PathFor(itemWithAnimation.Pack, itemWithAnimation.Sticker), FileName = "sticker.gif", Status = "received", Sticker = new StickerReference(itemWithAnimation.Pack.Id, itemWithAnimation.Pack.Name, itemWithAnimation.Sticker.Id, itemWithAnimation.Sticker.Name) };
            var bubble = (FrameworkElement)Call(window, "MessageBubble", message, false, false)!; Preview(bubble, 280, 250, "sticker-chat.png");
            Check(Visuals<StickerImage>(bubble).Count() == 1 && Visuals<Border>(bubble).First(b => b.Name == "MessageSurface").Background == Brushes.Transparent, "Chat renders stickers without file cards or a colored message background");
            Check(!Visuals<Button>(bubble).Any(b => b.Content as string == "Lưu sticker"), "Sticker bubbles have no inline save button");
            var menu = (ContextMenu)Call(window, "MessageMenu", message)!;
            Check(menu.Items.OfType<MenuItem>().Any(m => m.Header as string == "Lưu sticker" && m.IsEnabled), "Save sticker is available in the context menu");
            Check(Visuals<Border>(bubble).Count(b => b.Name == "MessageAvatar") == 1, "Incoming sticker starts with an avatar");
            Check(!Visuals<Border>((FrameworkElement)Call(window, "MessageBubble", message, true, false)!).Any(b => b.Name == "MessageAvatar"), "Consecutive incoming bubbles reserve space without repeating the avatar");
            message.Outgoing = true; Check(!Visuals<Border>((FrameworkElement)Call(window, "MessageBubble", message, false, false)!).Any(b => b.Name == "MessageAvatar"), "Own messages have no avatar"); message.Outgoing = false;
            var matches = new StickerSearch(packs).Suggest("Chúc ngủ ngon"); Check(matches.Count >= 3 && matches.All(x => x.Sticker.Keywords.Contains("ngủ ngon")), "Typing a Vietnamese phrase suggests relevant bundled stickers");
            var animatedImage = new StickerImage(message.FilePath, 130); var host = new Grid(); host.Children.Add(animatedImage); host.Measure(new Size(150, 150)); host.Arrange(new Rect(0, 0, 150, 150));
            // A loaded/unloaded route must release the animation timer and cached frames.
            animatedImage.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); animatedImage.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Check(!Field<DispatcherTimer>(animatedImage, "timer").IsEnabled, "Unloading a sticker stops its animation timer");
            Call(window, "ShowContact", peer);
            var composer = (RichComposer)window.FindName("Composer"); composer.Text = "chúc ngủ ngon";
            await Until(() => ((Panel)window.FindName("StickerSuggestions")).Children.Count >= 3, "Composer sticker suggestions");
            Check(((ScrollViewer)window.FindName("StickerSuggestionBar")).Visibility == Visibility.Visible, "Typing a phrase opens the inline sticker row");
            composer.Text = "nội dung không có gợi ý"; Check(((ScrollViewer)window.FindName("StickerSuggestionBar")).Visibility == Visibility.Collapsed, "Changing the draft immediately removes stale suggestions");
            Vault.SetConversationPassword(peer, "synthetic password"); Call(window, "RefreshConversation");
            Check(!((Button)window.FindName("StickerButton")).IsEnabled, "Locked conversations disable sending stickers");
            Console.WriteLine($"{passed} sticker UI checks passed. Artifacts: {root}");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally
        {
            if (window is not null) { Call(window, "WindowClosing", window, new System.ComponentModel.CancelEventArgs()); await Until(() => Field<bool>(window, "closeReady"), "Headless cleanup"); }
            Vault.Dispose(); Shutdown(Environment.ExitCode);
        }
    }
}

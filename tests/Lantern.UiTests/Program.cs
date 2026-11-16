using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            var list = (ListBox)window.FindName("ContactsList"); Check(list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id).Unread == 1, "Conversation row shows unread count");
            Vault.SetConversationPassword(remoteIdentity.Id, "synthetic conversation password");
            Check(Vault.VerifyConversation(remoteIdentity.Id, "synthetic conversation password"), "Synthetic conversation credential verifies");
            Field<HashSet<string>>(window, "unlockedConversations").Add(remoteIdentity.Id);
            window.WindowState = WindowState.Normal; window.Activate(); Call(window, "Refresh");
            list.SelectedItem = list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id);
            await Until(() => store.UnreadCount() == 0, "Visible read count");
            list.SelectedItem = list.Items.OfType<ContactRow>().Single(r => r.Id == new string('c', 64));
            list.SelectedItem = list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id);
            Check(Field<HashSet<string>>(window, "unlockedConversations").Contains(remoteIdentity.Id) && ((TextBox)window.FindName("Composer")).IsEnabled, "Conversation remains unlocked when switching away and back");
            node.SetPeerAlias(remoteIdentity.Id, "Minh Anh · Thiết kế"); Call(window, "Refresh");
            Check(((TextBlock)window.FindName("ChatTitle")).Text == "Minh Anh · Thiết kế", "Local alias appears in chat header");
            var composer = (TextBox)window.FindName("Composer"); composer.Text = "Chào :) "; Check(composer.Text == "Chào 🙂 ", "Composer replaces completed emoticons");
            Call(window, "BeginReply", store.Message(first.Id)!); composer.Text = "Em nhận rồi :D"; await (Task)Call(window, "SendMessage")!;
            await Until(() => remoteStore.Messages().Any(m => m.ReplyToId == first.Id && m.Text == "Em nhận rồi 😃"), "Quoted emoji delivery"); Check(true, "Reply composer sends quote and expands trailing emoticon");
            await (Task)Call(window, "ToggleReaction", first.Id, "❤️")!; await Until(() => remoteStore.Message(first.Id)!.Reactions.ContainsKey(node.Identity.Id), "UI reaction delivery"); Check(true, "Reaction button updates remote message");
            var pixels = new byte[64 * 48 * 4]; for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 100; pixels[i + 1] = 190; pixels[i + 2] = 35; pixels[i + 3] = 255; }
            var clipboardImage = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
            await (Task)Call(window, "SendClipboardImage", clipboardImage)!;
            await Until(() => remoteStore.Messages().Any(m => m.Kind == "image"), "Pasted image offer");
            var image = remoteStore.Messages().Last(m => m.Kind == "image"); await remote.AcceptFileAsync(image.Id); await Until(() => remoteStore.Message(image.Id)?.Status == "received", "Pasted image transfer");
            Check(File.Exists(remoteStore.Message(image.Id)!.FilePath), "Clipboard image follows normal PNG transfer and receipt");
            Call(window, "Refresh"); Check(((StackPanel)window.FindName("SharedLinks")).Children.Count == 1 && ((StackPanel)window.FindName("SharedMedia")).Children.Count == 1, "Info panel separates shared links and media");
            await remote.SetPresenceAsync("idle"); await Task.Delay(350); Check(list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id).Presence == "idle", "Presence indicator receives idle state");
            await remote.SetPresenceAsync("dnd"); await Task.Delay(350); Check(list.Items.OfType<ContactRow>().Single(r => r.Id == remoteIdentity.Id).PresenceColor == "#D94A53", "Do-not-disturb indicator is red");
            Snapshot(window, Path.Combine(root, "chat-light.png"));
            Call(window, "EmojiClick", window, new RoutedEventArgs()); await Task.Delay(100); var popup = Field<Popup>(window, "emojiPopup"); Check(popup.IsOpen, "Emoji picker opens"); Snapshot((FrameworkElement)popup.Child, Path.Combine(root, "emoji-picker.png")); popup.IsOpen = false;
            ((Button)window.FindName("HeaderLockButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!composer.IsEnabled, "Header lock immediately protects the conversation");
            await remote.SendTextAsync(node.Identity.Id, "Tin mới khi đang khóa"); await Task.Delay(500); Check(store.UnreadCount() == 1 && Field<int>(tray, "lastCount") == 1, "Locked conversation keeps incoming message unread");
            Check(!Settings.MessageToasts, "Message notifications remain opt-in");
            ApplyTheme("dark"); Call(window, "Refresh"); Snapshot(window, Path.Combine(root, "chat-locked-dark.png"));
            Console.WriteLine($"{passed} UI checks passed. Screenshots: {root}");
            var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult(); window.Close(); await closed.Task; window = null;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; window?.Close(); await Task.Delay(700); }
        Vault.Dispose(); Shutdown(Environment.ExitCode);
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

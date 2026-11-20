using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lantern.App;
using Lantern.Core;
using static Lantern.App.App;

internal sealed partial class UiHarness
{
    private async Task RunContactDirectoryUi(string? output)
    {
        using var watchdog = new System.Threading.Timer(_ => { Console.Error.WriteLine("Contact directory fixture timed out."); Environment.Exit(1); }, null, TimeSpan.FromMinutes(2), System.Threading.Timeout.InfiniteTimeSpan);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = Path.GetFullPath(output ?? Path.Combine(Path.GetTempPath(), "Lantern-contacts-ui-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        var workspace = Path.Combine(root, "workspace");
        SetApp("DataDirectory", workspace); SetApp("TestMode", true); SetApp("Settings", new AppSettings { Name = "Linh", Onboarded = true, Theme = "light", AutoUpdates = false, MessageToasts = false });
        SetApp("Vault", WorkspaceVault.Create(workspace, "synthetic QA password only")); ApplyTheme("light");
        var friend = new string('c', 64); var lan = new string('d', 64); var blocked = new string('e', 64);
        using (var seed = new LocalStore(workspace, Vault))
        {
            seed.SavePeer(new Peer { Id = friend, Name = "Bạn chưa trò chuyện", Trusted = true, Address = "10.10.14.20", Port = 45853 });
            seed.SavePeer(new Peer { Id = lan, Name = "Máy phòng thiết kế", Address = "10.10.15.30", Port = 45853 });
            seed.SavePeer(new Peer { Id = blocked, Name = "Máy đã chặn", Blocked = true, Address = "10.10.15.31" });
        }
        MainWindow? window = null;
        try
        {
            using var remoteId = new Identity(Path.Combine(root, "remote"), "Minh Anh"); using var remoteStore = new LocalStore(Path.Combine(root, "remote")); await using var remote = new PeerNode(remoteId, remoteStore); await remote.StartAsync(0, false, true);
            window = new MainWindow { Width = 1280, Height = 820 }; MainWindow = window; window.Show();
            var node = Field<PeerNode>(window, "node"); var store = Field<LocalStore>(window, "store"); await Until(() => node.Port > 0, "Window started");
            var list = (ListBox)window.FindName("ContactsList");
            ContactRow[] Rows() => list.Items.OfType<ContactRow>().ToArray();
            void Section(string value) => Call(window, "SetContactSection", value);
            Check(Rows().Length == 0, "Known LAN peers and friends without history never populate Chats");
            Section("friends"); Check(Rows().Single().Id == friend, "Friends contains trusted peers without chat history");
            Call(window, "ShowContact", friend); Check(((RichComposer)window.FindName("Composer")).IsEnabled, "A friend without history can open an empty composer");
            Section("chats"); Check(Rows().Length == 0 && store.ConversationActivity().Count == 0, "Opening a friend does not create a conversation");
            Section("lan"); Check(Rows().Single().Id == lan && Rows()[0].Subtitle.Contains("10.10.15.30:45853"), "LAN directory keeps endpoints, excludes friends and blocked peers");
            ((TextBox)window.FindName("ContactSearch")).Text = "10.10.15"; Check(Rows().Single().Id == lan, "Directory search accepts IP prefixes");
            ((TextBox)window.FindName("ContactSearch")).Text = "10.99"; Check(Rows().Length == 0, "Directory search hides unmatched endpoints");
            Section("lan"); Snapshot(window, Path.Combine(root, "lan-directory-light.png"));
            await node.ConnectAsync("127.0.0.1", remote.Port, remoteId.Id); await Until(() => remote.IsConnected(node.Identity.Id), "Connected"); Call(window, "Refresh");
            Check(Rows().Any(p => p.Id == remoteId.Id), "A newly connected untrusted peer appears in LAN");
            Section("chats"); Check(Rows().Length == 0 && ((TextBlock)window.FindName("OnlineCount")).Text == "0 online", "Connected machines without chat do not inflate the conversation count");
            await node.TrustAsync(remoteId.Id); await remote.TrustAsync(node.Identity.Id);
            Section("lan"); Check(Rows().All(p => p.Id != remoteId.Id), "Pairing removes the peer from unverified LAN contacts");
            Section("friends"); Check(Rows().Length == 2 && Rows().Any(p => p.Id == remoteId.Id), "Pairing adds a friend without manufacturing chat history");
            Snapshot(window, Path.Combine(root, "friends-light.png")); ApplyTheme("dark"); Snapshot(window, Path.Combine(root, "friends-dark.png"));
            window.Width = 820; window.UpdateLayout(); Snapshot(window, Path.Combine(root, "friends-compact.png"));
            Call(window, "ShowContact", remoteId.Id);
            var outgoing = await node.SendTextAsync(remoteId.Id, "Chào bạn!"); await Until(() => remoteStore.Message(outgoing.Id) is not null, "Outgoing delivered");
            Section("chats"); Check(Rows().Single().Id == remoteId.Id, "The first sent message creates a conversation row");
            _ = Dispatcher.InvokeAsync(() => Visuals<Button>(window).Single(b => Equals(b.Content, "Xóa chỉ ở phía tôi")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            await (Task)Call(window, "DeleteLocalConversation", remoteId.Id)!;
            Check(Rows().Length == 0 && store.Preference(remoteId.Id).Hidden, "Deleting a conversation removes its chat row");
            Section("friends"); Check(Rows().Any(p => p.Id == remoteId.Id), "Deleting a conversation preserves the friend");
            Call(window, "ShowContact", remoteId.Id); Section("chats"); Check(Rows().Length == 0, "Reopening a deleted friend's composer does not restore the chat row");
            var incoming = await remote.SendTextAsync(node.Identity.Id, "Bắt đầu lại nhé"); await Until(() => store.Message(incoming.Id) is not null, "Incoming delivered"); Call(window, "Refresh");
            Check(Rows().Single().Id == remoteId.Id && !store.Preference(remoteId.Id).Hidden, "A new incoming message restores a deleted conversation");
            await node.DeleteConversationHistoryAsync(remoteId.Id); Call(window, "Refresh");
            Check(Rows().Length == 0 && node.FindPeer(remoteId.Id)!.Trusted, "Clearing history also removes empty chat rows while preserving trust");
            Call(window, "ShowContact", remoteId.Id);
            await remote.SetConversationRulesAsync(node.Identity.Id, true, true);
            await Until(() => node.ConversationRules(remoteId.Id).CaptureProtected, "Remote capture restriction");
            Exception? permissionFailure = null;
            _ = Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var capture = Visuals<CheckBox>(window).Single(b => Equals(b.Content, "Chặn chụp và ghi màn hình"));
                    var forward = Visuals<CheckBox>(window).Single(b => Equals(b.Content, "Cho phép chuyển tiếp tin nhắn và tệp"));
                    Check(capture.IsChecked == true && !capture.IsEnabled && forward.IsEnabled, "Permissions UI locks the opponent-owned setting while leaving a default setting editable");
                }
                catch (Exception ex) { permissionFailure = ex; }
                finally { InAppDialog.CancelAll(window); }
            });
            Call(window, "ConversationRulesClick", window, new RoutedEventArgs()); if (permissionFailure is not null) throw permissionFailure;
            await remote.SetConversationRulesAsync(node.Identity.Id, false, true);
            await Until(() => !node.ConversationRules(remoteId.Id).CaptureProtected, "Remote owner releases restriction");
            Console.WriteLine($"{passed} contact directory UI checks passed. Screenshots: {root}");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally { if (window is not null) { Call(window, "ExitApplication"); await Task.Delay(700); } Vault.Dispose(); Shutdown(Environment.ExitCode); }
    }
}

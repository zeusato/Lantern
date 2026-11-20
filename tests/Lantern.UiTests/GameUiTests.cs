using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lantern.App;
using Lantern.Core;
using static Lantern.App.App;

internal sealed partial class UiHarness
{
    private async Task RunGamesUi(string? output)
    {
        using var watchdog = new System.Threading.Timer(_ => { Console.Error.WriteLine("Game UI fixture timed out."); Environment.Exit(1); }, null, TimeSpan.FromMinutes(2), System.Threading.Timeout.InfiniteTimeSpan);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = Path.GetFullPath(output ?? Path.Combine(Path.GetTempPath(), "Lantern-games-ui-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        var workspace = Path.Combine(root, "workspace");
        SetApp("DataDirectory", workspace); SetApp("TestMode", true); SetApp("Settings", new AppSettings { Name = "Linh", Onboarded = true, Theme = "light", AutoUpdates = false, MessageToasts = false });
        SetApp("Vault", WorkspaceVault.Create(workspace, "synthetic QA password only")); ApplyTheme("light");
        using (var seed = new LocalStore(workspace, Vault)) seed.SavePeer(new Peer { Id = new string('c', 64), Name = "Hội thoại khác", Trusted = true, Address = "127.0.0.1" });
        MainWindow? window = null;
        try
        {
            using var remoteId = new Identity(Path.Combine(root, "remote"), "Minh Anh"); using var remoteStore = new LocalStore(Path.Combine(root, "remote")); await using var remote = new PeerNode(remoteId, remoteStore); await remote.StartAsync(0, false, true);
            window = new MainWindow { Width = 1400, Height = 900 }; MainWindow = window; window.Show();
            var node = Field<PeerNode>(window, "node"); await Until(() => node.Port > 0, "Window started");
            await node.ConnectAsync("127.0.0.1", remote.Port, remoteId.Id); await Until(() => remote.IsConnected(node.Identity.Id), "Peer connected"); await node.TrustAsync(remoteId.Id); await remote.TrustAsync(node.Identity.Id);
            Call(window, "Refresh"); var list = (ListBox)window.FindName("ContactsList");
            void Select(string id) { Call(window, "Refresh"); list.SelectedItem = list.Items.OfType<ContactRow>().Single(r => r.Id == id); Call(window, "Refresh"); window.UpdateLayout(); }
            Select(remoteId.Id);
            var host = (Border)window.FindName("GameHost"); var chat = (Grid)window.FindName("ChatPane");
            async Task Ready(string id) { await Until(() => node.CurrentGame(remoteId.Id, id) is { Pending: false } a && remote.CurrentGame(node.Identity.Id, id) is { Pending: false } b && a.State.Head == b.State.Head, "Game synchronized"); Call(window, "Refresh"); window.UpdateLayout(); }
            var id = await remote.InviteGameAsync(node.Identity.Id, "caro"); await Ready(id);
            Check(host.Visibility == Visibility.Collapsed && Visuals<Button>(window).Any(b => Equals(b.Content, "Chấp nhận")), "Invitation stays in the chat before acceptance");
            await node.GameActionAsync(id, "decline"); await Ready(id); Check(!host.IsVisible, "Declining an invitation never splits the chat");
            id = await remote.InviteGameAsync(node.Identity.Id, "caro"); await Ready(id);
            Visuals<Button>(window).Single(b => Equals(b.Content, "Chấp nhận")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => node.CurrentGame(remoteId.Id)!.State.Status == "playing", "Accepted"); await Ready(id);
            Check(host.IsVisible && Math.Abs(chat.ActualWidth - host.ActualWidth) < 2, "Accepting splits conversation into equal chat and game columns");
            Check(((ColumnDefinition)window.FindName("DetailsColumn")).Width.Value == 0 && ((RichComposer)window.FindName("Composer")).IsEnabled, "Information sidebar is hidden and chat composer stays enabled");
            var message = await node.SendTextAsync(remoteId.Id, "Vừa chơi vừa trò chuyện nhé! 🙂"); await Until(() => remoteStore.Message(message.Id) is not null, "Chat while playing");
            await remote.GameActionAsync(id, "move", 112); await Ready(id); await node.GameActionAsync(id, "move", 113); await Ready(id);
            Snapshot(window, Path.Combine(root, "caro.png"));
            Select(new string('c', 64)); Check(!host.IsVisible, "Other conversations do not display the previous game's board"); Select(remoteId.Id); Check(host.IsVisible && node.CurrentGame(remoteId.Id)!.State.Board[113] != 0, "Returning to chat restores the ongoing game");
            Visuals<Button>(host).Single(b => Equals(b.Content, "×")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
            Check(!host.IsVisible && ((ColumnDefinition)window.FindName("GameColumn")).Width.Value == 0, "Closing game restores the full chat width");
            Call(window, "GameClick", window, new RoutedEventArgs()); Check(host.IsVisible, "Game button reopens the ongoing match");
            Vault.SetConversationPassword(remoteId.Id, "test-conversation"); Call(window, "Refresh"); Check(!host.IsVisible, "Locking a conversation conceals the game panel");
            Field<HashSet<string>>(window, "unlockedConversations").Add(remoteId.Id); Call(window, "Refresh");
            await node.GameActionAsync(id, "resign"); await Ready(id);
            id = await node.InviteGameAsync(remoteId.Id, "words"); await Ready(id); await remote.GameActionAsync(id, "accept"); await Ready(id);
            foreach (var item in new[] { (node, "học sinh"), (remote, "sinh nhật"), (node, "nhật ký"), (remote, "ký ức") }) { await item.Item1.GameActionAsync(id, "word", text: item.Item2); await Ready(id); }
            Check(Visuals<TextBlock>(host).Any(t => t.Text == "ức  🔒") && Visuals<TextBox>(host).Single().IsEnabled, "Word chain highlights and locks the required prefix beside an enabled next-syllable input");
            Snapshot(window, Path.Combine(root, "word-chain-light.png")); ApplyTheme("dark"); Snapshot(window, Path.Combine(root, "word-chain-dark.png"));
            await node.GameActionAsync(id, "resign"); await Ready(id);
            id = await node.InviteGameAsync(remoteId.Id, "ships"); await Ready(id); await remote.GameActionAsync(id, "accept"); await Ready(id);
            Check(Visuals<Button>(host).Any(b => Equals(b.Content, "Khóa đội tàu · Sẵn sàng")), "Battleship exposes manual placement and fleet locking"); Snapshot(window, Path.Combine(root, "battleship.png"));
            Console.WriteLine($"{passed} game UI checks passed. Screenshots: {root}");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally { if (window is not null) { Call(window, "ExitApplication"); await Task.Delay(700); } Vault.Dispose(); Shutdown(Environment.ExitCode); }
    }
}

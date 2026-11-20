using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lantern.App;
using Lantern.Core;
using static Lantern.App.App;

internal sealed partial class UiHarness
{
    private async Task CheckBattleshipUi(MainWindow window, Border host, PeerNode node, PeerNode remote, string peer, string id, Func<string, Task> ready, string root)
    {
        var panel = (ChatGamePanel)host.Child;
        void Click(string text) { window.UpdateLayout(); Visuals<Button>(host).Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout(); }
        void Place(int cell) { Call(panel, "PlaceShip", cell); window.UpdateLayout(); }
        int[] Fleet() => Field<int[]>(panel, "fleet");
        void Layout() { window.UpdateLayout(); }
        Rect BoardRect()
        {
            Layout(); var board = Visuals<GameBoard>(host).Single();
            return new Rect(board.TranslatePoint(new Point(0, 0), host), board.RenderSize);
        }
        bool Same(Rect a, Rect b) => Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;
        Check(Fleet().All(c => c == 0) && Visuals<Button>(host).Any(b => b.Content is string text && text.StartsWith("Đặt đủ 5 tàu") && !b.IsEnabled), "Placement starts with an empty board and cannot lock an incomplete fleet");
        Check(Visuals<Button>(host).Count(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Chọn tàu ")) == 5, "Five distinct ship choices show their lengths");
        Place(98); Check(Fleet().All(c => c == 0), "Placement rejects a ship extending beyond the board");
        Place(0); Check(Fleet().Take(5).All(c => c == 1) && Field<int>(panel, "selectedShip") == 2, "Placing a ship advances to the next unplaced ship");
        Place(1); Check(!Fleet().Contains(2) && Fleet().Count(c => c == 1) == 5, "Overlapping placement preserves the existing fleet");
        Place(20); Click("↔ Ngang · Xoay"); Place(40);
        Check(new[] { 40, 50, 60 }.All(c => Fleet()[c] == 3), "Rotation places the full selected ship vertically");
        Click("↕ Dọc · Xoay"); Place(42); Place(80);
        GameFleet.Validate(Fleet()); Check(Visuals<Button>(host).Single(b => Equals(b.Content, "Khóa đội tàu · Sẵn sàng")).IsEnabled, "Five correctly placed ships enable fleet locking");
        var boardFrame = Visuals<Border>(host).Single(b => b.Name == "ShipBoardFrame"); var placementBoard = Visuals<GameBoard>(host).Single();
        Check(Math.Abs(boardFrame.ActualWidth - placementBoard.ActualWidth - 4) < 1, "The map frame fits the playable grid without misleading empty side columns");
        var readyButton = Visuals<Button>(host).Single(b => Equals(b.Content, "Khóa đội tàu · Sẵn sàng"));
        Check(readyButton.TranslatePoint(new Point(0, readyButton.ActualHeight), host).Y <= host.ActualHeight, "The ready button remains visible below the placement board on a desktop window");
        ApplyTheme("light"); Snapshot(window, Path.Combine(root, "battleship-placement-light.png")); ApplyTheme("dark"); Snapshot(window, Path.Combine(root, "battleship-placement-dark.png"));
        Click("Xếp lại từ đầu"); Check(Fleet().All(c => c == 0), "Clear layout returns to manual placement");
        Click("Xếp tự động"); GameFleet.Validate(Fleet()); Check(Fleet().Count(c => c > 0) == 17, "Automatic placement provides a valid full fleet");
        Click("Khóa đội tàu · Sẵn sàng"); await Until(() => node.CurrentGame(peer, id)!.State.Fleets.ContainsKey(node.Identity.Id), "Fleet locked"); await ready(id);
        await remote.GameActionAsync(id, "fleet", board: GameFleet.RandomBoard()); await ready(id);
        var original = BoardRect();
        var stableRoot = panel.Content; var stableBoard = Visuals<GameBoard>(host).Single();
        var stableChat = ((Panel)window.FindName("MessagesPanel")).Children.Cast<UIElement>().ToArray();
        Check(Visuals<GameBoard>(host).Single().Cursor == Cursors.Cross && Visuals<TextBlock>(host).Any(t => t.Text.Contains("ĐẾN LƯỢT BẠN · BẮN NGAY")), "Your turn shows a clear action banner and targeting cursor");
        Snapshot(window, Path.Combine(root, "battleship-my-turn.png"));
        await node.GameActionAsync(id, "shot", 0); await Until(() => node.CurrentGame(peer, id)!.State.Turn == remote.Identity.Id, "Opponent turn"); await ready(id);
        Check(Same(original, BoardRect()) && Visuals<GameBoard>(host).Single().Cursor == Cursors.Arrow, "The opponent's turn keeps the exact board position and size and disables targeting");
        Snapshot(window, Path.Combine(root, "battleship-opponent-turn.png"));
        await remote.GameActionAsync(id, "shot", 0); await Until(() => node.CurrentGame(peer, id)!.State.Turn == node.Identity.Id, "My turn"); await ready(id);
        Check(Same(original, BoardRect()), "Returning to your turn does not move the firing map");
        Check(ReferenceEquals(stableRoot, panel.Content) && ReferenceEquals(stableBoard, Visuals<GameBoard>(host).Single()), "Battleship shots, results and acknowledgements retain the existing board and game shell");
        Check(stableChat.SequenceEqual(((Panel)window.FindName("MessagesPanel")).Children.Cast<UIElement>()), "Battleship turns never recreate the chat history");
        var current = node.CurrentGame(peer, id)!;
        panel.Update(current with { Pending = true, Connected = false, Error = "Kiểm thử trạng thái chờ kết nối" });
        Check(Same(original, BoardRect()), "Pending acknowledgement, offline text and errors never insert space above the map");
        panel.Update(current with { PeerName = new string('X', 80) });
        Check(Same(original, BoardRect()), "A long opponent name never shifts the map");
        Check(Field<TextBlock>(panel, "error").Text.Length == 0, "A recovered network state clears its earlier error without rebuilding the panel");
        panel.Update(current); Click("▦ Tàu của tôi"); Check(Same(original, BoardRect()), "Switching between firing and own fleet maps preserves geometry"); Click("◎ Bắn đối thủ");
        panel.ScrollToVerticalOffset(80); Layout(); var scrolled = BoardRect();
        panel.Update(current with { Pending = true }); Check(Same(scrolled, BoardRect()), "A turn refresh preserves a scrolled map's viewport position");
        panel.Update(current); panel.ScrollToTop(); window.Width = 900; Layout(); var compact = BoardRect();
        panel.Update(current with { Pending = true, Connected = false }); Check(Same(compact, BoardRect()), "Compact layout also stays fixed while waiting");
        panel.Update(current); ApplyTheme("light"); Snapshot(window, Path.Combine(root, "battleship-compact.png"));
    }
}

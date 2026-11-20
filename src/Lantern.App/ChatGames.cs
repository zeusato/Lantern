using System.Windows;
using System.Windows.Controls;
using Lantern.Core;

namespace Lantern.App;

public partial class MainWindow
{
    private ChatGamePanel? gamePanel;
    private string displayedGame = "";
    private string viewedGame = "", viewedConversation = "";
    private readonly HashSet<string> hiddenGames = [];
    private void HideGamePanel()
    {
        GameHost.Visibility = Visibility.Collapsed; GameColumn.Width = new GridLength(0); gamePanel = null; GameHost.Child = null; displayedGame = "";
    }
    private void RefreshGamePanel(bool locked)
    {
        GameButton.IsEnabled = !locked && node.FindPeer(selectedId) is { Trusted: true, Blocked: false };
        if (viewedConversation != selectedId) { viewedGame = ""; viewedConversation = selectedId; }
        var view = locked ? null : node.CurrentGame(selectedId, viewedGame.Length == 0 ? null : viewedGame);
        if (view is null || view.State.Status is "invited" or "declined" or "cancelled" || hiddenGames.Contains(view.State.Id)) { HideGamePanel(); return; }
        if (detailsVisible || GameHost.Visibility != Visibility.Visible) { detailsVisible = false; UpdateLayoutWidths(); GameColumn.Width = new GridLength(1, GridUnitType.Star); GameHost.Visibility = Visibility.Visible; }
        if (displayedGame != view.State.Id || gamePanel is null)
        {
            displayedGame = view.State.Id; var id = displayedGame; var conversation = selectedId;
            gamePanel = new ChatGamePanel(async (type, cell, text, board, revision) =>
            {
                if (appLocking || ConversationProtected || selectedId != conversation) throw new InvalidOperationException("Hội thoại đang khóa hoặc đã đổi.");
                await Task.Run(() => node.GameActionAsync(id, type, cell, text, board, revision)); RefreshGamesOnly();
            }, () => { hiddenGames.Add(id); HideGamePanel(); }, async () =>
            {
                try { await node.InviteGameAsync(conversation, view.State.Kind, view.State.Other(view.State.Starter)); viewedGame = ""; Refresh(); } catch (Exception ex) { ShowError(ex); }
            }); GameHost.Child = gamePanel;
        }
        gamePanel.Update(view);
    }
    private void GameClick(object sender, RoutedEventArgs e)
    {
        if (appLocking || ConversationProtected) return;
        viewedGame = "";
        if (node.CurrentGame(selectedId) is { State.Finished: false } game && game.State.Status != "invited") { hiddenGames.Remove(game.State.Id); RefreshGamePanel(false); return; }
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Chơi cùng nhau", 25, true));
        panel.Children.Add(Paragraph("Chọn game để gửi lời mời. Khi đối thủ chấp nhận, chat và bàn chơi chia đôi 1/1 để vừa chơi vừa trò chuyện.", muted: true));
        foreach (var kind in new[] { "caro", "ships", "words", "reversi" })
        {
            var description = kind switch { "caro" => "Nối 5 quân · Bàn 15×15", "ships" => "Giấu đội tàu · Bắn theo lượt · Bàn 10×10", "reversi" => "Kẹp và lật quân · Bàn 8×8", _ => "Nối hai tiếng · Chuỗi từ không lặp" };
            var button = new Button { Content = GameState.Title(kind) + "\n" + description, Padding = new Thickness(18), Margin = new Thickness(0, 6, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += async (_, _) => { button.IsEnabled = false; try { await node.InviteGameAsync(selectedId, kind); InAppDialog.CancelAll(this); Refresh(); } catch (Exception ex) { button.IsEnabled = true; ShowError(ex); } }; panel.Children.Add(button);
        }
        ShowDialog("Chơi game", panel, "Đóng", width: 520);
    }
    private readonly Dictionary<string, GameInvitationCard> gameCards = [];
    private int gameRefreshQueued;
    private void QueueGameRefresh()
    {
        if (Interlocked.Exchange(ref gameRefreshQueued, 1) != 0) return;
        Dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref gameRefreshQueued, 0); RefreshGamesOnly(); });
    }
    private void RefreshGamesOnly()
    {
        if (closing || !IsInitialized || appLocking || ConversationProtected) return;
        RefreshGamePanel(false); RefreshGameCards();
    }
    private void RefreshGameCards()
    {
        foreach (var item in gameCards) item.Value.Update(node.CurrentGame(selectedId, item.Key));
    }
    private FrameworkElement GameCard(ChatMessage message)
    {
        var card = new GameInvitationCard(async type =>
        {
            if (appLocking || ConversationProtected || selectedId != message.ConversationId) return;
            try { await Task.Run(() => node.GameActionAsync(message.GameId, type)); viewedGame = ""; RefreshGamesOnly(); }
            catch (Exception ex) { ShowError(ex); }
        }, () =>
        {
            if (appLocking || ConversationProtected || selectedId != message.ConversationId) return;
            viewedConversation = selectedId; viewedGame = message.GameId; hiddenGames.Remove(message.GameId); RefreshGamePanel(false);
        });
        gameCards[message.GameId] = card; card.Update(node.CurrentGame(message.ConversationId, message.GameId)); return card;
    }
}
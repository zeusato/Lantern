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
        if (view is null || view.State.Status == "invited" || hiddenGames.Contains(view.State.Id)) { HideGamePanel(); return; }
        detailsVisible = false; UpdateLayoutWidths(); GameColumn.Width = new GridLength(1, GridUnitType.Star); GameHost.Visibility = Visibility.Visible;
        if (displayedGame != view.State.Id || gamePanel is null)
        {
            displayedGame = view.State.Id; var id = displayedGame; var conversation = selectedId;
            gamePanel = new ChatGamePanel(async (type, cell, text, board, revision) =>
            {
                if (appLocking || ConversationProtected || selectedId != conversation) throw new InvalidOperationException("Hội thoại đang khóa hoặc đã đổi.");
                await node.GameActionAsync(id, type, cell, text, board, revision); Refresh();
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
        foreach (var kind in new[] { "caro", "ships", "words" })
        {
            var description = kind switch { "caro" => "Nối 5 quân · Bàn 15×15", "ships" => "Giấu đội tàu · Bắn theo lượt · Bàn 10×10", _ => "Nối hai tiếng · Chuỗi từ không lặp" };
            var button = new Button { Content = GameState.Title(kind) + "\n" + description, Padding = new Thickness(18), Margin = new Thickness(0, 6, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += async (_, _) => { button.IsEnabled = false; try { await node.InviteGameAsync(selectedId, kind); InAppDialog.CancelAll(this); Refresh(); } catch (Exception ex) { button.IsEnabled = true; ShowError(ex); } }; panel.Children.Add(button);
        }
        ShowDialog("Chơi game", panel, "Đóng", width: 520);
    }
    private FrameworkElement GameCard(ChatMessage message)
    {
        var panel = new StackPanel(); var view = node.CurrentGame(message.ConversationId, message.GameId);
        panel.Children.Add(Paragraph("🎮 " + (view is null ? "Ván đã xóa" : GameState.Title(view.State.Kind)), 19, true));
        if (view is null) return panel;
        var s = view.State;
        panel.Children.Add(Paragraph(s.Status == "invited" ? "Mời bạn chơi một ván. Vừa chơi vừa chat!" : s.Finished ? s.Reason.Length > 0 ? s.Reason : "Ván đã kết thúc" : s.Turn == identity.Id ? "Đến lượt bạn" : "Đang chờ đối thủ", 12, muted: true));
        var actions = new WrapPanel(); panel.Children.Add(actions);
        void Action(string label, string type)
        {
            var button = new Button { Content = label, IsEnabled = !view.Pending, Margin = new Thickness(0, 4, 6, 0) }; actions.Children.Add(button);
            button.Click += async (_, _) => { if (appLocking || ConversationProtected || selectedId != message.ConversationId) return; button.IsEnabled = false; try { await node.GameActionAsync(s.Id, type, expectedRevision: s.Revision); viewedGame = ""; Refresh(); } catch (Exception ex) { button.IsEnabled = true; ShowError(ex); } };
        }
        if (s.Status == "invited") { if (s.Host != identity.Id) { Action("Chấp nhận", "accept"); Action("Từ chối", "decline"); } else Action("Hủy lời mời", "cancel"); }
        else
        {
            var open = new Button { Content = "Mở bàn chơi", Margin = new Thickness(0, 4, 0, 0) }; open.Click += (_, _) => { if (appLocking || ConversationProtected) return; viewedConversation = selectedId; viewedGame = s.Id; hiddenGames.Remove(s.Id); RefreshGamePanel(false); }; actions.Children.Add(open);
        }
        if (view.Pending) panel.Children.Add(Paragraph("Đang chờ xác nhận…", 11, muted: true)); return panel;
    }
}

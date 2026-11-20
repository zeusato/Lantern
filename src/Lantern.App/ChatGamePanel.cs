using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lantern.Core;

namespace Lantern.App;

// The shell and controls live for the entire match. An acknowledgement updates
// properties; it never replaces Content, the board, the composer or the chat.
internal sealed partial class ChatGamePanel : ScrollViewer
{
    private readonly Func<string, int, string, int[]?, int, Task> act;
    private readonly Action close, rematch;
    private GameView view = null!;
    private bool sending;
    private int[] fleet = new int[100];
    private int selectedShip = 1;
    private bool vertical, ownBoard;
    private TextBox? wordInput;
    private TextBlock? error;
    private TextBlock players = null!, statusTitle = null!, statusHint = null!;
    private Border statusBadge = null!;
    private Action? updateBody;
    private readonly List<Action> updateControls = [];
    private StackPanel drawOffer = null!;
    private TextBlock drawText = null!;
    private WrapPanel playActions = null!, endActions = null!;
    private bool CanPlay => !sending && !view.Pending && !view.State.Finished && view.State.Status == "playing" && view.State.Turn == view.Me;
    public ChatGamePanel(Func<string, int, string, int[]?, int, Task> act, Action close, Action rematch)
    {
        this.act = act; this.close = close; this.rematch = rematch;
        VerticalScrollBarVisibility = ScrollBarVisibility.Visible; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SizeChanged += (_, _) => resizeShipBoard?.Invoke();
    }
    public void Update(GameView next)
    {
        bool mount = view is null || view.State.Id != next.State.Id;
        bool wordAdvanced = !mount && view!.State.Words.Count != next.State.Words.Count;
        bool errorChanged = view is null || view.Error != next.Error;
        view = next;
        if (mount) { fleet = new int[100]; selectedShip = 1; vertical = ownBoard = false; Build(); }
        if (wordAdvanced && wordInput is not null) wordInput.Clear();
        if ((mount || errorChanged) && error is not null) error.Text = next.Error;
        RefreshPresentation();
    }
    private TextBlock Label(string text, double size = 13, bool bold = false, bool muted = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); return block;
    }
    private Button Button(string text, Action click, bool enabled = true, bool accent = false)
    {
        var button = new Button { Content = text, IsEnabled = enabled, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 3, 6, 3), MinHeight = 34 };
        if (accent) button.SetResourceReference(StyleProperty, "AccentButton");
        button.Click += (_, _) => click(); return button;
    }
    private Button ActionButton(string text, Action click, Func<bool> enabled, bool accent = false)
    {
        var button = Button(text, click, accent: accent); updateControls.Add(() => button.IsEnabled = enabled()); return button;
    }
    private static void Visible(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    private async void Send(string type, int cell = -1, string text = "", int[]? board = null)
    {
        if (sending || view.Pending) return;
        var revision = view.State.Revision;
        try { sending = true; if (error is not null) error.Text = ""; RefreshPresentation(); await act(type, cell, text, board, revision); }
        catch (Exception ex) { if (error is not null) error.Text = ex.Message; }
        finally { sending = false; RefreshPresentation(); }
    }
    private void Build()
    {
        updateControls.Clear(); resizeShipBoard = null; wordInput = null;
        var panel = new StackPanel { Margin = new Thickness(18, 20, 18, 18) }; Content = panel;
        var heading = new DockPanel(); var exit = Button("×", close); exit.ToolTip = "Thu gọn bàn chơi; ván vẫn tiếp tục"; DockPanel.SetDock(exit, Dock.Right); heading.Children.Add(exit); heading.Children.Add(Label(GameState.Title(view.State.Kind), 24, true)); panel.Children.Add(heading);
        players = Label("", 11, true, true); players.TextWrapping = TextWrapping.NoWrap; players.TextTrimming = TextTrimming.CharacterEllipsis; panel.Children.Add(players);
        statusTitle = Label("", 14, true); statusTitle.Margin = new Thickness(0, 0, 0, 6); statusTitle.TextWrapping = TextWrapping.NoWrap; statusTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        statusHint = Label("", 12, muted: true); statusHint.Margin = new Thickness(0); statusHint.MaxHeight = 36; statusHint.TextTrimming = TextTrimming.CharacterEllipsis;
        var status = new StackPanel(); status.Children.Add(statusTitle); status.Children.Add(statusHint);
        statusBadge = new Border { Name = "ShipTurnStatus", Height = 90, Padding = new Thickness(12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Margin = new Thickness(0, 6, 0, 12), Child = status }; panel.Children.Add(statusBadge);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(statusBadge, System.Windows.Automation.AutomationLiveSetting.Polite);
        if (view.State.Kind == "ships") ShipPanel(panel);
        else if (view.State.Kind == "words") WordPanel(panel);
        else
        {
            bool reversi = view.State.Kind == "reversi";
            var score = Label("", 12, true); score.Height = 28; panel.Children.Add(score);
            var board = new GameBoard(reversi ? 8 : 15, new int[reversi ? 64 : 225], -1, [], i => Send("move", i), false, reversi: reversi);
            AddBoard(panel, board);
            panel.Children.Add(Label(reversi ? "Kẹp quân đối thủ giữa hai quân của bạn để lật màu. Chấm sáng là ô có thể đi; hết nước sẽ tự nhường lượt." : "Nối 5 quân theo hàng ngang, dọc hoặc chéo để thắng.", 12, muted: true));
            updateBody = () =>
            {
                var s = view.State; int mark = s.Starter == view.Me ? 1 : 2;
                score.Text = reversi ? $"● ĐEN {s.Board.Count(v => v == 1)}    ○ TRẮNG {s.Board.Count(v => v == 2)}    · Bạn cầm {(mark == 1 ? "Đen" : "Trắng")}" : mark == 1 ? "BẠN  X     ·     ĐỐI THỦ  O" : "BẠN  O     ·     ĐỐI THỦ  X";
                board.Update(s.Board, s.LastCell, reversi ? [] : s.WinningCells, CanPlay, legal: reversi && CanPlay ? ReversiRules.LegalMoves(s.Board, mark) : []);
            };
        }
        error = Label("", 12); error.Foreground = Brushes.IndianRed; error.Height = 36; error.TextTrimming = TextTrimming.CharacterEllipsis; panel.Children.Add(error);
        drawOffer = new StackPanel(); drawText = Label(""); drawOffer.Children.Add(drawText);
        var draw = new WrapPanel(); draw.Children.Add(ActionButton("Đồng ý hòa", () => Send("draw"), () => !sending && !view.Pending)); draw.Children.Add(ActionButton("Chơi tiếp", () => Send("draw-decline"), () => !sending && !view.Pending)); drawOffer.Children.Add(draw); panel.Children.Add(drawOffer);
        playActions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        playActions.Children.Add(ActionButton("Xin hòa", () => Send("draw"), () => !sending && !view.Pending && view.State.DrawOffer.Length == 0));
        playActions.Children.Add(ActionButton("Đầu hàng", () => { if (InAppDialog.Show(Window.GetWindow(this), "Đầu hàng", Label("Kết thúc ván và nhận thua?"), "Đầu hàng")) Send("resign"); }, () => !sending && !view.Pending)); panel.Children.Add(playActions);
        endActions = new WrapPanel(); endActions.Children.Add(ActionButton("Tái đấu", rematch, () => view.Connected, true)); endActions.Children.Add(Button("Đóng game", close)); panel.Children.Add(endActions);
    }
    private void RefreshPresentation()
    {
        if (view is null || Content is null) return;
        var s = view.State;
        players.Text = "BẠN  ×  " + view.PeerName.ToUpperInvariant();
        if (s.Kind == "ships") ShipStatus();
        else
        {
            statusTitle.Text = s.Finished ? s.Winner.Length == 0 ? "HAI BÊN HÒA" : s.Winner == view.Me ? "BẠN ĐÃ THẮNG!" : "ĐỐI THỦ ĐÃ THẮNG" : sending || view.Pending ? "ĐANG XÁC NHẬN LƯỢT" : CanPlay ? "ĐẾN LƯỢT BẠN" : "LƯỢT ĐỐI THỦ";
            statusHint.Text = s.Finished ? s.Reason : !view.Connected ? "Đối thủ offline. Lượt chơi được giữ để đồng bộ lại." : s.Kind == "reversi" && s.Reason.Length > 0 ? (s.Turn == view.Me ? "Đối thủ không có nước hợp lệ. Bạn đi tiếp." : "Bạn không có nước hợp lệ. Đối thủ đi tiếp.") : s.Kind == "words" ? "Nối tiếng cuối của từ trước. Không lặp lại từ đã dùng." : "Chọn một ô trên bàn khi đến lượt bạn.";
            SetStatusAccent(CanPlay);
        }
        statusTitle.ToolTip = statusTitle.Text; statusHint.ToolTip = statusHint.Text;
        System.Windows.Automation.AutomationProperties.SetName(statusBadge, statusTitle.Text + ". " + statusHint.Text);
        updateBody?.Invoke();
        foreach (var update in updateControls) update();
        drawText.Text = view.PeerName + " đề nghị hòa.";
        Visible(drawOffer, !s.Finished && s.DrawOffer.Length > 0 && s.DrawOffer != view.Me); Visible(playActions, !s.Finished); Visible(endActions, s.Finished);
    }
    private void SetStatusAccent(bool active)
    {
        statusBadge.SetResourceReference(Border.BackgroundProperty, active ? "AccentSoftBrush" : "SoftBrush");
        statusBadge.SetResourceReference(Border.BorderBrushProperty, active ? "AccentBrush" : "LineBrush");
    }
    private void AddBoard(Panel panel, GameBoard board)
    {
        var frame = new Border { Child = board, Padding = new Thickness(5), CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        frame.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
        var scroller = new ScrollViewer { Content = frame, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; panel.Children.Add(scroller);
        board.Width = board.Height = 300;
        resizeShipBoard = () => { if (scroller.ActualWidth > 0) board.Width = board.Height = Math.Max(160, Math.Min(Math.Max(180, ActualHeight - 425), Math.Min(540, scroller.ActualWidth - 12))); };
        scroller.SizeChanged += (_, _) => resizeShipBoard?.Invoke();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lantern.Core;

namespace Lantern.App;

internal sealed class ChatGamePanel : ScrollViewer
{
    private readonly Func<string, int, string, int[]?, int, Task> act;
    private readonly Action close;
    private readonly Action rematch;
    private GameView view = null!;
    private string rendered = "", draft = "";
    private int[] fleet = GameFleet.RandomBoard();
    private int selectedShip = 1;
    private bool vertical, ownBoard;
    private TextBox? wordInput;
    private TextBlock? error;
    public ChatGamePanel(Func<string, int, string, int[]?, int, Task> act, Action close, Action rematch)
    {
        this.act = act; this.close = close; this.rematch = rematch;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }
    public void Update(GameView next)
    {
        var key = Codec.Encode(next); if (key == rendered) return;
        if (wordInput is not null) draft = wordInput.Text;
        if (view is not null && (next.State.Id != view.State.Id || next.State.Words.Count != view.State.Words.Count)) draft = "";
        view = next; rendered = key; Build();
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
    private async void Send(string type, int cell = -1, string text = "", int[]? board = null)
    {
        var revision = view.State.Revision;
        try { if (error is not null) error.Text = ""; IsEnabled = false; await act(type, cell, text, board, revision); }
        catch (Exception ex)
        {
            if (error is not null) error.Text = ex.Message;
            var shake = new TranslateTransform(); if (wordInput is not null) { wordInput.RenderTransform = shake; shake.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 5, TimeSpan.FromMilliseconds(70)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) }); }
        }
        finally { IsEnabled = true; }
    }
    private void Build()
    {
        wordInput = null;
        var s = view.State; var panel = new StackPanel { Margin = new Thickness(18, 20, 18, 18) }; Content = panel;
        var heading = new DockPanel(); var exit = Button("×", close); exit.ToolTip = "Thu gọn bàn chơi; ván vẫn tiếp tục"; DockPanel.SetDock(exit, Dock.Right); heading.Children.Add(exit); heading.Children.Add(Label(GameState.Title(s.Kind), 24, true)); panel.Children.Add(heading);
        panel.Children.Add(Label("BẠN  ×  " + view.PeerName.ToUpperInvariant(), 11, true, true));
        var message = s.Finished ? s.Status == "cancelled" ? "Ván đã hủy" : s.Status == "declined" ? "Lời mời đã từ chối" : s.Winner.Length == 0 ? "HAI BÊN HÒA" : s.Winner == view.Me ? "BẠN ĐÃ THẮNG!" : view.PeerName + " thắng" : s.Status == "placing" ? "XẾP ĐỘI TÀU CỦA BẠN" : s.Status == "verifying" ? "ĐANG KIỂM CHỨNG ĐỘI TÀU" : s.PendingCell >= 0 ? "ĐANG CHỜ KẾT QUẢ BẮN" : s.Turn == view.Me ? "ĐẾN LƯỢT BẠN" : "ĐANG CHỜ " + view.PeerName.ToUpperInvariant();
        var badge = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 6, 0, 16), Child = Label(message, 14, true) }; badge.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); panel.Children.Add(badge);
        if (!view.Connected) panel.Children.Add(Label("Đối thủ đang offline. Ván đã lưu; lượt gửi sẽ được đồng bộ khi kết nối lại.", muted: true));
        if (view.Pending) panel.Children.Add(Label("Đang chờ đối thủ xác nhận…", muted: true));
        if (s.Finished) panel.Children.Add(Label(s.Reason, muted: true));
        bool canPlay = !view.Pending && !s.Finished && s.Turn == view.Me;
        if (s.Kind == "words") WordPanel(panel, canPlay);
        else if (s.Kind == "caro")
        {
            panel.Children.Add(Label("Nối từ 5 quân • " + (s.Starter == view.Me ? "Bạn đi X trước" : "Bạn đi O sau"), 12, muted: true));
            AddBoard(panel, new GameBoard(15, s.Board, s.LastCell, s.WinningCells, i => Send("move", i), canPlay));
        }
        else ShipPanel(panel, canPlay);
        error = Label(view.Error, 12); error.Foreground = Brushes.IndianRed; panel.Children.Add(error);
        if (!s.Finished)
        {
            if (s.DrawOffer.Length > 0 && s.DrawOffer != view.Me)
            {
                panel.Children.Add(Label(view.PeerName + " đề nghị hòa.")); var draw = new WrapPanel(); draw.Children.Add(Button("Đồng ý hòa", () => Send("draw"), !view.Pending)); draw.Children.Add(Button("Chơi tiếp", () => Send("draw-decline"), !view.Pending)); panel.Children.Add(draw);
            }
            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            actions.Children.Add(Button("Xin hòa", () => Send("draw"), !view.Pending && s.DrawOffer.Length == 0));
            actions.Children.Add(Button("Đầu hàng", () => { if (MessageBox.Show(Window.GetWindow(this), "Kết thúc ván và nhận thua?", "Đầu hàng", MessageBoxButton.YesNo) == MessageBoxResult.Yes) Send("resign"); }, !view.Pending)); panel.Children.Add(actions);
        }
        else { var actions = new WrapPanel(); actions.Children.Add(Button("Tái đấu", rematch, view.Connected, true)); actions.Children.Add(Button("Đóng game", close)); panel.Children.Add(actions); }
    }
    private void AddBoard(Panel panel, GameBoard board)
    {
        board.Margin = new Thickness(0, 0, 0, 12);
        var scroller = new ScrollViewer { Content = board, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(scroller);
        board.Width = board.Height = 330;
        scroller.SizeChanged += (_, _) => board.Width = board.Height = Math.Max(280, Math.Min(600, scroller.ActualWidth - 4));
    }
    private void ShipPanel(Panel panel, bool canPlay)
    {
        var s = view.State;
        if (s.Status == "placing" && !s.Fleets.ContainsKey(view.Me))
        {
            panel.Children.Add(Label("Đội tàu 5–4–3–3–2 ô. Chọn tàu, chọn hướng rồi bấm ô đặt đầu tàu. Không chồng tàu.", 12, muted: true));
            var choices = new WrapPanel();
            for (int ship = 1; ship <= 5; ship++) { var id = ship; choices.Children.Add(Button($"{(selectedShip == id ? "● " : "")}{GameFleet.Lengths[id - 1]} ô", () => { selectedShip = id; Build(); })); } panel.Children.Add(choices);
            var tools = new WrapPanel(); tools.Children.Add(Button(vertical ? "↕ Dọc" : "↔ Ngang", () => { vertical = !vertical; Build(); })); tools.Children.Add(Button("Xếp tự động", () => { fleet = GameFleet.RandomBoard(); Build(); }, !view.Pending)); panel.Children.Add(tools);
            AddBoard(panel, new GameBoard(10, fleet.Select(v => v == 0 ? 0 : 3).ToArray(), -1, [], cell =>
            {
                var length = GameFleet.Lengths[selectedShip - 1]; if ((vertical ? cell / 10 : cell % 10) + length > 10) { if (error is not null) error.Text = "Tàu vượt mép bàn."; return; }
                var cells = Enumerable.Range(0, length).Select(i => cell + i * (vertical ? 10 : 1)).ToArray(); if (cells.Any(i => fleet[i] != 0 && fleet[i] != selectedShip)) { if (error is not null) error.Text = "Vị trí này chồng lên tàu khác."; return; }
                for (int i = 0; i < 100; i++) if (fleet[i] == selectedShip) fleet[i] = 0; foreach (int i in cells) fleet[i] = selectedShip; Build();
            }, !view.Pending, ships: true));
            panel.Children.Add(Button("Khóa đội tàu · Sẵn sàng", () => Send("fleet", board: fleet), !view.Pending, true)); return;
        }
        var toggle = new WrapPanel(); toggle.Children.Add(Button(ownBoard ? "Xem vùng bắn đối thủ" : "Xem đội tàu của tôi", () => { ownBoard = !ownBoard; Build(); })); panel.Children.Add(toggle);
        var target = ownBoard ? view.Me : s.Other(view.Me); var shots = s.Shots.GetValueOrDefault(target) ?? [];
        var board = new int[100];
        if (ownBoard && view.OwnBoard.Length == 100) for (int i = 0; i < 100; i++) if (view.OwnBoard[i] > 0) board[i] = 3;
        foreach (var shot in shots) board[shot.Key] = shot.Value == 0 ? 4 : 5;
        panel.Children.Add(Label(ownBoard ? "Đội tàu của bạn" : "VÙNG BẮN · " + view.PeerName, 13, true));
        AddBoard(panel, new GameBoard(10, board, s.LastCell, [], i => Send("shot", i), canPlay && !ownBoard && s.Status == "playing" && s.PendingCell < 0, ships: true));
        var sunk = Enumerable.Range(1, 5).Count(ship => shots.Values.Count(v => v == ship) == GameFleet.Lengths[ship - 1]);
        panel.Children.Add(Label($"Trúng {shots.Values.Count(v => v > 0)}/17 ô · Chìm {sunk}/5 tàu", 12, muted: true));
        if (s.Status == "placing") panel.Children.Add(Label("Đội tàu đã khóa. Đang chờ đối thủ sẵn sàng.", muted: true));
    }
    private void WordPanel(Panel panel, bool canPlay)
    {
        var s = view.State; panel.Children.Add(Label($"CHUỖI NỐI  ·  {s.Words.Count} TỪ", 11, true, true));
        for (int i = Math.Max(0, s.Words.Count - 6); i < s.Words.Count; i++)
        {
            var parts = s.Words[i].Split(' '); bool mine = (i % 2 == 0 ? s.Starter : s.Other(s.Starter)) == view.Me;
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            for (int part = 0; part < 2; part++)
            {
                var token = new Border { Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(9), Margin = new Thickness(2), Child = new TextBlock { Text = parts[part], FontSize = 18, FontWeight = FontWeights.SemiBold } };
                ((TextBlock)token.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                token.SetResourceReference(Border.BackgroundProperty, part == 1 ? "AccentSoftBrush" : "SoftBrush"); words.Children.Add(token);
            }
            var card = new Border { Child = words, HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3) }; panel.Children.Add(card);
            if (i == s.Words.Count - 1) { card.BeginAnimation(OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(260))); }
            if (i < s.Words.Count - 1) panel.Children.Add(Label("↳", 18, muted: true));
        }
        if (s.Words.Count > 6) panel.Children.Add(Button("Xem toàn bộ chuỗi", () => MessageBox.Show(Window.GetWindow(this), string.Join(" → ", s.Words), "Lịch sử nối chữ")));
        if (s.ProposedWord.Length > 0)
        {
            panel.Children.Add(Label("Đề nghị công nhận: “" + s.ProposedWord + "”", 16, true));
            if (s.Turn != view.Me) { var choices = new WrapPanel(); choices.Children.Add(Button("Có nghĩa · Công nhận", () => Send("word-approve"), !view.Pending, true)); choices.Children.Add(Button("Không chấp nhận", () => Send("word-reject"), !view.Pending)); panel.Children.Add(choices); }
            else panel.Children.Add(Label("Đang chờ đối thủ quyết định.", muted: true));
            return;
        }
        if (s.Finished) return;
        var prefix = s.Words.Count > 0 ? s.Words[^1].Split(' ')[1] : "";
        panel.Children.Add(Label(canPlay ? "✨ ĐẾN LƯỢT BẠN" : "Đối thủ đang nghĩ…", 14, true));
        if (prefix.Length > 0) { var token = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Child = Label(prefix + "  🔒", 22, true) }; token.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); panel.Children.Add(token); }
        wordInput = new TextBox { Text = draft, FontSize = 19, MaxLength = prefix.Length > 0 ? 16 : 40, Padding = new Thickness(12), IsEnabled = canPlay, Margin = new Thickness(0, 8, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(wordInput, prefix.Length == 0 ? "Nhập hai tiếng để bắt đầu" : "Nhập tiếng tiếp theo"); panel.Children.Add(wordInput);
        void SubmitWord() { var word = WordChain.Normalize((prefix.Length == 0 ? "" : prefix + " ") + wordInput.Text); Send(WordChain.Contains(word) ? "word" : "word-propose", text: word); }
        wordInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SubmitWord(); e.Handled = true; } };
        panel.Children.Add(Button("Nối từ →", SubmitWord, canPlay, true));
        panel.Children.Add(Label(prefix.Length == 0 ? "Nhập hai tiếng, ví dụ “học sinh”." : "Chỉ cần nhập tiếng thứ hai. Tiếng đầu đã được giữ sẵn.", 12, muted: true));
        panel.Children.Add(Label("Từ chưa có trong từ điển sẽ được gửi để đối thủ công nhận. Không lặp lại từ đã dùng.", 11, muted: true));
    }
}

internal sealed class GameBoard(int size, int[] cells, int last, IReadOnlyCollection<int> winning, Action<int> click, bool playable, bool ships = false) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var unit = ActualWidth / size;
        var line = (Brush)FindResource("LineBrush"); var surface = (Brush)FindResource("SurfaceBrush"); var accent = (Brush)FindResource("AccentBrush");
        dc.DrawRectangle(surface, null, new Rect(0, 0, ActualWidth, ActualHeight));
        for (int i = 0; i < cells.Length; i++)
        {
            var box = new Rect(i % size * unit, i / size * unit, unit, unit);
            if (winning.Contains(i) || i == last) dc.DrawRectangle((Brush)FindResource("AccentSoftBrush"), null, box);
            dc.DrawRectangle(null, new Pen(line, .7), box);
            var value = cells[i]; var center = new Point(box.X + unit / 2, box.Y + unit / 2); var radius = unit * .27;
            if (ships)
            {
                if (value == 3) dc.DrawRoundedRectangle(accent, null, new Rect(box.X + 3, box.Y + 3, unit - 6, unit - 6), 3, 3);
                if (value == 4) dc.DrawEllipse((Brush)FindResource("MutedBrush"), null, center, unit * .08, unit * .08);
                if (value == 5) { dc.DrawEllipse(Brushes.Coral, null, center, radius, radius); dc.DrawLine(new Pen(surface, 2), new(center.X - radius / 2, center.Y - radius / 2), new(center.X + radius / 2, center.Y + radius / 2)); }
            }
            else if (value == 1)
            {
                var pen = new Pen(accent, 2.2); dc.DrawLine(pen, new(center.X - radius, center.Y - radius), new(center.X + radius, center.Y + radius)); dc.DrawLine(pen, new(center.X + radius, center.Y - radius), new(center.X - radius, center.Y + radius));
            }
            else if (value == 2) dc.DrawEllipse(null, new Pen(Brushes.Coral, 2.2), center, radius, radius);
        }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (!playable || ActualWidth <= 0) return;
        var p = e.GetPosition(this); var x = (int)(p.X * size / ActualWidth); var y = (int)(p.Y * size / ActualHeight);
        if (x >= 0 && x < size && y >= 0 && y < size) { click(y * size + x); e.Handled = true; }
    }
}

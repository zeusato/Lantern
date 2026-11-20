using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Lantern.Core;

namespace Lantern.App;

internal sealed partial class ChatGamePanel
{
    private Action? resizeShipBoard;
    private bool CanShoot => CanPlay && view.State.PendingCell < 0;
    private int PlacedShips => Enumerable.Range(1, 5).Count(id => fleet.Count(v => v == id) == GameFleet.Lengths[id - 1]);
    private void ShipStatus()
    {
        var s = view.State;
        if (s.Finished) { statusTitle.Text = s.Winner.Length == 0 ? "VÁN ĐÃ KẾT THÚC" : s.Winner == view.Me ? "BẠN ĐÃ THẮNG!" : "ĐỐI THỦ ĐÃ THẮNG"; statusHint.Text = s.Reason; }
        else if (s.Status == "placing") { statusTitle.Text = s.Fleets.ContainsKey(view.Me) ? "ĐÃ SẴN SÀNG · CHỜ ĐỐI THỦ" : $"XẾP TÀU · {PlacedShips}/5 TÀU"; statusHint.Text = s.Fleets.ContainsKey(view.Me) ? "Đội tàu đã khóa. Trận đấu bắt đầu khi cả hai sẵn sàng." : "1 Chọn tàu  →  2 Chọn hướng  →  3 Bấm ô đặt đầu tàu"; }
        else if (s.Status == "verifying") { statusTitle.Text = "ĐANG KIỂM CHỨNG"; statusHint.Text = "Đang xác nhận kết quả ván chơi."; }
        else if (sending || view.Pending || s.PendingCell >= 0) { statusTitle.Text = "ĐANG CHỜ KẾT QUẢ"; statusHint.Text = "Đã gửi lượt bắn. Bàn bắn sẽ mở lại khi đến lượt bạn."; }
        else if (CanShoot) { statusTitle.Text = "◎ ĐẾN LƯỢT BẠN · BẮN NGAY"; statusHint.Text = ownBoard ? "Chọn “Bắn đối thủ” để bắn một ô chưa thử." : "Bấm một ô trống trên vùng bắn bên dưới."; }
        else { statusTitle.Text = "◷ LƯỢT ĐỐI THỦ"; statusHint.Text = "Chờ đối thủ bắn. Bạn vẫn có thể xem hai bản đồ."; }
        if (!view.Connected) statusHint.Text = "Đối thủ đang offline. Ván đã lưu; lượt gửi sẽ đồng bộ khi kết nối lại.";
        SetStatusAccent(CanShoot); // A steady accent, never blink the panel on acknowledgements.
    }
    private void ShipPanel(Panel panel)
    {
        var placement = new StackPanel(); panel.Children.Add(placement);
        var choices = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 8) }; placement.Children.Add(choices);
        var shipChoices = new List<(Button Button, TextBlock Caption, int Id)>();
        for (int ship = 1; ship <= 5; ship++)
        {
            int id = ship, length = GameFleet.Lengths[id - 1]; var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = $"Tàu {id}", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = new string('■', length), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 4) });
            var caption = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }; content.Children.Add(caption);
            var choice = Button("", () => { selectedShip = id; RefreshPresentation(); }); choice.Content = content; choice.Padding = new Thickness(4, 8, 4, 8);
            choice.ToolTip = $"Chọn tàu {id}, dài {length} ô · Bấm bàn để đổi vị trí";
            System.Windows.Automation.AutomationProperties.SetName(choice, $"Chọn tàu {id}, {length} ô"); choices.Children.Add(choice); shipChoices.Add((choice, caption, id));
        }
        var tools = new WrapPanel(); placement.Children.Add(tools);
        var rotate = ActionButton("", () => { vertical = !vertical; RefreshPresentation(); }, () => !sending && !view.Pending); tools.Children.Add(rotate);
        tools.Children.Add(ActionButton("Xếp tự động", () => { fleet = GameFleet.RandomBoard(); RefreshPresentation(); }, () => !sending && !view.Pending));
        tools.Children.Add(ActionButton("Xếp lại từ đầu", () => { fleet = new int[100]; selectedShip = 1; RefreshPresentation(); }, () => !sending && !view.Pending));
        var instruction = Label("", 12, muted: true); instruction.Height = 40; placement.Children.Add(instruction);
        var tabs = new UniformGrid { Columns = 2, Height = 42, Margin = new Thickness(0, 0, 0, 8) }; panel.Children.Add(tabs);
        var attack = Button("◎ Bắn đối thủ", () => { ownBoard = false; RefreshPresentation(); });
        var defend = Button("▦ Tàu của tôi", () => { ownBoard = true; RefreshPresentation(); }); tabs.Children.Add(attack); tabs.Children.Add(defend);
        var boardCaption = Label("", 12, true); boardCaption.Height = 18; boardCaption.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(boardCaption);
        bool IsPlacing() => view.State.Status == "placing" && !view.State.Fleets.ContainsKey(view.Me);
        var board = new GameBoard(10, new int[100], -1, [], i => { if (IsPlacing()) PlaceShip(i); else Send("shot", i); }, false, ships: true) { Name = "ShipBoard" };
        var frame = new Border { Name = "ShipBoardFrame", HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Child = board };
        var scroller = new ScrollViewer { Content = frame, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 10) }; panel.Children.Add(scroller);
        board.Width = board.Height = 280;
        resizeShipBoard = () => { if (scroller.ActualWidth > 0) board.Width = board.Height = Math.Max(120, Math.Min(Math.Max(180, ActualHeight - (IsPlacing() ? 510 : 395)), Math.Min(540, scroller.ActualWidth - 6))); };
        scroller.SizeChanged += (_, _) => resizeShipBoard?.Invoke();
        var ready = ActionButton("", () => Send("fleet", board: fleet), () => !sending && !view.Pending && PlacedShips == 5, true); panel.Children.Add(ready);
        var help = Label("Tàu có thể chạm nhau nhưng không chồng ô. Chọn lại một tàu để di chuyển; đối thủ không thấy đội tàu của bạn.", 12, muted: true); panel.Children.Add(help);
        var summary = Label("", 12, muted: true); summary.Height = 34; panel.Children.Add(summary);
        bool? previousPhase = null;
        updateBody = () =>
        {
            var s = view.State; bool placing = IsPlacing();
            Visible(placement, placing); Visible(tabs, !placing); Visible(boardCaption, !placing); Visible(ready, placing); Visible(help, placing); Visible(summary, !placing);
            int[] cells; int last;
            if (placing)
            {
                foreach (var item in shipChoices) { item.Caption.Text = $"{GameFleet.Lengths[item.Id - 1]} ô" + (fleet.Contains(item.Id) ? " ✓" : ""); item.Button.SetResourceReference(StyleProperty, item.Id == selectedShip ? "AccentButton" : typeof(Button)); item.Button.IsEnabled = !sending && !view.Pending; }
                rotate.Content = vertical ? "↕ Dọc · Xoay" : "↔ Ngang · Xoay";
                instruction.Text = $"Đang chọn tàu {selectedShip} · {GameFleet.Lengths[selectedShip - 1]} ô · {(vertical ? "Dọc ↓" : "Ngang →")}\nRê chuột để xem trước; xanh: đặt được, đỏ: bị chặn.";
                ready.Content = PlacedShips == 5 ? "Khóa đội tàu · Sẵn sàng" : $"Đặt đủ 5 tàu để sẵn sàng · {PlacedShips}/5";
                cells = fleet.Select(v => v == 0 ? 0 : 3).ToArray(); last = -1;
            }
            else
            {
                attack.SetResourceReference(StyleProperty, !ownBoard ? "AccentButton" : typeof(Button)); defend.SetResourceReference(StyleProperty, ownBoard ? "AccentButton" : typeof(Button));
                boardCaption.Text = ownBoard ? "ĐỘI TÀU CỦA BẠN" : "VÙNG BẮN ĐỐI THỦ";
                string target = ownBoard ? view.Me : s.Other(view.Me); var shots = s.Shots.GetValueOrDefault(target) ?? [];
                cells = new int[100]; if (ownBoard && view.OwnBoard.Length == 100) for (int i = 0; i < 100; i++) if (view.OwnBoard[i] > 0) cells[i] = 3;
                foreach (var shot in shots) cells[shot.Key] = shot.Value == 0 ? 4 : 5;
                last = s.Turn == target && shots.ContainsKey(s.LastCell) ? s.LastCell : -1;
                int sunk = Enumerable.Range(1, 5).Count(ship => shots.Values.Count(v => v == ship) == GameFleet.Lengths[ship - 1]);
                summary.Text = $"Trúng {shots.Values.Count(v => v > 0)}/17 ô · Chìm {sunk}/5 tàu     ● Trúng   · Trượt";
            }
            board.Update(cells, last, [], placing ? !sending && !view.Pending : CanShoot && !ownBoard, placing ? fleet : ownBoard ? view.OwnBoard : null, placing ? selectedShip : 0, vertical);
            frame.SetResourceReference(Border.BorderBrushProperty, CanShoot && !ownBoard ? "AccentBrush" : "LineBrush");
            if (previousPhase != placing) { previousPhase = placing; resizeShipBoard?.Invoke(); }
        };
    }
    private void PlaceShip(int cell)
    {
        if (sending || view.Pending || view.State.Fleets.ContainsKey(view.Me)) return;
        var cells = GameBoard.PlacementCells(cell, GameFleet.Lengths[selectedShip - 1], vertical);
        if (cells.Length == 0 || cells.Any(i => fleet[i] != 0 && fleet[i] != selectedShip))
        { if (error is not null) error.Text = cells.Length == 0 ? "Tàu vượt mép bàn. Đổi hướng hoặc chọn ô khác." : "Đang chồng tàu khác. Chọn vùng xem trước màu xanh."; return; }
        for (int i = 0; i < 100; i++) if (fleet[i] == selectedShip) fleet[i] = 0;
        foreach (int i in cells) fleet[i] = selectedShip;
        var next = Enumerable.Range(1, 5).FirstOrDefault(id => !fleet.Contains(id)); if (next > 0) selectedShip = next;
        if (error is not null) error.Text = ""; RefreshPresentation();
    }
}

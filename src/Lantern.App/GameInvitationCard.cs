using System.Windows;
using System.Windows.Controls;
using Lantern.Core;

namespace Lantern.App;

internal sealed class GameInvitationCard : StackPanel
{
    private readonly TextBlock title = new() { FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly Button accept, decline, cancel, open;
    internal GameInvitationCard(Action<string> act, Action show)
    {
        Children.Add(title); Children.Add(status);
        var actions = new WrapPanel(); Children.Add(actions);
        Button Add(string text, Action click) { var b = new Button { Content = text, Margin = new Thickness(0, 4, 6, 0) }; b.Click += (_, _) => click(); actions.Children.Add(b); return b; }
        accept = Add("Chấp nhận", () => act("accept")); decline = Add("Từ chối", () => act("decline")); cancel = Add("Hủy lời mời", () => act("cancel")); open = Add("Mở bàn chơi", show);
    }
    internal void Update(GameView? view)
    {
        title.Text = "🎮 " + (view is null ? "Ván đã xóa" : GameState.Title(view.State.Kind));
        var s = view?.State;
        status.Text = view is null ? "" : s!.Status == "invited" ? "Mời bạn chơi một ván. Vừa chơi vừa chat!" : s.Finished ? s.Reason.Length > 0 ? s.Reason : "Ván đã kết thúc" : s.Status == "placing" ? "Đang xếp đội tàu" : view.Pending ? "Đang xác nhận lượt…" : s.Turn == view.Me ? "Đến lượt bạn" : "Đang chờ đối thủ";
        void Show(Button b, bool visible) { b.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; b.IsEnabled = view is { Pending: false }; }
        Show(accept, s?.Status == "invited" && s.Host != view!.Me); Show(decline, accept.Visibility == Visibility.Visible); Show(cancel, s?.Status == "invited" && s.Host == view!.Me);
        Show(open, s is not null && s.Status is not ("invited" or "declined" or "cancelled")); open.IsEnabled = view is not null;
    }
}

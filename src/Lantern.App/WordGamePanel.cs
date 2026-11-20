using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lantern.Core;

namespace Lantern.App;

internal sealed partial class ChatGamePanel
{
    private void WordPanel(Panel panel)
    {
        var count = Label("", 11, true, true); panel.Children.Add(count);
        var history = new StackPanel(); panel.Children.Add(history);
        var all = Button("Xem toàn bộ chuỗi", () => InAppDialog.Show(Window.GetWindow(this), "Lịch sử nối chữ", Label(string.Join(" → ", view.State.Words), 18), "Đóng")); panel.Children.Add(all);
        var proposal = new StackPanel(); var proposed = Label("", 16, true); proposal.Children.Add(proposed);
        var approve = ActionButton("Có nghĩa · Công nhận", () => Send("word-approve"), () => !sending && !view.Pending, true);
        var reject = ActionButton("Không chấp nhận", () => Send("word-reject"), () => !sending && !view.Pending);
        var choices = new WrapPanel(); choices.Children.Add(approve); choices.Children.Add(reject); proposal.Children.Add(choices);
        var waiting = Label("Đang chờ đối thủ quyết định.", 12, muted: true); proposal.Children.Add(waiting); panel.Children.Add(proposal);
        var inputPanel = new StackPanel(); panel.Children.Add(inputPanel);
        var turn = Label("", 14, true); inputPanel.Children.Add(turn);
        var prefixLabel = Label("", 22, true); prefixLabel.Margin = new Thickness(0);
        var prefixCard = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Child = prefixLabel }; prefixCard.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); inputPanel.Children.Add(prefixCard);
        wordInput = new TextBox { FontSize = 19, MaxLength = 40, Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 8) }; inputPanel.Children.Add(wordInput);
        void Submit()
        {
            if (!CanPlay || view.State.ProposedWord.Length > 0) return;
            string prefix = view.State.Words.Count > 0 ? view.State.Words[^1].Split(' ')[1] : "";
            var word = WordChain.Normalize((prefix.Length == 0 ? "" : prefix + " ") + wordInput.Text);
            Send(WordChain.Contains(word) ? "word" : "word-propose", text: word);
        }
        wordInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
        inputPanel.Children.Add(ActionButton("Nối từ →", Submit, () => CanPlay && view.State.ProposedWord.Length == 0, true));
        var help = Label("", 12, muted: true); inputPanel.Children.Add(help);
        inputPanel.Children.Add(Label("Từ chưa có trong từ điển sẽ được gửi để đối thủ công nhận. Không lặp lại từ đã dùng.", 11, muted: true));
        var cards = new Dictionary<int, FrameworkElement>();
        updateBody = () =>
        {
            var s = view.State; count.Text = $"CHUỖI NỐI  ·  {s.Words.Count} TỪ";
            int start = Math.Max(0, s.Words.Count - 6);
            foreach (var index in cards.Keys.Where(i => i < start || i >= s.Words.Count).ToArray()) { history.Children.Remove(cards[index]); cards.Remove(index); }
            for (int i = start; i < s.Words.Count; i++)
            {
                if (cards.ContainsKey(i)) continue;
                bool mine = (i % 2 == 0 ? s.Starter : s.Other(s.Starter)) == view.Me;
                var words = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var (part, n) in s.Words[i].Split(' ').Select((part, n) => (part, n)))
                {
                    var label = Label(part, 18, true); label.Margin = new Thickness(0);
                    var token = new Border { Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(9), Margin = new Thickness(2), Child = label }; token.SetResourceReference(Border.BackgroundProperty, n == 1 ? "AccentSoftBrush" : "SoftBrush"); words.Children.Add(token);
                }
                var card = new StackPanel { HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
                card.Children.Add(Label($"{i + 1:00}  ·  {(mine ? "BẠN" : view.PeerName)}", 10, true, true)); card.Children.Add(words); cards.Add(i, card); history.Children.Add(card);
            }
            Visible(all, s.Words.Count > 6);
            bool pendingWord = s.ProposedWord.Length > 0;
            proposed.Text = "Đề nghị công nhận: “" + s.ProposedWord + "”"; Visible(proposal, pendingWord && !s.Finished); Visible(choices, s.Turn != view.Me); Visible(waiting, s.Turn == view.Me);
            Visible(inputPanel, !s.Finished && !pendingWord);
            string prefix = s.Words.Count > 0 ? s.Words[^1].Split(' ')[1] : "";
            prefixLabel.Text = prefix + "  🔒"; Visible(prefixCard, prefix.Length > 0);
            turn.Text = CanPlay ? "ĐẾN LƯỢT BẠN" : "Đối thủ đang nghĩ…";
            wordInput.IsEnabled = CanPlay && !pendingWord; wordInput.MaxLength = prefix.Length > 0 ? 16 : 40;
            System.Windows.Automation.AutomationProperties.SetName(wordInput, prefix.Length == 0 ? "Nhập hai tiếng để bắt đầu" : "Nhập tiếng tiếp theo");
            help.Text = prefix.Length == 0 ? "Nhập hai tiếng, ví dụ “học sinh”." : "Chỉ cần nhập tiếng thứ hai. Tiếng đầu đã được giữ sẵn.";
        };
    }
}

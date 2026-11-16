using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lantern.Core;

namespace Lantern.App;

public sealed class LoginWindow : Window
{
    public WorkspaceVault? UnlockedVault { get; private set; }
    private readonly string directory;
    private readonly bool allowReset;
    private int attempts;
    private bool busy;
    public LoginWindow(string directory, bool allowReset = true)
    {
        this.directory = directory; this.allowReset = allowReset;
        Title = "Lantern · Mở khóa workspace"; Width = 500; Height = 660; MinHeight = 560; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Build();
    }
    private void Build()
    {
        bool creating = !WorkspaceVault.Exists(directory);
        Height = Math.Min(creating ? 790 : 660, SystemParameters.WorkArea.Height - 60);
        var panel = new StackPanel { Margin = new Thickness(42, 32, 42, 28) };
        var brand = new TextBlock { Text = "LANTERN", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 28) }; brand.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); panel.Children.Add(brand);
        var lockIcon = new TextBlock { Text = "\uE72E", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 31, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; lockIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var badge = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(22), HorizontalAlignment = HorizontalAlignment.Left, Child = lockIcon, Margin = new Thickness(0, 0, 0, 22) }; badge.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); panel.Children.Add(badge);
        panel.Children.Add(Text(creating ? "Không gian riêng\nbắt đầu từ đây." : "Chào mừng\nbạn trở lại.", 28, true));
        panel.Children.Add(Text(creating ? "Tạo mật khẩu cho workspace. Mỗi lần mở Lantern đều cần nhập mật khẩu này." : "Nhập mật khẩu để mở các cuộc trò chuyện trên máy này.", 13, muted: true));
        var name = new TextBox { Text = App.Settings.Name, MaxLength = 80, Margin = new Thickness(0, 0, 0, 12) };
        if (creating) { panel.Children.Add(Text("Tên hiển thị", 12, true)); panel.Children.Add(name); }
        var password = new PasswordBox { Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 14), MaxLength = 256 };
        System.Windows.Automation.AutomationProperties.SetName(password, creating ? "Mật khẩu mới" : "Mật khẩu workspace");
        panel.Children.Add(Text(creating ? "Mật khẩu · ít nhất 8 ký tự" : "Mật khẩu workspace", 12, true)); panel.Children.Add(password);
        var confirmation = new PasswordBox { Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 14), MaxLength = 256 };
        if (creating) { panel.Children.Add(Text("Nhập lại mật khẩu", 12, true)); panel.Children.Add(confirmation); }
        var error = Text("", 12); error.Foreground = Brushes.IndianRed; panel.Children.Add(error);
        var enter = new Button { Content = creating ? "Tạo workspace" : "Mở khóa", IsDefault = true, Style = (Style)FindResource("AccentButton"), HorizontalContentAlignment = HorizontalAlignment.Center };
        enter.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true; enter.IsEnabled = false; error.Text = "";
            try
            {
                if (creating && (password.Password.Length < 8 || password.Password != confirmation.Password || string.IsNullOrWhiteSpace(name.Text))) throw new ArgumentException("Nhập tên, mật khẩu ít nhất 8 ký tự và xác nhận trùng nhau.");
                var secret = password.Password;
                UnlockedVault = await Task.Run(() => creating ? WorkspaceVault.Create(directory, secret) : WorkspaceVault.Open(directory, secret));
                password.Clear(); confirmation.Clear();
                if (creating) { App.Settings.Name = name.Text.Trim(); App.Settings.Onboarded = true; App.SaveSettings(); }
                DialogResult = true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or System.Security.Cryptography.CryptographicException or IOException)
            { error.Text = ex.Message; attempts++; await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempts * 2))); }
            finally { busy = false; enter.IsEnabled = true; }
        };
        panel.Children.Add(enter);
        if (!creating && allowReset)
        {
            var forgot = new Button { Content = "Quên mật khẩu?", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0, 10, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            forgot.Click += async (_, _) => await ResetWorkspace(); panel.Children.Add(forgot);
        }
        panel.Children.Add(Text("Mật khẩu ở trên máy này. Không có khôi phục qua email.", 11, muted: true));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => password.Focus();
    }
    private async Task ResetWorkspace()
    {
        if (busy) return;
        busy = true;
        try
        {
            var header = WorkspaceVault.ReadHeader(directory);
            if (!WorkspaceVault.IsOwner(header)) { MessageBox.Show(this, "Tài khoản Windows hiện tại không phải chủ workspace.", "Không được phép"); return; }
            var dialog = new Window { Owner = this, Title = "Xóa workspace và bắt đầu mới", Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(28) }; panel.Children.Add(Text("Xóa toàn bộ workspace?", 23, true));
            panel.Children.Add(Text("Danh tính, danh bạ, toàn bộ lịch sử và tệp lưu trong workspace trên máy này sẽ bị xóa. Bản chat trên máy người khác không thay đổi. Thao tác không có nút hoàn tác.", 13));
            panel.Children.Add(Text("Nhập đúng: " + ResetPolicy.Phrase(header), 16, true)); var phrase = new TextBox { Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(phrase);
            panel.Children.Add(Text("Bước tiếp theo bắt buộc xác thực Windows Hello của chủ workspace. Không thể bỏ qua bằng nút xác nhận đơn thuần.", 12, muted: true));
            var cancel = new Button { Content = "Giữ nguyên workspace", IsCancel = true, Margin = new Thickness(0, 0, 0, 8) }; panel.Children.Add(cancel);
            var next = new Button { Content = "Xác thực chủ máy để xóa", IsEnabled = false, Style = (Style)FindResource("AccentButton") }; panel.Children.Add(next); phrase.TextChanged += (_, _) => next.IsEnabled = ResetPolicy.CanRequestVerification(header, phrase.Text);
            next.Click += (_, _) => dialog.DialogResult = true; dialog.Content = panel;
            if (dialog.ShowDialog() != true || !ResetPolicy.CanRequestVerification(header, phrase.Text)) return;
            if (!await WindowsOwnerVerifier.VerifyAsync(this, header, "Lantern: xác nhận xóa toàn bộ workspace " + header.WorkspaceId[..8] + " và tạo mới")) return;
            if (!IsVisible) return;
            WindowsOwnerVerifier.DeleteWorkspaceFiles(directory, header.WorkspaceId); App.ResetSettings(); attempts = 0; Build();
        }
        catch (Exception ex) { MessageBox.Show(this, "Chưa hoàn tất xóa workspace: " + ex.Message, "Lantern"); }
        finally { busy = false; }
    }
    private static TextBlock Text(string text, double size = 14, bool bold = false, bool muted = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), LineHeight = size * 1.35 };
        block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); return block;
    }
}

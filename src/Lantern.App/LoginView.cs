using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Lantern.Core;

namespace Lantern.App;

public sealed class LoginView : UserControl
{
    public event Action<WorkspaceVault>? Unlocked;
    private readonly string directory;
    private readonly bool allowReset;
    private int attempts;
    private bool busy;
    public bool IsRestoring { get; private set; }
    private PasswordBox? passwordInput;
    public LoginView(string directory, bool allowReset = true)
    {
        this.directory = directory; this.allowReset = allowReset;
        MaxWidth = 500; HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Stretch;
        Build();
        Loaded += (_, _) => FocusPassword();
        IsVisibleChanged += (_, _) => { if (IsVisible) FocusPassword(); };
    }
    public void FocusPassword() => Dispatcher.BeginInvoke(() =>
    {
        if (!IsVisible || passwordInput is null) return;
        var owner = Window.GetWindow(this);
        FocusManager.SetFocusedElement(owner, passwordInput);
        passwordInput.Focus(); Keyboard.Focus(passwordInput);
    }, DispatcherPriority.ContextIdle);
    private void Build()
    {
        bool creating = !WorkspaceVault.Exists(directory);
        var panel = new StackPanel { Margin = new Thickness(42, 32, 42, 28) };
        var brand = new TextBlock { Text = "LANTERN", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 28) }; brand.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); panel.Children.Add(brand);
        var lockIcon = new TextBlock { Text = "\uE72E", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 31, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; lockIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var badge = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(22), HorizontalAlignment = HorizontalAlignment.Left, Child = lockIcon, Margin = new Thickness(0, 0, 0, 22) }; badge.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); panel.Children.Add(badge);
        var displayName = string.IsNullOrWhiteSpace(App.Settings.Name) ? "bạn" : App.Settings.Name.Trim();
        panel.Children.Add(Text(creating ? "Không gian riêng\nbắt đầu từ đây." : $"Chào mừng\n{displayName} trở lại.", 28, true));
        panel.Children.Add(Text(creating ? "Tạo mật khẩu cho workspace. Mỗi lần mở Lantern đều cần nhập mật khẩu này." : "Nhập mật khẩu để mở các cuộc trò chuyện trên máy này.", 13, muted: true));
        var name = new TextBox { Text = App.Settings.Name, MaxLength = 80, Margin = new Thickness(0, 0, 0, 12) };
        if (creating) { panel.Children.Add(Text("Tên hiển thị", 12, true)); panel.Children.Add(name); }
        var password = new PasswordBox { Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 14), MaxLength = 256 };
        passwordInput = password;
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
                var vault = await Task.Run(() => creating ? WorkspaceVault.Create(directory, secret) : WorkspaceVault.Open(directory, secret));
                password.Clear(); confirmation.Clear();
                if (!IsLoaded) { vault.Dispose(); return; }
                if (creating) { App.Settings.Name = name.Text.Trim(); App.Settings.Onboarded = true; App.SaveSettings(); }
                Unlocked?.Invoke(vault);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or System.Security.Cryptography.CryptographicException or IOException)
            { error.Text = ex.Message; attempts++; await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempts * 2))); }
            finally { busy = false; enter.IsEnabled = true; }
        };
        panel.Children.Add(enter);
        if (creating && allowReset && WorkspaceBackup.CanRestore(directory))
        {
            var restore = new Button { Content = "Khôi phục dữ liệu từ máy cũ", Margin = new Thickness(0, 12, 0, 0) };
            restore.Click += async (_, _) =>
            {
                if (busy) return;
                var file = new Microsoft.Win32.OpenFileDialog { Filter = "Dữ liệu Lantern|*.lanternbackup" };
                if (file.ShowDialog(Window.GetWindow(this)) != true) return;
                var form = new StackPanel(); form.Children.Add(Text("Khôi phục dữ liệu", 22, true));
                form.Children.Add(Text("Nhập mật khẩu file sao lưu. Mật khẩu này sẽ dùng để mở Lantern trên máy mới. Đóng Lantern trên máy cũ sau khi chuyển xong.", 13));
                var secret = new PasswordBox { Padding = new Thickness(12), MaxLength = 256 }; form.Children.Add(secret);
                if (!InAppDialog.Show(Window.GetWindow(this), "Khôi phục dữ liệu", form, "Khôi phục", () => secret.Password.Length >= 12, secret)) return;
                busy = IsRestoring = true; enter.IsEnabled = restore.IsEnabled = false; error.Text = "Đang khôi phục dữ liệu…";
                var backupPassword = secret.Password; secret.Clear();
                try
                {
                    await Task.Run(() => WorkspaceBackup.Restore(file.FileName, backupPassword, directory));
                    App.ReloadSettings(); Build();
                }
                catch (Exception ex) { error.Text = "Chưa khôi phục được. Kiểm tra mật khẩu và file sao lưu. " + ex.Message; }
                finally { busy = IsRestoring = false; enter.IsEnabled = restore.IsEnabled = true; }
            };
            panel.Children.Add(restore);
        }
        if (!creating && allowReset)
        {
            var forgot = new Button { Content = "Quên mật khẩu?", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0, 10, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
            forgot.Click += async (_, _) => await ResetWorkspace(); panel.Children.Add(forgot);
        }
        panel.Children.Add(Text("Mật khẩu ở trên máy này. Không có khôi phục qua email.", 11, muted: true));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        FocusPassword();
    }
    private async Task ResetWorkspace()
    {
        if (busy) return;
        busy = true;
        try
        {
            var owner = Window.GetWindow(this);
            var header = WorkspaceVault.ReadHeader(directory);
            if (!WorkspaceVault.IsOwner(header)) { MessageBox.Show(owner, "Tài khoản Windows hiện tại không phải chủ workspace.", "Không được phép"); return; }
            var panel = new StackPanel { Margin = new Thickness(28) }; panel.Children.Add(Text("Xóa toàn bộ workspace?", 23, true));
            panel.Children.Add(Text("Danh tính, danh bạ, toàn bộ lịch sử và tệp lưu trong workspace trên máy này sẽ bị xóa. Bản chat trên máy người khác không thay đổi. Thao tác không có nút hoàn tác.", 13));
            panel.Children.Add(Text("Nhập đúng: " + ResetPolicy.Phrase(header), 16, true)); var phrase = new TextBox { Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(phrase);
            panel.Children.Add(Text("Bước tiếp theo bắt buộc xác thực Windows Hello của chủ workspace. Không thể bỏ qua bằng nút xác nhận đơn thuần.", 12, muted: true));
            if (!InAppDialog.Show(owner, "Xóa workspace và bắt đầu mới", panel, "Xác thực chủ máy để xóa", () => ResetPolicy.CanRequestVerification(header, phrase.Text), phrase)) return;
            if (!await WindowsOwnerVerifier.VerifyAsync(owner, header, "Lantern: xác nhận xóa toàn bộ workspace " + header.WorkspaceId[..8] + " và tạo mới")) return;
            if (!IsVisible) return;
            WindowsOwnerVerifier.DeleteWorkspaceFiles(directory, header.WorkspaceId); App.ResetSettings(); attempts = 0; Build();
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), "Chưa hoàn tất xóa workspace: " + ex.Message, "Lantern"); }
        finally { busy = false; }
    }
    private static TextBlock Text(string text, double size = 14, bool bold = false, bool muted = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), LineHeight = size * 1.35 };
        block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); return block;
    }
}

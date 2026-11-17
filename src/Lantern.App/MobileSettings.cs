using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.Core;
using QRCoder;

namespace Lantern.App;

public partial class MainWindow
{
    private MobileServer mobile = null!;
    private void InitializeMobile()
    {
        mobile = new MobileServer(node, App.Vault);
        mobile.PcControl = new PcControl(mobile.Registry, new PcDesktop(), () => !Volatile.Read(ref appLocking) && !Volatile.Read(ref closing));
        // Provisioned locally for this Windows user; never included in the published binary.
        var bootstrap = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanternRelay", "relay-bootstrap.bin");
        if (!App.TestMode && !mobile.Registry.Snapshot().Relay.Enabled && File.Exists(bootstrap))
        {
            try
            {
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(bootstrap), null, DataProtectionScope.CurrentUser);
                try { mobile.ConfigureRelay(JsonSerializer.Deserialize<RelayConfiguration>(bytes, MobileProtocol.Json)!); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException or InvalidOperationException) { }
        }
        if (!App.TestMode) mobile.Registry.EnsureAutomaticRelay();
        mobile.ApprovePairing = request => Dispatcher.InvokeAsync(() =>
        {
            if (appLocking || closing) return false;
            var panel = new StackPanel(); panel.Children.Add(Paragraph("Cho phép điện thoại này?", 24, true));
            panel.Children.Add(Paragraph(request.Name, 18, true));
            panel.Children.Add(Paragraph("So sánh mã này với điện thoại trước khi chấp nhận:", 13));
            panel.Children.Add(Paragraph(request.VerificationCode, 20, true));
            panel.Children.Add(Paragraph("Thiết bị sẽ gửi và nhận bằng tài khoản PC khi Lantern đang chạy. Hội thoại có mật khẩu vẫn yêu cầu mở khóa riêng.", 13, muted: true));
            return ShowDialog("Xác nhận ghép đôi điện thoại", panel, "Cho phép thiết bị");
        }).Task;
    }
    private void MobileSettings()
    {
        if (appLocking || closing) return;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Điện thoại của bạn", 25, true));
        panel.Children.Add(Paragraph("PC đang mở là cầu nối. Điện thoại dùng cùng tài khoản, kể cả qua 4G/5G; không cần VPN hay mở cổng router.", 13, muted: true));
        var config = mobile.Registry.Snapshot().Relay;
        var status = Paragraph(config.Enabled && !config.Automatic ? "Internet · Đang dùng relay riêng" : "Kết nối Internet được thiết lập tự động khi ghép đôi", 14, true);
        panel.Children.Add(status);
        panel.Children.Add(Paragraph("Cổng LAN: " + mobile.Port + " · Tối đa 8 thiết bị. PC cần mở Lantern, đã đăng nhập và không sleep.", 12, muted: true));
        var pair = new Button { Content = "＋ Ghép đôi điện thoại bằng QR", Margin = new Thickness(0, 10, 0, 16), Style = (Style)FindResource("AccentButton") };
        var webPair = new Button { Content = "＋ Ghép đôi iPhone · Bản web", Margin = new Thickness(0, 0, 0, 16) };
        var advanced = new Button { Content = "Cấu hình relay riêng…", HorizontalAlignment = HorizontalAlignment.Left };
        using var pending = new CancellationTokenSource(); bool active = true;
        void Busy(bool busy) { pair.IsEnabled = webPair.IsEnabled = advanced.IsEnabled = !busy; if (busy) status.Text = "Đang kết nối và tạo mã QR… Vui lòng chờ."; }
        void PairingError(Exception ex)
        {
            if (!active || closing || appLocking) return;
            status.Text = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests }
                ? "Dịch vụ đang bận. Vui lòng thử ghép đôi lại sau."
                : ex is HttpRequestException or OperationCanceledException
                    ? "Chưa kết nối được dịch vụ Lantern. Kiểm tra Internet rồi bấm ghép đôi để thử lại."
                    : "Không tạo được mã QR. " + ex.Message;
        }
        pair.Click += async (_, _) =>
        {
            Busy(true);
            try
            {
                var payload = await mobile.CreateInvitationAsync(allowLanFallback: true, cancellationToken: pending.Token);
                if (!active || closing || appLocking) { mobile.CancelInvitation(); return; }
                using var invitation = JsonDocument.Parse(payload);
                bool internet = invitation.RootElement.GetProperty("relay").ValueKind != JsonValueKind.Null;
                status.Text = internet ? "Đã kết nối · Sẵn sàng ghép đôi" : "LAN sẵn sàng · Chưa nối được Internet";
                using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M); using var png = new PngByteQRCode(data);
                using var stream = new MemoryStream(png.GetGraphic(7)); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                var content = new StackPanel(); content.Children.Add(Paragraph("Quét bằng Lantern Android / iOS", 23, true));
                if (!internet) content.Children.Add(Paragraph("Mã này dùng khi điện thoại và PC cùng mạng Wi-Fi/LAN. Để dùng qua 4G/5G, kết nối Internet rồi tạo mã mới.", 13, muted: true));
                content.Children.Add(new Image { Source = bitmap, Width = 340, Height = 340, Stretch = Stretch.Uniform });
                content.Children.Add(Paragraph("Mã dùng một lần, hết hạn sau 2 phút. Chỉ quét trên điện thoại của bạn; sau đó xác nhận mã đối chiếu trên cả hai thiết bị.", 13, muted: true));
                ShowDialog("Ghép đôi điện thoại", content, "Đóng mã QR");
            }
            catch (Exception ex) { PairingError(ex); }
            finally { mobile.CancelInvitation(); Busy(false); }
        }; panel.Children.Add(pair);
        webPair.Click += async (_, _) =>
        {
            Busy(true);
            try
            {
                var payload = await mobile.CreateInvitationAsync(web: true, cancellationToken: pending.Token);
                if (!active || closing || appLocking) { mobile.CancelInvitation(); return; }
                config = mobile.Registry.Snapshot().Relay;
                status.Text = "Đã kết nối · Sẵn sàng ghép đôi";
                using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M); using var png = new PngByteQRCode(data);
                using var stream = new MemoryStream(png.GetGraphic(7)); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                var content = new StackPanel(); content.Children.Add(Paragraph("Lantern trên iPhone", 23, true));
                content.Children.Add(Paragraph("Mở địa chỉ này bằng Safari, thêm vào Màn hình chính, rồi mở Lantern và quét QR:", 13, muted: true));
                content.Children.Add(new TextBox { Text = config.Url, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
                content.Children.Add(new Image { Source = bitmap, Width = 320, Height = 320, Stretch = Stretch.Uniform });
                content.Children.Add(Paragraph("Mã dùng một lần, hết hạn sau 2 phút. So sánh mã đối chiếu rồi cho phép trên PC. Trang web mở công khai; không cần tài khoản ChatGPT.", 12, muted: true));
                ShowDialog("Ghép đôi bản web", content, "Đóng mã QR");
            }
            catch (Exception ex) { PairingError(ex); }
            finally { mobile.CancelInvitation(); Busy(false); }
        }; panel.Children.Add(webPair);
        foreach (var device in mobile.Registry.Snapshot().Devices)
        {
            panel.Children.Add(Paragraph(device.Name, 15, true)); panel.Children.Add(Paragraph("Đã ghép đôi " + device.Added.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), 12, muted: true));
            var permissions = new Button { Content = "Quyền ra lệnh và lấy tệp…", Margin = new Thickness(0, 4, 0, 6) };
            permissions.Click += (_, _) => EditPcAccess(device.Id);
            panel.Children.Add(permissions);
            var revoke = new Button { Content = "Gỡ thiết bị", Margin = new Thickness(0, 0, 0, 12) };
            revoke.Click += async (_, _) => { try { await mobile.RevokeAsync(device.Id); revoke.IsEnabled = false; revoke.Content = "Đã thu hồi quyền truy cập"; } catch (Exception ex) { ShowError(ex); } }; panel.Children.Add(revoke);
        }
        advanced.Click += (_, _) =>
        {
            config = mobile.Registry.Snapshot().Relay;
            var form = new StackPanel(); form.Children.Add(Paragraph("Relay HTTPS riêng", 23, true));
            var url = new TextBox { Text = config.Automatic ? "" : config.Url }; var token = new PasswordBox { Password = config.ServiceToken, Padding = new Thickness(12) };
            form.Children.Add(Paragraph("Địa chỉ relay", 12, true)); form.Children.Add(url); form.Children.Add(Paragraph("Khóa truy cập Sites", 12, true)); form.Children.Add(token);
            form.Children.Add(Paragraph("Gỡ các điện thoại trước khi đổi relay, rồi ghép đôi lại. Khóa được mã hóa trong workspace.", 12, muted: true));
            if (ShowDialog("Cấu hình relay", form, "Lưu", initialFocus: url))
            { try { mobile.ConfigureRelay(new() { Url = url.Text.Trim().TrimEnd('/') + "/", ServiceToken = token.Password.Trim() }); config = mobile.Registry.Snapshot().Relay; status.Text = "Internet · Đang dùng relay riêng"; ShowToast("Đã lưu relay."); } catch (Exception ex) { ShowError(ex); } }
            token.Clear();
        }; panel.Children.Add(new Expander { Header = "Nâng cao", Content = advanced, Margin = new Thickness(0, 8, 0, 0) });
        ShowDialog("Thiết bị di động", panel, "Xong");
        active = false; pending.Cancel(); mobile.CancelInvitation();
    }
    private void EditPcAccess(string deviceId)
    {
        var device = mobile.Registry.Snapshot().Devices.Find(d => d.Id == deviceId);
        if (device is null || appLocking || closing) return;
        var panel = new StackPanel();
        panel.Children.Add(Paragraph(device.Name + " · Quyền PC", 23, true));
        panel.Children.Add(Paragraph("Mỗi quyền chỉ áp dụng cho điện thoại này. Lantern phải đang mở khóa. Tệp chỉ được đọc và tải về.", 13, muted: true));
        var power = new CheckBox { Content = "Cho phép khóa Windows và tắt máy", IsChecked = device.Access.Power, Margin = new Thickness(0, 12, 0, 12) };
        var ai = new CheckBox { Content = "Cho phép hiện cửa sổ AI và gửi prompt (thử nghiệm)", IsChecked = device.Access.Ai, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(power); panel.Children.Add(ai);
        panel.Children.Add(Paragraph("AI dùng cuộc trò chuyện đang mở, không tự duyệt xác nhận hoặc ghi đè bản nháp. Windows cần mở khóa; cửa sổ có thể được đưa lên trước.", 12, muted: true));
        var roots = device.Access.Roots.ToList(); var list = new StackPanel(); panel.Children.Add(list);
        void Render()
        {
            list.Children.Clear();
            foreach (var root in roots.ToArray())
            {
                list.Children.Add(Paragraph(root.Name + " · " + root.Path, 12));
                var remove = new Button { Content = "Bỏ quyền thư mục này", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
                remove.Click += (_, _) => { roots.Remove(root); Render(); }; list.Children.Add(remove);
            }
        }
        Render();
        var add = new Button { Content = "＋ Cấp một thư mục / ổ dữ liệu", Margin = new Thickness(0, 10, 0, 10) };
        add.Click += (_, _) =>
        {
            if (roots.Count >= 16) { ShowToast("Tối đa 16 thư mục."); return; }
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Chọn thư mục điện thoại được đọc" };
            if (picker.ShowDialog(this) != true) return;
            if (roots.Any(r => r.Path.Equals(picker.FolderName, StringComparison.OrdinalIgnoreCase))) return;
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(picker.FolderName));
            roots.Add(new(Guid.NewGuid().ToString("N"), name.Length == 0 ? picker.FolderName : name, picker.FolderName)); Render();
        };
        panel.Children.Add(add);
        panel.Children.Add(Paragraph("Không cấp thư mục chứa mật khẩu/khóa bí mật. Liên kết thư mục và tệp hệ thống không được duyệt; mỗi tệp tối đa 100 MB.", 12, muted: true));
        if (ShowDialog("Quyền PC", panel, "Lưu quyền"))
        {
            try { mobile.Registry.SetAccess(deviceId, new() { Power = power.IsChecked == true, Ai = ai.IsChecked == true, Roots = roots }); ShowToast("Đã lưu quyền điện thoại."); }
            catch (Exception ex) { ShowError(ex); }
        }
    }
}

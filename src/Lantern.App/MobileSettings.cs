using System.IO;
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
        panel.Children.Add(Paragraph(config.Enabled ? "Internet · Relay riêng đã cấu hình" : "LAN sẵn sàng · Chưa cấu hình relay Internet", 14, true));
        panel.Children.Add(Paragraph("Cổng LAN: " + mobile.Port + " · Tối đa 8 thiết bị. PC cần mở Lantern, đã đăng nhập và không sleep.", 12, muted: true));
        var pair = new Button { Content = "＋ Ghép đôi điện thoại bằng QR", Margin = new Thickness(0, 10, 0, 16), Style = (Style)FindResource("AccentButton") };
        pair.Click += async (_, _) =>
        {
            pair.IsEnabled = false;
            try
            {
                var payload = await mobile.CreateInvitationAsync();
                if (closing || appLocking) { mobile.CancelInvitation(); return; }
                using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M); using var png = new PngByteQRCode(data);
                using var stream = new MemoryStream(png.GetGraphic(7)); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                var content = new StackPanel(); content.Children.Add(Paragraph("Quét bằng Lantern Android", 23, true));
                content.Children.Add(new Image { Source = bitmap, Width = 340, Height = 340, Stretch = Stretch.Uniform });
                content.Children.Add(Paragraph("Mã dùng một lần, hết hạn sau 2 phút. Chỉ quét trên điện thoại của bạn; sau đó xác nhận mã đối chiếu trên cả hai thiết bị.", 13, muted: true));
                ShowDialog("Ghép đôi điện thoại", content, "Đóng mã QR");
            }
            catch (Exception ex) { ShowError(ex); }
            finally { mobile.CancelInvitation(); pair.IsEnabled = true; }
        }; panel.Children.Add(pair);
        foreach (var device in mobile.Registry.Snapshot().Devices)
        {
            panel.Children.Add(Paragraph(device.Name, 15, true)); panel.Children.Add(Paragraph("Đã ghép đôi " + device.Added.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), 12, muted: true));
            var revoke = new Button { Content = "Gỡ thiết bị", Margin = new Thickness(0, 0, 0, 12) };
            revoke.Click += async (_, _) => { try { await mobile.RevokeAsync(device.Id); revoke.IsEnabled = false; revoke.Content = "Đã thu hồi quyền truy cập"; } catch (Exception ex) { ShowError(ex); } }; panel.Children.Add(revoke);
        }
        var advanced = new Button { Content = "Cấu hình relay riêng…", HorizontalAlignment = HorizontalAlignment.Left };
        advanced.Click += (_, _) =>
        {
            var form = new StackPanel(); form.Children.Add(Paragraph("Relay HTTPS riêng", 23, true));
            var url = new TextBox { Text = config.Url }; var token = new PasswordBox { Password = config.ServiceToken, Padding = new Thickness(12) };
            form.Children.Add(Paragraph("Địa chỉ relay", 12, true)); form.Children.Add(url); form.Children.Add(Paragraph("Khóa truy cập Sites", 12, true)); form.Children.Add(token);
            form.Children.Add(Paragraph("Gỡ các điện thoại trước khi đổi relay, rồi ghép đôi lại. Khóa được mã hóa trong workspace.", 12, muted: true));
            if (ShowDialog("Cấu hình relay", form, "Lưu", initialFocus: url))
            { try { mobile.ConfigureRelay(new() { Url = url.Text.Trim().TrimEnd('/') + "/", ServiceToken = token.Password.Trim() }); ShowToast("Đã lưu relay. Mở lại mục Điện thoại để ghép đôi."); } catch (Exception ex) { ShowError(ex); } }
            token.Clear();
        }; panel.Children.Add(advanced);
        ShowDialog("Thiết bị di động", panel, "Xong");
    }
}

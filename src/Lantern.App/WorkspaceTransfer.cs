using System.Windows;
using System.Windows.Controls;
using Lantern.Core;
using Microsoft.Win32;

namespace Lantern.App;

public partial class MainWindow
{
    private bool exportingWorkspace;
    private async void ExportWorkspaceClick(object sender, RoutedEventArgs e)
    {
        if (exportingWorkspace || appLocking) return;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Xuất dữ liệu chuyển máy", 23, true));
        panel.Children.Add(Paragraph("Đặt mật khẩu ít nhất 12 ký tự để bảo vệ file sao lưu. Sau khi khôi phục trên máy mới, dùng mật khẩu này để mở Lantern. Mật khẩu riêng của từng hội thoại được giữ nguyên.", muted: true));
        var password = new PasswordBox { Padding = new Thickness(12), MaxLength = 256 }; panel.Children.Add(password);
        panel.Children.Add(Paragraph("Nhập lại mật khẩu", bold: true)); var confirm = new PasswordBox { Padding = new Thickness(12), MaxLength = 256 }; panel.Children.Add(confirm);
        panel.Children.Add(Paragraph("File bao gồm tệp đính kèm đã có trên máy; tệp chưa tải hoặc đang tải cần nhận lại. Sau khi chuyển xong, đóng Lantern trên máy cũ để tránh dùng cùng danh tính ở hai nơi.", 12, muted: true));
        if (!ShowDialog("Xuất dữ liệu", panel, "Chọn nơi lưu", () => password.Password.Length >= 12 && password.Password == confirm.Password, password)) return;
        var file = new SaveFileDialog { Filter = "Dữ liệu Lantern|*.lanternbackup", FileName = $"Lantern-{DateTime.Now:yyyyMMdd}.lanternbackup" };
        if (file.ShowDialog(this) != true) return;
        exportingWorkspace = true; var button = (Button)sender; button.IsEnabled = false; var secret = password.Password; password.Clear(); confirm.Clear();
        try
        {
            App.SaveSettings(); ShowToast("Đang xuất dữ liệu, vui lòng giữ ứng dụng mở…");
            await Task.Run(() => WorkspaceBackup.Export(file.FileName, secret, identity, App.Vault, store, uiLifetime.Token));
            ShowToast("Đã xuất dữ liệu. Trên máy mới, chọn Khôi phục dữ liệu ở màn tạo workspace.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex); }
        finally { exportingWorkspace = false; button.IsEnabled = true; }
    }
}

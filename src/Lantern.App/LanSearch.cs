using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lantern.Core;

namespace Lantern.App;

public partial class MainWindow
{
    private void CopyAddressClick(object sender, RoutedEventArgs e)
    {
        var addresses = PeerNode.LocalAddresses();
        if (addresses.Length == 0) { ShowToast("Chưa có địa chỉ IPv4 trong LAN. Hãy kết nối Wi-Fi hoặc Ethernet."); return; }
        var address = addresses[0];
        if (addresses.Length > 1)
        {
            var panel = new StackPanel(); panel.Children.Add(Paragraph("Chọn IP để chia sẻ", 22, true));
            panel.Children.Add(Paragraph("Chọn mạng mà bạn bè có thể kết nối tới.", muted: true));
            var choices = new ComboBox { ItemsSource = addresses, SelectedIndex = 0 }; panel.Children.Add(choices);
            if (!ShowDialog("Sao chép IP của tôi", panel, "Sao chép")) return;
            address = (string)choices.SelectedItem;
        }
        try { Clipboard.SetText($"{address}:{node.Port}"); ShowToast($"Đã sao chép {address}:{node.Port}."); } catch (Exception ex) { ShowError(ex); }
    }

    private void DiscoverClick(object sender, RoutedEventArgs e)
    {
        if (node is null || appLocking) return;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Tìm máy trong LAN", 24, true));
        panel.Children.Add(Paragraph("Chỉ cần nhập 10.10 để quét lần lượt 10.10.0.x đến 10.10.255.x; ưu tiên dải máy đang dùng. Nhập 10.10.14 để chỉ quét một dải. Mở khóa Lantern trên máy cần tìm.", muted: true));
        var ranges = new TextBox { Text = string.Join(", ", PeerNode.DiscoveryRanges()), MaxLength = 2048 }; panel.Children.Add(ranges);
        panel.Children.Add(Paragraph("Có thể nhập IP đầy đủ, CIDR hoặc nhiều tiền tố cách nhau bằng dấu phẩy. Quét toàn bộ 10.10 có thể mất hơn 10 phút; kết quả hiện ngay khi tìm thấy. Router/firewall cần cho phép kết nối giữa các dải.", 12, muted: true));
        var controls = new WrapPanel(); var scan = new Button { Content = "Quét lại" }; var stop = new Button { Content = "Dừng quét", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) }; controls.Children.Add(scan); controls.Children.Add(stop); panel.Children.Add(controls);
        var status = Paragraph("Đang chuẩn bị quét…", 13); panel.Children.Add(status);
        var bar = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 4, 0, 12) }; panel.Children.Add(bar);
        var results = new StackPanel(); panel.Children.Add(results);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(uiLifetime.Token);
        CancellationTokenSource? search = null; bool running = false; DateTimeOffset started = DateTimeOffset.UtcNow;
        var seen = new HashSet<string>();
        void Render()
        {
            foreach (var peer in node.Peers.Where(p => !p.Blocked && (p.Connected || p.Seen >= started)).OrderBy(p => p.DisplayName))
            {
                if (!seen.Add(peer.Id)) continue;
                var button = new Button { Content = $"{peer.DisplayName} · {peer.Address}:{peer.Port}" + (peer.Trusted ? " · Đã xác minh" : " · Chưa xác minh"), Margin = new Thickness(0, 3, 0, 3) };
                button.Click += async (_, _) =>
                {
                    button.IsEnabled = false;
                    try { await node.ConnectAsync(peer.Address, peer.Port, peer.Id); status.Text = "Đã kết nối. Đóng màn quét và chọn hội thoại để xác minh."; ContactSearch.Clear(); Refresh(); ContactsList.SelectedItem = ContactsList.Items.OfType<ContactRow>().FirstOrDefault(r => r.Id == peer.Id); }
                    catch (Exception ex) { status.Text = ex.Message; }
                    finally { button.IsEnabled = true; }
                };
                results.Children.Add(button);
            }
        }
        async Task Scan()
        {
            if (running || lifetime.IsCancellationRequested) return;
            running = true; scan.IsEnabled = ranges.IsEnabled = false; stop.IsEnabled = true;
            search = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); started = DateTimeOffset.UtcNow; seen.Clear(); results.Children.Clear(); bar.Value = 0;
            var progress = new Progress<DiscoveryProgress>(p => { if (lifetime.IsCancellationRequested || !running) return; bar.Maximum = Math.Max(1, p.Total); bar.Value = Math.Max(bar.Value, p.Completed); Render(); status.Text = $"Đang quét {p.Range}\nĐã dò {p.Completed:N0}/{p.Total:N0} địa chỉ · Tìm thấy {seen.Count} máy"; });
            try { await node.DiscoverAsync(ranges.Text, progress, search.Token); Render(); status.Text = seen.Count > 0 ? $"Hoàn tất · Tìm thấy {seen.Count} máy. Chọn máy để kết nối." : "Chưa tìm thấy máy. Kiểm tra dải mạng, Lantern trên máy kia và quyền kết nối giữa các mạng."; }
            catch (OperationCanceledException) { status.Text = $"Đã dừng · Tìm thấy {seen.Count} máy."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { running = false; scan.IsEnabled = ranges.IsEnabled = true; stop.IsEnabled = false; search.Dispose(); search = null; Refresh(); }
        }
        scan.Click += async (_, _) => await Scan(); stop.Click += (_, _) => search?.Cancel();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; timer.Tick += (_, _) => Render(); timer.Start();
        panel.Loaded += async (_, _) => await Scan();
        try { ShowDialog("Tìm máy trong LAN", panel, "Đóng", width: 700); }
        finally { timer.Stop(); lifetime.Cancel(); }
    }
}

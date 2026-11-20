using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lantern.App;
using Lantern.Core;
using static Lantern.App.App;

internal sealed partial class UiHarness
{
    private async Task RunNetworkBackupUi(string? output)
    {
        using var watchdog = new System.Threading.Timer(_ => { Console.Error.WriteLine("Network/backup UI fixture timed out."); Environment.Exit(1); }, null, TimeSpan.FromMinutes(2), System.Threading.Timeout.InfiniteTimeSpan);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = Path.GetFullPath(output ?? Path.Combine(Path.GetTempPath(), "Lantern-network-ui-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        var workspace = Path.Combine(root, "workspace");
        SetApp("DataDirectory", workspace); SetApp("TestMode", true); SetApp("Settings", new AppSettings { Name = "LAN QA", Onboarded = true, Theme = "light", AutoUpdates = false, MessageToasts = false });
        SetApp("Vault", WorkspaceVault.Create(workspace, "synthetic QA password only")); ApplyTheme("light");
        using (var seed = new LocalStore(workspace))
            foreach (var c in new[] { 'a', 'b' }) seed.SavePeer(new Peer { Id = new string(c, 64), Name = "Peer " + c, Address = "127.0.0.1", Trusted = true });
        MainWindow? window = null;
        try
        {
            window = new MainWindow { Width = 1300 }; MainWindow = window; window.Show();
            await Until(() => Field<PeerNode>(window, "node").Port > 0, "QA window starts");
            Check(((ColumnDefinition)window.FindName("DetailsColumn")).Width.Value == 0, "Details sidebar starts hidden on a wide window");
            Call(window, "SetContactSection", "friends"); var contacts = (ListBox)window.FindName("ContactsList"); contacts.SelectedIndex = 0;
            Check(((ColumnDefinition)window.FindName("DetailsColumn")).Width.Value == 0, "Selecting a conversation keeps details hidden");
            void CheckDetailsOpen()
            {
                if (window.ActualWidth >= 1150)
                {
                    Call(window, "DetailsClick", window, new RoutedEventArgs());
                    Check(((ColumnDefinition)window.FindName("DetailsColumn")).Width.Value > 0, "Information button explicitly opens details");
                    return;
                }
                // Hosted runners can constrain even a requested 1300px window to
                // a smaller desktop. The compact information view is modal.
                bool opened = false;
                _ = Dispatcher.InvokeAsync(() =>
                {
                    var dialog = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "Thông tin hội thoại");
                    opened = dialog.IsVisible; dialog.Close();
                });
                Call(window, "DetailsClick", window, new RoutedEventArgs());
                Check(opened, "Information button opens and closes the compact details dialog");
            }
            CheckDetailsOpen();
            window.Width = 900; window.UpdateLayout(); CheckDetailsOpen();
            contacts.SelectedIndex = 1;
            Check(((ColumnDefinition)window.FindName("DetailsColumn")).Width.Value == 0, "Switching conversations closes details again");
            Check(Visuals<Button>(window).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Sao chép IP của tôi"), "Copy IP button is present beside manual connection");
            Snapshot(window, Path.Combine(root, "chat-sidebar-hidden.png"));
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(250);
                try
                {
                    Check(Visuals<Button>(window).Any(b => Equals(b.Content, "Quét lại")) && Visuals<Button>(window).Any(b => Equals(b.Content, "Dừng quét")), "LAN dialog exposes scan and cancellation controls");
                    Check(Visuals<TextBox>(window).Any(b => b.MaxLength == 2048), "LAN dialog accepts routed network ranges");
                    Snapshot(window, Path.Combine(root, "lan-search.png"));
                }
                finally { InAppDialog.CancelAll(window); }
            });
            Call(window, "DiscoverClick", window, new RoutedEventArgs());
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(250);
                try { Check(Visuals<Button>(window).Any(b => Equals(b.Content, "Xuất toàn bộ dữ liệu để chuyển máy")), "Settings includes full data export"); Snapshot(window, Path.Combine(root, "backup-settings.png")); }
                finally { InAppDialog.CancelAll(window); }
            });
            Call(window, "SettingsClick", window, new RoutedEventArgs());
            var fresh = new LoginView(Path.Combine(root, "fresh-profile")); var host = new Window { Content = fresh, Width = 600, Height = 800 }; host.Show(); host.UpdateLayout();
            Check(Visuals<Button>(fresh).Any(b => Equals(b.Content, "Khôi phục dữ liệu từ máy cũ")), "New-machine login offers backup restore"); Snapshot(host, Path.Combine(root, "restore-login.png")); host.Close();
            Console.WriteLine($"{passed} UI checks passed. Screenshots: {root}");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally { if (window is not null) { Call(window, "ExitApplication"); await Task.Delay(700); } Vault.Dispose(); Shutdown(Environment.ExitCode); }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Lantern.App;
using Lantern.Core;
using static Lantern.App.App;

internal sealed partial class UiHarness
{
    private async Task RunComposerMenus()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = Path.Combine(Path.GetTempPath(), "Lantern-menu-qa-" + Guid.NewGuid().ToString("N"));
        SetApp("DataDirectory", root); SetApp("TestMode", true); SetApp("Settings", new AppSettings { Name = "QA", Onboarded = true, AutoUpdates = false, MessageToasts = false });
        SetApp("Vault", WorkspaceVault.Create(root, "synthetic password")); ApplyTheme("light"); MainWindow? window = null;
        try
        {
            string peer = new('a', 64); using (var seed = new LocalStore(root, Vault)) seed.SavePeer(new Peer { Id = peer, Name = "Synthetic peer", Trusted = true });
            window = new MainWindow { ShowInTaskbar = false }; window.Show(); await Until(() => Field<PeerNode>(window, "node").Port > 0, "Fixture startup");
            Call(window, "Refresh"); var list = (ListBox)window.FindName("ContactsList"); Call(window, "ShowContact", peer); await Task.Delay(100); ActivateFixture(window);
            var composer = (RichComposer)window.FindName("Composer"); composer.Text = "Nội dung kiểm thử"; Call(window, "FormatClick", window, new RoutedEventArgs());
            var controls = Field<Dictionary<string, Button>>(window, "formatControls");
            foreach (var key in new[] { "size", "color" })
            {
                composer.Focus(); composer.SelectAll(); controls[key].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(120); Call(window, "Refresh"); await Task.Delay(120);
                Check(controls[key].ContextMenu.IsOpen, key + " menu remains open after click and background refresh");
                var item = controls[key].ContextMenu.Items.OfType<MenuItem>().ElementAt(key == "size" ? 2 : 1); item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); controls[key].ContextMenu.IsOpen = false;
                Check(key == "size" ? Equals(composer.Selection.GetPropertyValue(TextElement.FontSizeProperty), 18d) : (composer.Selection.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color == (Color)ColorConverter.ConvertFromString("#D64242"), key + " choice applies to the original selection");
            }
            Call(window, "EmojiClick", window, new RoutedEventArgs()); await Task.Delay(150); Call(window, "Refresh");
            var popup = Field<Popup>(window, "emojiPopup"); Check(popup.IsOpen, "Emoji picker survives refresh and stays interactive"); popup.IsOpen = false;
            Console.WriteLine($"{passed} composer menu checks passed.");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally { if (window is not null) { Call(window, "WindowClosing", window, new System.ComponentModel.CancelEventArgs()); await Until(() => Field<bool>(window, "closeReady"), "Fixture cleanup"); } Vault.Dispose(); Shutdown(Environment.ExitCode); }
    }
}

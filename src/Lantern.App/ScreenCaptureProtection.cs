using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Lantern.App;

internal static class ScreenCaptureProtection
{
    internal const uint ExcludeFromCapture = 0x11;
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    internal static bool Set(Window window, bool protect)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return !protect;
        uint requested = protect ? ExcludeFromCapture : 0;
        if (GetWindowDisplayAffinity(handle, out var current) && current == requested) return true;
        return SetWindowDisplayAffinity(handle, requested);
    }
    internal static bool IsProtected(Window window) => GetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, out var affinity) && affinity == ExcludeFromCapture;
    internal static bool ProtectPopup(Visual child, Window owner)
    {
        if (!IsProtected(owner)) return true;
        return PresentationSource.FromVisual(child) is HwndSource source && SetWindowDisplayAffinity(source.Handle, ExcludeFromCapture);
    }
}

public partial class MainWindow
{
    private bool captureUnavailable;
    private bool UpdateCaptureProtection()
    {
        bool protect = node is not null && selectedId.Length > 0 && node.ConversationRules(selectedId).CaptureProtected;
        var success = ScreenCaptureProtection.Set(this, protect);
        foreach (var window in OwnedWindows.Cast<Window>().ToArray())
            if (!ScreenCaptureProtection.Set(window, protect) && protect) window.Close();
        captureUnavailable = protect && !success;
        if (captureUnavailable)
        {
            ClearPinnedMessages();
            CloseMediaViews(); MessagesPanel.Children.Clear(); ClearSharedContent(); DetailsContent.Visibility = Visibility.Collapsed; InfoTabs.IsEnabled = false; Composer.Clear();
            Composer.IsEnabled = SendButton.IsEnabled = AttachButton.IsEnabled = EmojiButton.IsEnabled = false;
            WelcomePanel.Visibility = Visibility.Visible; WelcomeTitle.Text = "Không thể mở log\nđược bảo vệ.";
            WelcomeBody.Text = "Windows chưa bật được chống chụp cho cửa sổ này. Nội dung được giữ ẩn."; WelcomeAction.Visibility = Visibility.Collapsed;
            renderKey = "capture-unavailable:" + selectedId;
        }
        return !captureUnavailable;
    }
    private void ProtectOwnedWindow(Window window, string conversation)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (node.ConversationRules(conversation).CaptureProtected && !ScreenCaptureProtection.Set(window, true))
            { window.Content = null; window.Dispatcher.BeginInvoke(() => { window.Close(); ShowToast("Không mở được nội dung vì Windows chưa bật được chống chụp."); }); }
        };
    }
}

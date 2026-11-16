using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.Core;

namespace Lantern.App;

internal sealed class MediaThumbnail : Button
{
    private static readonly SemaphoreSlim Loaders = new(2, 2);
    private bool loading;
    internal MediaThumbnail(ChatMessage message, Action open, CancellationToken ct)
    {
        SetResourceReference(StyleProperty, typeof(Button));
        Width = Height = 78; MinHeight = 0; Padding = new Thickness(0); Margin = new Thickness(0, 0, 7, 7); BorderThickness = new Thickness(0); ToolTip = message.FileName;
        AutomationProperties.SetName(this, (message.Kind == "video" ? "Video: " : "Ảnh: ") + message.FileName);
        var grid = new Grid { Width = 78, Height = 78, Clip = new RectangleGeometry(new Rect(0, 0, 78, 78), 9, 9) }; grid.SetResourceReference(BackgroundProperty, "SoftBrush"); Content = grid;
        var fallback = new TextBlock { Text = message.Kind == "video" ? "\uE714" : "\uE91B", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 27, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; fallback.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); grid.Children.Add(fallback);
        var image = new Image { Stretch = Stretch.UniformToFill }; grid.Children.Add(image);
        if (message.Kind == "video" || !File.Exists(message.FilePath))
        {
            var badge = new Border { Background = new SolidColorBrush(Color.FromArgb(165, 10, 22, 30)), CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5), Padding = new Thickness(5, 2, 5, 2), Child = new TextBlock { Text = message.Kind == "video" ? "▶" : "↓", Foreground = Brushes.White, FontSize = 11 } }; grid.Children.Add(badge);
        }
        Click += (_, _) => open();
        async void Load()
        {
            if (loading || !IsVisible || !File.Exists(message.FilePath) || ct.IsCancellationRequested) return;
            loading = true;
            try
            {
                await Loaders.WaitAsync(ct);
                BitmapSource? bitmap;
                try { bitmap = await Task.Run(() => message.Kind == "image" ? ClipboardImages.LoadLocal(message.FilePath, 180) : ShellThumbnail.Load(message.FilePath), ct); }
                finally { Loaders.Release(); }
                if (ct.IsCancellationRequested) return;
                image.Source = bitmap; if (bitmap is not null) fallback.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or COMException or OperationCanceledException or UnauthorizedAccessException or ArgumentException) { }
        }
        Loaded += (_, _) => Load(); IsVisibleChanged += (_, _) => Load();
    }
}

internal static class ShellThumbnail
{
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width; public int Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory { void GetImage(Size size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out IImageFactory item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
    internal static BitmapSource? Load(string path)
    {
        IImageFactory? factory = null; IntPtr handle = IntPtr.Zero;
        try
        {
            var id = typeof(IImageFactory).GUID; SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out factory);
            factory.GetImage(new Size { Width = 180, Height = 180 }, 0x28, out handle);
            var bitmap = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bitmap.Freeze(); return bitmap;
        }
        finally { if (handle != IntPtr.Zero) DeleteObject(handle); if (factory is not null) Marshal.ReleaseComObject(factory); }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.Core;
using Microsoft.Win32;

namespace Lantern.App;

internal sealed class AvatarEditor : StackPanel
{
    private BitmapSource source;
    private readonly Canvas viewport = new() { Width = 300, Height = 300, ClipToBounds = true, Background = Brushes.Black, Focusable = true, Cursor = Cursors.SizeAll };
    private readonly Image preview = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Slider zoom = new() { Minimum = 1, Maximum = 4, Value = 1, TickFrequency = .1, Margin = new Thickness(0, 12, 0, 8) };
    private double centerX, centerY;
    private Point? pointer;
    private double Scale => 300d / Math.Min(source.PixelWidth, source.PixelHeight) * zoom.Value;
    internal AvatarEditor(BitmapSource bitmap)
    {
        source = bitmap; HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(new TextBlock { Text = "Cắt ảnh đại diện · 1:1", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        viewport.Children.Add(preview); Children.Add(new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Child = viewport });
        var guide = new Grid { Width = 300, Height = 300, IsHitTestVisible = false };
        for (int i = 1; i < 3; i++)
        {
            guide.Children.Add(new System.Windows.Shapes.Line { X1 = i * 100, X2 = i * 100, Y2 = 300, Stroke = Brushes.White, Opacity = .35 });
            guide.Children.Add(new System.Windows.Shapes.Line { Y1 = i * 100, Y2 = i * 100, X2 = 300, Stroke = Brushes.White, Opacity = .35 });
        }
        viewport.Children.Add(guide);
        Children.Add(new TextBlock { Text = "Kéo ảnh để chọn vùng cắt. Cuộn chuột hoặc kéo thanh để phóng to.", MaxWidth = 300, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        System.Windows.Automation.AutomationProperties.SetName(zoom, "Phóng to ảnh đại diện"); Children.Add(zoom);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var rotate = new Button { Content = "Xoay 90°", Margin = new Thickness(0, 0, 10, 0) }; rotate.Click += (_, _) => { source = new TransformedBitmap(source, new RotateTransform(90)); source.Freeze(); Reset(); }; buttons.Children.Add(rotate);
        var reset = new Button { Content = "Căn giữa" }; reset.Click += (_, _) => Reset(); buttons.Children.Add(reset); Children.Add(buttons);
        zoom.ValueChanged += (_, _) => Render();
        viewport.MouseLeftButtonDown += (_, e) => { pointer = e.GetPosition(viewport); viewport.Focus(); viewport.CaptureMouse(); e.Handled = true; };
        viewport.MouseMove += (_, e) => { if (pointer is not { } old) return; var current = e.GetPosition(viewport); centerX -= (current.X - old.X) / Scale; centerY -= (current.Y - old.Y) / Scale; pointer = current; Render(); };
        viewport.MouseLeftButtonUp += (_, _) => { pointer = null; viewport.ReleaseMouseCapture(); };
        viewport.LostMouseCapture += (_, _) => pointer = null;
        viewport.MouseWheel += (_, e) => { zoom.Value = Math.Clamp(zoom.Value + Math.Sign(e.Delta) * .15, 1, 4); e.Handled = true; };
        viewport.PreviewKeyDown += (_, e) => { double move = 10 / Scale; switch (e.Key) { case Key.Left: centerX -= move; break; case Key.Right: centerX += move; break; case Key.Up: centerY -= move; break; case Key.Down: centerY += move; break; default: return; } Render(); e.Handled = true; };
        Reset();
    }
    private void Reset() { centerX = source.PixelWidth / 2d; centerY = source.PixelHeight / 2d; zoom.Value = 1; Render(); }
    private void Render()
    {
        double half = 150 / Scale;
        centerX = Math.Clamp(centerX, half, source.PixelWidth - half); centerY = Math.Clamp(centerY, half, source.PixelHeight - half);
        preview.Source = source; preview.Width = source.PixelWidth * Scale; preview.Height = source.PixelHeight * Scale;
        Canvas.SetLeft(preview, 150 - centerX * Scale); Canvas.SetTop(preview, 150 - centerY * Scale);
    }
    internal string Export()
    {
        int side = Math.Clamp((int)Math.Round(300 / Scale), 1, Math.Min(source.PixelWidth, source.PixelHeight));
        int x = Math.Clamp((int)Math.Round(centerX - side / 2d), 0, source.PixelWidth - side), y = Math.Clamp((int)Math.Round(centerY - side / 2d), 0, source.PixelHeight - side);
        var crop = new CroppedBitmap(source, new Int32Rect(x, y, side, side));
        var visual = new DrawingVisual(); using (var draw = visual.RenderOpen()) draw.DrawImage(crop, new Rect(0, 0, ProfileAvatar.Size, ProfileAvatar.Size));
        var output = new RenderTargetBitmap(ProfileAvatar.Size, ProfileAvatar.Size, 96, 96, PixelFormats.Pbgra32); output.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(output)); using var stream = new MemoryStream(); encoder.Save(stream);
        var data = Convert.ToBase64String(stream.ToArray()); ProfileAvatar.Validate(data); return data;
    }
    internal static BitmapSource Load(string path)
    {
        if (new FileInfo(path).Length > 30 * 1024 * 1024) throw new IOException("Chọn ảnh nhỏ hơn 30 MB.");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        int width = decoder.Frames[0].PixelWidth, height = decoder.Frames[0].PixelHeight;
        if (width < 1 || height < 1 || (long)width * height > 100_000_000) throw new IOException("Kích thước ảnh vượt giới hạn.");
        stream.Position = 0; var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream;
        if (width >= height) bitmap.DecodePixelWidth = Math.Min(2048, width); else bitmap.DecodePixelHeight = Math.Min(2048, height);
        bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
}

internal static class AvatarImages
{
    private static readonly Dictionary<string, BitmapSource?> cache = [];
    internal static BitmapSource? Get(string data)
    {
        if (string.IsNullOrEmpty(data)) return null;
        if (cache.TryGetValue(data, out var found)) return found;
        BitmapSource? bitmap = null;
        try { ProfileAvatar.Validate(data); using var stream = new MemoryStream(Convert.FromBase64String(data)); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.DecodePixelWidth = ProfileAvatar.Size; image.EndInit(); image.Freeze(); bitmap = image; } catch (Exception ex) when (ex is not OutOfMemoryException) { }
        if (cache.Count >= 200) cache.Clear(); cache[data] = bitmap; return bitmap;
    }
}

public partial class MainWindow
{
    private FrameworkElement AvatarSettings(Func<string> get, Action<string> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 16) };
        var image = new Image { Width = 72, Height = 72, Source = AvatarImages.Get(get()), Margin = new Thickness(0, 0, 14, 0) }; row.Children.Add(image);
        var choose = new Button { Content = "Chọn ảnh đại diện", VerticalAlignment = VerticalAlignment.Center };
        choose.Click += (_, _) => {
            var file = new OpenFileDialog { Filter = "Ảnh|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff", Title = "Chọn ảnh đại diện" }; if (file.ShowDialog(this) != true) return;
            try { var editor = new AvatarEditor(AvatarEditor.Load(file.FileName)); if (ShowDialog("Cắt ảnh đại diện", editor, "Dùng ảnh này", width: 410)) { var data = editor.Export(); set(data); image.Source = AvatarImages.Get(data); } } catch (Exception ex) { ShowError(ex); }
        }; row.Children.Add(choose);
        var remove = new Button { Content = "Gỡ ảnh", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; remove.Click += (_, _) => { set(""); image.Source = null; }; row.Children.Add(remove);
        return row;
    }
}

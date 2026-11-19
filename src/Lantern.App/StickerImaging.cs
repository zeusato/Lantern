using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lantern.Core;

namespace Lantern.App;

internal sealed class StickerImage : Image
{
    private readonly string path;
    private readonly bool hoverOnly;
    private readonly DispatcherTimer timer = new();
    private List<(BitmapSource Bitmap, int Delay)>? frames;
    private ScrollViewer? viewport;
    private int index;
    internal StickerImage(string path, double size, bool hoverOnly = false)
    {
        this.path = path; this.hoverOnly = hoverOnly; Width = size; Height = size; Stretch = Stretch.Uniform;
        Loaded += (_, _) =>
        {
            try
            {
                _ = StickerLibrary.ReadImage(path); Source = ClipboardImages.LoadLocal(path, Math.Min(512, (int)Math.Ceiling(Width * 1.5)));
                if (viewport is null) for (DependencyObject? p = VisualTreeHelper.GetParent(this); p is not null; p = VisualTreeHelper.GetParent(p)) if (p is ScrollViewer scroll) { viewport = scroll; scroll.ScrollChanged += Scrolled; break; }
                Play();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { ToolTip = "Không đọc được sticker: " + ex.Message; }
        };
        Unloaded += (_, _) => { timer.Stop(); frames = null; index = 0; Source = null; if (viewport is not null) viewport.ScrollChanged -= Scrolled; viewport = null; };
        IsVisibleChanged += (_, _) => Play(); MouseEnter += (_, _) => Play(); MouseLeave += (_, _) => Play();
        SizeChanged += (_, _) => Play();
        timer.Tick += (_, _) => { if (frames is not { Count: > 1 }) return; index = (index + 1) % frames.Count; Source = frames[index].Bitmap; timer.Interval = TimeSpan.FromMilliseconds(frames[index].Delay); };
    }
    private void Scrolled(object sender, ScrollChangedEventArgs e) => Play();
    private bool InViewport()
    {
        if (viewport is null) return true;
        try { return TransformToAncestor(viewport).TransformBounds(new Rect(RenderSize)).IntersectsWith(new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight)); }
        catch (InvalidOperationException) { return false; }
    }
    private void Play()
    {
        timer.Stop();
        if (!InViewport()) { frames = null; index = 0; return; }
        if (!IsLoaded || !IsVisible || hoverOnly && !IsMouseOver || !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            frames ??= DecodeGif(StickerLibrary.ReadImage(path));
            if (frames.Count < 2) return;
            Source = frames[index].Bitmap; timer.Interval = TimeSpan.FromMilliseconds(frames[index].Delay); timer.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ToolTip = "Không phát được ảnh động: " + ex.Message; }
    }
    internal static List<(BitmapSource Bitmap, int Delay)> DecodeGif(byte[] bytes)
    {
        StickerLibrary.ValidateImage(bytes);
        using var input = new MemoryStream(bytes);
        using var gif = System.Drawing.Image.FromStream(input, false, true);
        var dimension = System.Drawing.Imaging.FrameDimension.Time; int count = gif.GetFrameCount(dimension);
        if (count > 300 || (long)gif.Width * gif.Height * count > 16_000_000) throw new InvalidDataException("Ảnh động quá lớn (tối đa 300 khung / 16 triệu pixel).");
        var delays = gif.PropertyIdList.Contains(0x5100) ? gif.GetPropertyItem(0x5100)?.Value : null;
        var result = new List<(BitmapSource, int)>();
        for (int i = 0; i < count; i++)
        {
            gif.SelectActiveFrame(dimension, i);
            using var rendered = new System.Drawing.Bitmap(gif.Width, gif.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var draw = System.Drawing.Graphics.FromImage(rendered)) { draw.Clear(System.Drawing.Color.Transparent); draw.DrawImage(gif, 0, 0, gif.Width, gif.Height); }
            using var png = new MemoryStream(); rendered.Save(png, System.Drawing.Imaging.ImageFormat.Png); png.Position = 0;
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = png; bitmap.EndInit(); bitmap.Freeze();
            int delay = delays?.Length >= (i + 1) * 4 ? (int)Math.Clamp((long)BitConverter.ToInt32(delays, i * 4) * 10, 20, 10_000) : 100;
            result.Add((bitmap, delay));
        }
        return result;
    }
    internal static byte[] ValidateFile(string path)
    {
        var bytes = StickerLibrary.ReadImage(path);
        if (StickerLibrary.ValidateImage(bytes) == ".gif") _ = DecodeGif(bytes);
        else { using var stream = new MemoryStream(bytes); var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); _ = decoder.Frames[0].PixelWidth; }
        return bytes;
    }
}

internal static class StickerImaging
{
    internal static BitmapSource Fit(BitmapSource image, int size = 480)
    {
        double scale = Math.Min(1, (double)size / Math.Max(image.PixelWidth, image.PixelHeight));
        var visual = new DrawingVisual(); using (var draw = visual.RenderOpen()) draw.DrawImage(image, new Rect(0, 0, Math.Max(1, (int)(image.PixelWidth * scale)), Math.Max(1, (int)(image.PixelHeight * scale))));
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)(image.PixelWidth * scale)), Math.Max(1, (int)(image.PixelHeight * scale)), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; converted.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes;
    }
    private static BitmapSource Bitmap(byte[] bytes, int w, int h) { var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bytes, w * 4); bitmap.Freeze(); return bitmap; }
    internal static BitmapSource RemoveEdgeBackground(BitmapSource source, int tolerance)
    {
        int w = source.PixelWidth, h = source.PixelHeight; var pixels = Pixels(source); var visited = new bool[w * h]; var queue = new Queue<int>();
        // Flood only regions connected to a corner, retaining enclosed areas of the subject.
        foreach (var corner in new[] { 0, w - 1, (h - 1) * w, w * h - 1 })
        {
            var color = pixels.AsSpan(corner * 4, 4).ToArray();
            void Visit(int p)
            {
                if (visited[p]) return;
                int at = p * 4;
                if (pixels[at + 3] > 0 && (Math.Abs(pixels[at] - color[0]) > tolerance || Math.Abs(pixels[at + 1] - color[1]) > tolerance || Math.Abs(pixels[at + 2] - color[2]) > tolerance)) return;
                visited[p] = true; queue.Enqueue(p);
            }
            Visit(corner);
            while (queue.TryDequeue(out int p)) { pixels[p * 4 + 3] = 0; int x = p % w, y = p / w; if (x > 0) Visit(p - 1); if (x + 1 < w) Visit(p + 1); if (y > 0) Visit(p - w); if (y + 1 < h) Visit(p + w); }
        }
        return Bitmap(pixels, w, h);
    }
    internal static BitmapSource Select(BitmapSource image, IReadOnlyList<Point> points)
    {
        if (points.Count < 3) throw new InvalidDataException("Vẽ vùng chọn kín quanh chủ thể.");
        var geometry = new StreamGeometry(); using (var context = geometry.Open()) { context.BeginFigure(points[0], true, true); context.PolyLineTo(points.Skip(1).ToArray(), true, true); }
        var visual = new DrawingVisual(); using (var draw = visual.RenderOpen()) { draw.PushClip(geometry); draw.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight)); draw.Pop(); }
        var output = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32); output.Render(visual); output.Freeze(); return output;
    }
    internal static BitmapSource OutlineAndTrim(BitmapSource image, int radius)
    {
        radius = Math.Clamp(radius, 0, 16); var source = Pixels(image); int sw = image.PixelWidth, sh = image.PixelHeight;
        int left = sw, top = sh, right = -1, bottom = -1;
        for (int y = 0; y < sh; y++) for (int x = 0; x < sw; x++) if (source[(y * sw + x) * 4 + 3] > 0) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        if (right < left) throw new InvalidDataException("Vùng chọn trống. Hãy chọn lại hoặc khôi phục ảnh.");
        int pad = radius + 1, w = right - left + 1 + pad * 2, h = bottom - top + 1 + pad * 2;
        var output = new byte[w * h * 4]; var distance = Enumerable.Repeat(1e6f, w * h).ToArray();
        for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
        { int dst = (y - top + pad) * w + x - left + pad, src = (y * sw + x) * 4; source.AsSpan(src, 4).CopyTo(output.AsSpan(dst * 4, 4)); if (source[src + 3] > 0) distance[dst] = 0; }
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int p = y * w + x; if (x > 0) distance[p] = Math.Min(distance[p], distance[p - 1] + 1); if (y > 0) { distance[p] = Math.Min(distance[p], distance[p - w] + 1); if (x > 0) distance[p] = Math.Min(distance[p], distance[p - w - 1] + 1.414f); if (x + 1 < w) distance[p] = Math.Min(distance[p], distance[p - w + 1] + 1.414f); } }
        for (int y = h - 1; y >= 0; y--) for (int x = w - 1; x >= 0; x--) { int p = y * w + x; if (x + 1 < w) distance[p] = Math.Min(distance[p], distance[p + 1] + 1); if (y + 1 < h) { distance[p] = Math.Min(distance[p], distance[p + w] + 1); if (x > 0) distance[p] = Math.Min(distance[p], distance[p + w - 1] + 1.414f); if (x + 1 < w) distance[p] = Math.Min(distance[p], distance[p + w + 1] + 1.414f); } }
        if (radius > 0) for (int p = 0; p < distance.Length; p++)
        {
            float border = Math.Clamp(radius + 1 - distance[p], 0, 1); if (border <= 0) continue;
            int at = p * 4; float alpha = output[at + 3] / 255f, combined = alpha + border * (1 - alpha);
            for (int c = 0; c < 3; c++) output[at + c] = (byte)Math.Clamp((output[at + c] * alpha + 255 * border * (1 - alpha)) / combined, 0, 255);
            output[at + 3] = (byte)(combined * 255);
        }
        return Bitmap(output, w, h);
    }
    internal static byte[] Png(BitmapSource bitmap) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = new MemoryStream(); encoder.Save(output); return output.ToArray(); }
}

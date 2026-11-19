using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lantern.App;

internal static class ClipboardImages
{
    internal static DataObject CopyData(BitmapSource image)
    {
        var data = new DataObject();
        data.SetImage(image);
        // PNG preserves alpha; Bitmap supports applications expecting CF_BITMAP/CF_DIB.
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        var png = new MemoryStream(); encoder.Save(png); png.Position = 0;
        data.SetData("PNG", png, false);
        return data;
    }
    internal static void CopyLocal(string path)
    {
        var data = CopyData(LoadLocal(path, 0));
        using var png = (Stream)data.GetData("PNG", false)!;
        Clipboard.SetDataObject(data, true);
    }
    internal static bool ContainsImage() => Clipboard.ContainsData("PNG") || Clipboard.ContainsImage();
    internal static BitmapSource? Read()
    {
        // PNG carries defined alpha. CF_DIB/Bitmap from many Windows apps does not.
        var data = Clipboard.GetDataObject();
        if (data?.GetDataPresent("PNG", false) == true)
        {
            try
            {
                var png = data.GetData("PNG", false);
                using var stream = png is byte[] bytes ? new MemoryStream(bytes) : png is Stream input ? CopyStream(input) : null;
                if (stream is not null)
                {
                    return ReadPng(stream);
                }
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { }
        }
        var bitmap = Clipboard.GetImage(); return bitmap is null ? null : NormalizeBitmap(bitmap);
    }
    private static MemoryStream CopyStream(Stream input)
    {
        if (input.CanSeek) input.Position = 0;
        var copy = new MemoryStream(); input.CopyTo(copy); copy.Position = 0; return copy;
    }
    internal static BitmapSource ReadPng(Stream stream)
    {
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); frame.Freeze(); return frame;
    }
    internal static BitmapSource NormalizeBitmap(BitmapSource bitmap)
    {
        if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Pbgra32) return bitmap;
        int stride = checked(bitmap.PixelWidth * 4);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)]; bitmap.CopyPixels(pixels, stride, 0);
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) return bitmap;
        // Do not unpremultiply Pbgra32 with zero alpha: that would discard its RGB bytes.
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var opaque = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.DpiX, bitmap.DpiY, PixelFormats.Bgra32, null, pixels, stride); opaque.Freeze(); return opaque;
    }
    internal static bool IsLegacyClipboardPath(string path) => Path.GetFileName(path).Contains("_Anh-dan-", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Contains("_Lantern-clipboard-", StringComparison.OrdinalIgnoreCase);
    internal static BitmapSource LoadLocal(string path, int width)
    {
        if (IsLegacyClipboardPath(path))
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource image = NormalizeBitmap(frame);
            if (width > 0 && image.PixelWidth > width) image = new TransformedBitmap(image, new ScaleTransform(width / (double)image.PixelWidth, width / (double)image.PixelWidth));
            image.Freeze(); return image;
        }
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = width; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
}

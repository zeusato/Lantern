using System.Windows;
using System.Windows.Media;
using Lantern.App;
using Drawing = System.Drawing;

internal sealed partial class UiHarness
{
    private sealed record DesktopSample(Drawing.Point Location, Drawing.Color Color);

    private static Drawing.Color ReadPixel(Drawing.Point p)
    {
        using var pixel = new Drawing.Bitmap(1, 1);
        using var graphics = Drawing.Graphics.FromImage(pixel);
        graphics.CopyFromScreen(p, Drawing.Point.Empty, pixel.Size);
        return pixel.GetPixel(0, 0);
    }

    // Run --tray-display on an interactive desktop with an unobstructed monitor.
    private static async Task<DesktopSample[]> SetUpTrayDisplay(MainWindow chat)
    {
        var monitor = System.Windows.Forms.Screen.AllScreens.OrderByDescending(s => s.Bounds.Left).First();
        chat.Hide();
        await Task.Delay(600);
        var area = monitor.WorkingArea;
        var samples = new[] { 0.25, 0.5, 0.75 }.Select(f =>
        {
            var p = new Drawing.Point(area.Left + (int)(area.Width * f), area.Top + area.Height / 2);
            return new DesktopSample(p, ReadPixel(p));
        }).ToArray();
        var scale = VisualTreeHelper.GetDpi(chat);
        chat.Left = area.Left / scale.DpiScaleX + 40;
        chat.Top = area.Top / scale.DpiScaleY + 40;
        chat.Show();
        return samples;
    }

    private async Task CheckTrayDisplay(DesktopSample[] samples)
    {
        for (var i = 0; i < 24; i++)
        {
            await Task.Delay(250);
            foreach (var sample in samples)
            {
                var actual = ReadPixel(sample.Location);
                if (actual != sample.Color)
                    throw new InvalidOperationException($"Hidden chat changed the desktop at {sample.Location} from {sample.Color} to {actual} ({(i + 1) * 250} ms).");
            }
        }
        Check(true, "Desktop behind the hidden chat remains visible for six seconds");
    }
}

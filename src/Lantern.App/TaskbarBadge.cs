using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Lantern.App;

internal static class TaskbarBadge
{
    internal static DrawingImage Create(int count)
    {
        var label = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        var drawing = new DrawingGroup();
        using (var canvas = drawing.Open())
        {
            canvas.DrawEllipse(new SolidColorBrush(Color.FromRgb(235, 48, 66)), new Pen(Brushes.White, 1), new Point(16, 16), 15, 15);
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), label.Length > 2 ? 11 : label.Length > 1 ? 16 : 21, Brushes.White, 1);
            canvas.DrawText(text, new Point(16 - text.Width / 2, 16 - text.Height / 2));
        }
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}

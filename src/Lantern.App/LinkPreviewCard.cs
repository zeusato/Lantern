using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.Core;

namespace Lantern.App;

internal sealed class LinkPreviewCard : Button
{
    private bool started;
    internal LinkPreviewCard(Uri uri, LinkPreviews previews, Action open)
    {
        SetResourceReference(StyleProperty, typeof(Button));
        Margin = new Thickness(0, 0, 0, 12); Padding = new Thickness(0); HorizontalContentAlignment = HorizontalAlignment.Stretch; ToolTip = uri.AbsoluteUri;
        var stack = new StackPanel(); Content = stack;
        var hero = new Image { Height = 112, Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed }; hero.SizeChanged += (_, e) => hero.Clip = new RectangleGeometry(new Rect(new Point(), e.NewSize), 7, 7); stack.Children.Add(hero);
        var text = new StackPanel { Margin = new Thickness(11) }; stack.Children.Add(text);
        var title = new TextBlock { Text = uri.Host, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 56, Margin = new Thickness(0, 0, 0, 5) }; title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); text.Children.Add(title);
        var link = new TextBlock { Text = uri.Host + uri.PathAndQuery, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11 }; link.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); text.Children.Add(link);
        Click += (_, _) => open();
        async void Load()
        {
            if (started || !IsVisible) return; started = true;
            var preview = await previews.GetAsync(uri); if (preview is null) return;
            if (preview.Title.Length > 0) title.Text = preview.Title;
            if (preview.ImageBytes is { Length: > 0 } bytes)
            {
                try
                {
                    using var stream = new MemoryStream(bytes); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 520; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); hero.Source = bitmap; hero.Visibility = Visibility.Visible;
                }
                catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException) { }
            }
        }
        Loaded += (_, _) => Load(); IsVisibleChanged += (_, _) => Load();
    }
}

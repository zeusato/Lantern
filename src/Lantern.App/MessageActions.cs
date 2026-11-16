using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Lantern.Core;

namespace Lantern.App;

internal sealed class MessageActions : Border
{
    private readonly Popup palette;
    internal MessageActions(FrameworkElement host, Action reply, Func<string, Task> react)
    {
        Name = "MessageActions"; Visibility = Visibility.Collapsed; CornerRadius = new CornerRadius(12); Padding = new Thickness(3); BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(BorderBrushProperty, "LineBrush");
        Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.13 };
        var row = new StackPanel { Orientation = Orientation.Horizontal }; Child = row;
        var arrow = new Path { Data = Geometry.Parse("M 10,5 L 3,11 L 10,17 M 4,11 L 14,11 Q 21,11 21,19"), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 22, Height = 22 };
        arrow.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        var quote = ActionButton(arrow, "Trả lời có trích dẫn"); quote.Click += (_, _) => reply(); row.Children.Add(quote);
        var reaction = ActionButton(EmojiAssets.Icon("🙂", 24), "Thả biểu cảm"); row.Children.Add(reaction);
        palette = new Popup { PlacementTarget = this, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, VerticalOffset = -4 };
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        var surface = new Border { Child = choices, Padding = new Thickness(5), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        surface.SetResourceReference(BackgroundProperty, "SurfaceBrush"); surface.SetResourceReference(BorderBrushProperty, "LineBrush"); palette.Child = surface;
        foreach (var emoji in PeerNode.ReactionEmoji)
        {
            var button = ActionButton(EmojiAssets.Icon(emoji, 28), EmojiAssets.Find(emoji)!.Name); button.Width = 36; button.Height = 36;
            button.Click += async (_, _) => { palette.IsOpen = false; await react(emoji); }; choices.Children.Add(button);
        }
        reaction.Click += (_, _) => palette.IsOpen = !palette.IsOpen;
        host.MouseEnter += (_, _) => Visibility = Visibility.Visible;
        host.MouseLeave += (_, _) => UpdateVisibility(host);
        host.IsKeyboardFocusWithinChanged += (_, _) => UpdateVisibility(host);
        host.IsVisibleChanged += (_, _) => { if (!host.IsVisible) { palette.IsOpen = false; Visibility = Visibility.Collapsed; } };
        host.Unloaded += (_, _) => { palette.IsOpen = false; };
        palette.Closed += (_, _) => UpdateVisibility(host);
    }
    private void UpdateVisibility(FrameworkElement host) => Visibility = host.IsMouseOver || host.IsKeyboardFocusWithin || palette.IsOpen ? Visibility.Visible : Visibility.Collapsed;
    private static Button ActionButton(UIElement content, string label)
    {
        var button = new Button { Content = content, Width = 32, Height = 30, MinHeight = 0, Padding = new Thickness(3), Margin = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = label };
        AutomationProperties.SetName(button, label); return button;
    }
}

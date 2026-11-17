using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Lantern.Core;

namespace Lantern.App;

internal sealed class MessageReactions : StackPanel
{
    private readonly Popup palette;
    internal MessageReactions(FrameworkElement host, IReadOnlyDictionary<string, string> reactions, string selfId, Func<string, string> name, Func<string, Task> react)
    {
        Name = "MessageReactions"; Orientation = Orientation.Horizontal;
        var active = reactions.Where(pair => pair.Value.Length > 0).ToArray();
        var hasReactions = active.Length > 0;
        Visibility = hasReactions ? Visibility.Visible : Visibility.Collapsed;
        var thumb = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 2,10 L 5,10 L 5,21 L 2,21 Z M 7,10 L 11,3 Q 14,2 14,5 L 13,9 L 20,9 Q 22,9 21,12 L 19,20 Q 19,21 17,21 L 7,21 Z"), StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 14, Height = 14 };
        thumb.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedBrush");
        var trigger = MessageActions.RoundButton(thumb, "Thả biểu cảm"); trigger.Name = "ReactionTrigger"; trigger.Width = 24;
        palette = new Popup { PlacementTarget = trigger, Placement = PlacementMode.Top, VerticalOffset = -5, StaysOpen = false, AllowsTransparency = true };
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        var surface = new Border { Child = choices, Padding = new Thickness(5), CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1) };
        surface.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); surface.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); palette.Child = surface;
        var closeDelay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        void OpenPalette() { closeDelay.Stop(); palette.IsOpen = true; }
        void CloseLater() { closeDelay.Stop(); closeDelay.Start(); }
        closeDelay.Tick += (_, _) => { closeDelay.Stop(); if (!trigger.IsMouseOver && !surface.IsMouseOver && !surface.IsKeyboardFocusWithin) palette.IsOpen = false; };
        trigger.MouseEnter += (_, _) => OpenPalette(); trigger.MouseLeave += (_, _) => CloseLater();
        surface.MouseEnter += (_, _) => closeDelay.Stop(); surface.MouseLeave += (_, _) => CloseLater();
        surface.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { palette.IsOpen = false; trigger.Focus(); e.Handled = true; } };
        foreach (var emoji in PeerNode.ReactionEmoji)
        {
            var button = MessageActions.RoundButton(EmojiAssets.Icon(emoji, 28), EmojiAssets.Find(emoji)!.Name); button.Width = button.Height = 36; button.BorderThickness = new Thickness(0);
            button.Click += async (_, _) => { palette.IsOpen = false; await react(emoji); }; choices.Children.Add(button);
        }
        if (hasReactions)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var emoji in active.Select(pair => pair.Value).Distinct().Take(3)) row.Children.Add(EmojiAssets.Icon(emoji, 17));
            row.Children.Add(new TextBlock { Text = active.Length.ToString(), FontSize = 11, Margin = new Thickness(3, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center });
            var summary = MessageActions.RoundButton(row, string.Join("\n", active.Select(pair => (pair.Key == selfId ? "Bạn" : name(pair.Key)) + ": " + pair.Value)));
            summary.Name = "ReactionSummary"; summary.Margin = new Thickness(0, 0, 3, 0);
            var own = reactions.GetValueOrDefault(selfId, ""); if (own.Length > 0) summary.SetResourceReference(Control.BackgroundProperty, "AccentSoftBrush");
            summary.Click += async (_, _) => { if (own.Length > 0) await react(own); else palette.IsOpen = !palette.IsOpen; };
            Children.Add(summary);
        }
        Children.Add(trigger);
        trigger.Click += (_, _) => OpenPalette();
        void UpdateVisibility() => Visibility = hasReactions || host.IsMouseOver || host.IsKeyboardFocusWithin || palette.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        host.MouseEnter += (_, _) => Visibility = Visibility.Visible; host.MouseLeave += (_, _) => UpdateVisibility(); host.IsKeyboardFocusWithinChanged += (_, _) => UpdateVisibility();
        palette.Closed += (_, _) => { closeDelay.Stop(); UpdateVisibility(); }; host.Unloaded += (_, _) => { closeDelay.Stop(); palette.IsOpen = false; };
        host.IsVisibleChanged += (_, _) => { if (!host.IsVisible) palette.IsOpen = false; };
    }
}

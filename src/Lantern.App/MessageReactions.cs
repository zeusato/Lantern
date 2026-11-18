using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Lantern.Core;

namespace Lantern.App;

internal sealed class MessageReactions : StackPanel
{
    private readonly Popup palette;
    internal MessageReactions(FrameworkElement host, IReadOnlyDictionary<string, string> reactions, string selfId, Func<string, string> name, Func<string, Task> react, Func<string, ImageSource?>? avatar = null)
    {
        Name = "MessageReactions"; Orientation = Orientation.Horizontal;
        var active = reactions.Where(pair => pair.Value.Length > 0).ToArray();
        var hasReactions = active.Length > 0;
        Visibility = hasReactions ? Visibility.Visible : Visibility.Collapsed;
        var thumb = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 2,10 L 5,10 L 5,21 L 2,21 Z M 7,10 L 11,3 Q 14,2 14,5 L 13,9 L 20,9 Q 22,9 21,12 L 19,20 Q 19,21 17,21 L 7,21 Z"), StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 14, Height = 14 };
        thumb.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedBrush");
        var trigger = MessageActions.RoundButton(thumb, "Thả biểu cảm"); trigger.Name = "ReactionTrigger"; trigger.Width = 24;
        var triggerArea = new Border { Background = Brushes.Transparent, Padding = new Thickness(6), Margin = new Thickness(-4), Child = trigger };
        palette = new Popup { PlacementTarget = triggerArea, Placement = PlacementMode.Top, VerticalOffset = 3, StaysOpen = false, AllowsTransparency = true };
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        var paletteContent = new StackPanel(); paletteContent.Children.Add(choices);
        var surface = new Border { Child = paletteContent, Padding = new Thickness(7), CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1) };
        var hoverSurface = new Border { Background = Brushes.Transparent, Padding = new Thickness(10), Child = surface };
        surface.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); surface.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); palette.Child = hoverSurface;
        palette.Opened += (_, _) => { if (Window.GetWindow(host) is { } owner && !ScreenCaptureProtection.ProtectPopup(hoverSurface, owner)) palette.IsOpen = false; };
        var closeDelay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void OpenPalette() { closeDelay.Stop(); palette.IsOpen = true; }
        void CloseLater() { closeDelay.Stop(); closeDelay.Start(); }
        bool InHoverCorridor()
        {
            if (!palette.IsOpen || !triggerArea.IsVisible || !hoverSurface.IsVisible) return false;
            var area = new Rect(triggerArea.PointToScreen(new Point()), triggerArea.PointToScreen(new Point(triggerArea.ActualWidth, triggerArea.ActualHeight)));
            area.Union(new Rect(hoverSurface.PointToScreen(new Point()), hoverSurface.PointToScreen(new Point(hoverSurface.ActualWidth, hoverSurface.ActualHeight)))); area.Inflate(12, 12);
            return area.Contains(triggerArea.PointToScreen(System.Windows.Input.Mouse.GetPosition(triggerArea)));
        }
        closeDelay.Tick += (_, _) => { if (triggerArea.IsMouseOver || hoverSurface.IsMouseOver || surface.IsKeyboardFocusWithin || InHoverCorridor()) return; closeDelay.Stop(); palette.IsOpen = false; };
        trigger.MouseEnter += (_, _) => OpenPalette(); trigger.MouseLeave += (_, _) => CloseLater();
        triggerArea.MouseEnter += (_, _) => OpenPalette(); triggerArea.MouseLeave += (_, _) => CloseLater();
        surface.MouseEnter += (_, _) => closeDelay.Stop(); surface.MouseLeave += (_, _) => CloseLater();
        hoverSurface.MouseEnter += (_, _) => closeDelay.Stop(); hoverSurface.MouseLeave += (_, _) => CloseLater();
        surface.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { palette.IsOpen = false; trigger.Focus(); e.Handled = true; } };
        foreach (var emoji in PeerNode.ReactionEmoji)
        {
            var button = MessageActions.RoundButton(EmojiAssets.Icon(emoji, 28), EmojiAssets.Tooltip(EmojiAssets.Find(emoji)!)); button.Width = button.Height = 36; button.BorderThickness = new Thickness(0);
            button.Click += async (_, _) => { palette.IsOpen = false; await react(emoji); }; choices.Children.Add(button);
        }
        var own = reactions.GetValueOrDefault(selfId, "");
        if (own.Length > 0)
        {
            var remove = new Button { Content = "Gỡ cảm xúc của bạn", Margin = new Thickness(4, 5, 4, 0), Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
            remove.Click += async (_, _) => { palette.IsOpen = false; await react(""); }; paletteContent.Children.Add(remove);
        }
        if (hasReactions)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var emoji in active.Select(pair => pair.Value).Distinct().Take(3)) row.Children.Add(EmojiAssets.Icon(emoji, 17));
            row.Children.Add(new TextBlock { Text = active.Length.ToString(), FontSize = 11, Margin = new Thickness(3, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center });
            var names = string.Join("\n", active.Take(5).Select(pair => (pair.Key == selfId ? "Bạn" : name(pair.Key)) + " " + pair.Value));
            if (active.Length > 5) names += $"\nvà {active.Length - 5} người khác";
            var summary = MessageActions.RoundButton(row, "Xem người đã thả cảm xúc");
            summary.ToolTip = new ToolTip { Content = names, Placement = PlacementMode.Bottom, VerticalOffset = 5, PlacementTarget = summary };
            ((ToolTip)summary.ToolTip).Opened += (_, _) => { var tip = (ToolTip)summary.ToolTip; if (Window.GetWindow(host) is { } owner && !ScreenCaptureProtection.ProtectPopup(tip, owner)) tip.IsOpen = false; };
            summary.Name = "ReactionSummary"; summary.Margin = new Thickness(0, 0, 3, 0);
            if (own.Length > 0) summary.SetResourceReference(Control.BackgroundProperty, "AccentSoftBrush");
            summary.Click += (_, _) =>
            {
                palette.IsOpen = false; var owner = Window.GetWindow(host); if (owner is null) return;
                var content = new DockPanel { MinHeight = 260 };
                var heading = new TextBlock { Text = "Biểu cảm", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) }; heading.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); DockPanel.SetDock(heading, Dock.Top); content.Children.Add(heading);
                var tabs = new StackPanel { Width = 125, Margin = new Thickness(0, 0, 12, 0) }; DockPanel.SetDock(tabs, Dock.Left); content.Children.Add(tabs);
                var people = new StackPanel(); content.Children.Add(new ScrollViewer { Content = people, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                void Filter(string? emoji)
                {
                    people.Children.Clear();
                    foreach (var pair in active.Where(p => emoji is null || p.Value == emoji).OrderBy(p => name(p.Key)))
                    {
                        var person = new DockPanel { Margin = new Thickness(5, 8, 5, 8) };
                        var icon = EmojiAssets.Icon(pair.Value, 24); DockPanel.SetDock(icon, Dock.Right); person.Children.Add(icon);
                        if (avatar?.Invoke(pair.Key) is { } photo) { var face = new Image { Source = photo, Width = 32, Height = 32, Margin = new Thickness(0, 0, 10, 0), Clip = new EllipseGeometry(new Point(16, 16), 16, 16) }; person.Children.Add(face); }
                        var label = new TextBlock { Text = pair.Key == selfId ? "Bạn" : name(pair.Key), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); person.Children.Add(label); people.Children.Add(person);
                    }
                }
                void Tab(string title, string? emoji)
                {
                    var button = new Button { Content = title, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 5) }; button.Click += (_, _) => Filter(emoji); tabs.Children.Add(button);
                }
                Tab("Tất cả  " + active.Length, null);
                foreach (var group in active.GroupBy(p => p.Value)) Tab(group.Key + "  " + group.Count(), group.Key);
                if (own.Length > 0)
                {
                    var remove = new Button { Content = new TextBlock { Text = "Gỡ cảm xúc\ncủa bạn", TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 15, 0, 0) };
                    remove.Click += async (_, _) => { InAppDialog.CancelAll(owner); await react(""); }; tabs.Children.Add(remove);
                }
                Filter(null); InAppDialog.Show(owner, "Biểu cảm", content, "Đóng", width: 560);
            };
            Children.Add(summary);
        }
        Children.Add(triggerArea);
        trigger.Click += (_, _) => OpenPalette();
        void UpdateVisibility() => Visibility = hasReactions || host.IsMouseOver || host.IsKeyboardFocusWithin || palette.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        host.MouseEnter += (_, _) => Visibility = Visibility.Visible; host.MouseLeave += (_, _) => UpdateVisibility(); host.IsKeyboardFocusWithinChanged += (_, _) => UpdateVisibility();
        palette.Closed += (_, _) => { closeDelay.Stop(); UpdateVisibility(); }; host.Unloaded += (_, _) => { closeDelay.Stop(); palette.IsOpen = false; };
        host.IsVisibleChanged += (_, _) => { if (!host.IsVisible) palette.IsOpen = false; };
    }
}

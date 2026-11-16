using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lantern.App;

internal static class InAppDialog
{
    private static readonly Dictionary<Window, List<Action>> active = [];
    public static bool IsOpen(Window owner) => active.ContainsKey(owner);
    public static void CancelAll(Window owner)
    {
        if (active.TryGetValue(owner, out var dialogs)) foreach (var cancel in dialogs.ToArray()) cancel();
    }
    public static bool Show(Window owner, string title, FrameworkElement content, string accept, Func<bool>? validate = null, UIElement? initialFocus = null)
    {
        var root = (Grid)owner.Content;
        var previousFocus = Keyboard.FocusedElement;
        var background = root.Children.Cast<UIElement>().Select(child => (Child: child, Enabled: child.IsEnabled)).ToArray();
        foreach (var item in background) item.Child.IsEnabled = false;
        var overlay = new Border { Name = "InAppDialog", Background = new SolidColorBrush(Color.FromArgb(130, 0, 0, 0)), Padding = new Thickness(24), Tag = title };
        Grid.SetRowSpan(overlay, int.MaxValue); Panel.SetZIndex(overlay, 1000);
        var card = new Border { Width = 510, MaxWidth = Math.Max(280, owner.ActualWidth - 48), CornerRadius = new CornerRadius(18), Padding = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        var panel = new StackPanel();
        panel.Children.Add(content);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(buttons);
        var frame = new DispatcherFrame(); bool accepted = false;
        void Cancel() => frame.Continue = false;
        var cancel = new Button { Content = "Để sau", Margin = new Thickness(0, 0, 10, 0) }; cancel.Click += (_, _) => Cancel(); buttons.Children.Add(cancel);
        void Accept()
        {
            if (validate?.Invoke() == false) { error.Text = "Vui lòng kiểm tra thông tin vừa nhập."; initialFocus?.Focus(); return; }
            accepted = true; frame.Continue = false;
        }
        var okay = new Button { Content = accept, Style = (Style)owner.FindResource("AccentButton") }; okay.Click += (_, _) => Accept(); buttons.Children.Add(okay);
        card.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(240, owner.ActualHeight - 104) }; overlay.Child = card;
        KeyboardNavigation.SetTabNavigation(overlay, KeyboardNavigationMode.Cycle);
        overlay.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Cancel(); e.Handled = true; } else if (e.Key == Key.Enter && Keyboard.FocusedElement is PasswordBox) { Accept(); e.Handled = true; } };
        void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { if (!owner.IsVisible) Cancel(); }
        void Closed(object? sender, EventArgs e) => Cancel();
        if (!active.TryGetValue(owner, out var dialogs)) active[owner] = dialogs = [];
        dialogs.Add(Cancel); owner.Closed += Closed; owner.IsVisibleChanged += VisibilityChanged;
        root.Children.Add(overlay);
        owner.UpdateLayout();
        owner.Dispatcher.BeginInvoke(() =>
        {
            if (!overlay.IsVisible) return;
            if (initialFocus is not null) Keyboard.Focus(initialFocus);
            else content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }, DispatcherPriority.Input);
        try { Dispatcher.PushFrame(frame); return accepted; }
        finally
        {
            root.Children.Remove(overlay); panel.Children.Remove(content);
            owner.Closed -= Closed; owner.IsVisibleChanged -= VisibilityChanged;
            dialogs.Remove(Cancel); if (dialogs.Count == 0) active.Remove(owner);
            foreach (var item in background) item.Child.IsEnabled = item.Enabled;
            if (owner.IsVisible && previousFocus is UIElement { IsVisible: true, IsEnabled: true }) Keyboard.Focus(previousFocus);
        }
    }
}

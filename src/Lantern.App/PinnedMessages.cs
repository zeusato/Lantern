using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Lantern.Core;

namespace Lantern.App;

public partial class MainWindow
{
    private string pinnedConversation = "", pinnedPreviewId = "", pinnedSignature = "";
    private ContextMenu? pinnedMessageMenu;
    private Border? highlightedMessage;
    private Thickness highlightedBorder;
    private DispatcherTimer? highlightTimer;

    private void ClearPinnedMessages()
    {
        if (pinnedMessageMenu is not null) { pinnedMessageMenu.IsOpen = false; pinnedMessageMenu.Items.Clear(); pinnedMessageMenu = null; }
        pinnedConversation = pinnedPreviewId = pinnedSignature = "";
        PinnedMessageBar.Visibility = Visibility.Collapsed; PinnedMessageSummary.Text = "";
        ClearMessageHighlight();
    }
    private void RefreshPinnedMessages()
    {
        if (appLocking || ConversationProtected || captureUnavailable || selectedId.Length == 0) { ClearPinnedMessages(); return; }
        var messages = store.PinnedMessages(selectedId).Select(store.Message).OfType<ChatMessage>().ToArray();
        if (messages.Length == 0) { ClearPinnedMessages(); return; }
        var signature = selectedId + "|" + string.Join('|', messages.Select(m => m.Id + Author(m) + Snippet(m)));
        if (signature != pinnedSignature && pinnedMessageMenu is not null) { pinnedMessageMenu.IsOpen = false; pinnedMessageMenu.Items.Clear(); pinnedMessageMenu = null; }
        if (pinnedConversation != selectedId || !messages.Any(m => m.Id == pinnedPreviewId)) pinnedPreviewId = messages[0].Id;
        pinnedConversation = selectedId; pinnedSignature = signature;
        var selected = messages.Single(m => m.Id == pinnedPreviewId);
        PinnedMessageSummary.Text = Author(selected) + " · " + Snippet(selected);
        PinnedMessageBar.Visibility = Visibility.Visible;
        PinnedMessagesDropdown.Visibility = messages.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        PinnedMessagesDropdown.Content = $"+{messages.Length - 1} ghim ⌄";
    }
    private void PinnedMessagePreviewClick(object sender, RoutedEventArgs e) => GoToMessage(pinnedPreviewId);
    private void UnpinMessageClick(object sender, RoutedEventArgs e)
    {
        if (appLocking || ConversationProtected || pinnedConversation != selectedId) return;
        try { store.PinMessage(pinnedPreviewId, false); renderKey = ""; RefreshConversation(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ShowPinnedMessagesClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking || captureUnavailable) return;
        RefreshPinnedMessages();
        if (PinnedMessageBar.Visibility != Visibility.Visible) return;
        if (pinnedMessageMenu is not null) pinnedMessageMenu.IsOpen = false;
        BuildPinnedMessageMenu(sender as UIElement ?? PinnedMessagesDropdown).IsOpen = true;
    }
    private ContextMenu BuildPinnedMessageMenu(UIElement placementTarget)
    {
        var conversation = selectedId;
        var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = PlacementMode.Bottom };
        pinnedMessageMenu = menu;
        foreach (var message in store.PinnedMessages(conversation).Select(store.Message).OfType<ChatMessage>())
        {
            var label = new StackPanel { MaxWidth = 360 };
            label.Children.Add(new TextBlock { Text = Author(message), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            label.Children.Add(new TextBlock { Text = Snippet(message), TextWrapping = TextWrapping.Wrap, MaxHeight = 48, Margin = new Thickness(0, 3, 0, 0) });
            var item = new MenuItem { Header = label, Tag = message.Id, IsCheckable = true, IsChecked = message.Id == pinnedPreviewId };
            item.Click += (_, _) => { if (selectedId != conversation || appLocking || ConversationProtected) return; pinnedPreviewId = message.Id; RefreshPinnedMessages(); GoToMessage(message.Id); };
            menu.Items.Add(item);
        }
        menu.Opened += (_, _) => { if (!ScreenCaptureProtection.ProtectPopup(menu, this)) menu.IsOpen = false; };
        return menu;
    }
    private void ClearMessageHighlight()
    {
        highlightTimer?.Stop(); highlightTimer = null;
        if (highlightedMessage is not null) { highlightedMessage.BorderThickness = highlightedBorder; highlightedMessage.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); highlightedMessage = null; }
    }
    private void HighlightMessage(FrameworkElement row)
    {
        ClearMessageHighlight();
        Border? Surface(DependencyObject element)
        {
            if (element is Border { Name: "MessageSurface" } border) return border;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) if (Surface(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
            return null;
        }
        if (Surface(row) is not { } surface) return;
        highlightedMessage = surface; highlightedBorder = surface.BorderThickness;
        surface.BorderThickness = new Thickness(2); surface.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        row.Focusable = true; if (IsActive) row.Focus(); row.BringIntoView();
        highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; highlightTimer.Tick += (_, _) => ClearMessageHighlight(); highlightTimer.Start();
    }
}

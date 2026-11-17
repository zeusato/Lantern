using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lantern.Core;

namespace Lantern.App;

public partial class MainWindow
{
    private sealed record SharedItem(string Key, FrameworkElement View);
    private readonly Dictionary<string, SharedItem> sharedItems = [];
    private string sharedConversation = "";
    private long sharedVersion = -1;

    private void ClearSharedContent()
    {
        SharedFiles.Children.Clear(); SharedMedia.Children.Clear(); SharedLinks.Children.Clear();
        sharedItems.Clear(); sharedConversation = ""; sharedVersion = -1;
    }
    private void RefreshSharedClick(object sender, RoutedEventArgs e)
    {
        if (appLocking || ConversationProtected || closing || selectedId.Length == 0) return;
        RefreshSharedContent(force: true);
    }
    private void RefreshSharedContent(bool force = false)
    {
        RefreshSharedButton.IsEnabled = selectedId.Length > 0 && !ConversationProtected && !appLocking;
        if (!RefreshSharedButton.IsEnabled) { ClearSharedContent(); return; }
        if (sharedConversation != selectedId) { ClearSharedContent(); sharedConversation = selectedId; }
        var version = store.MessageVersion(selectedId);
        if (!force && sharedVersion == version) return;
        sharedVersion = version;
        var history = store.Messages(selectedId, limit: int.MaxValue);
        var files = new List<FrameworkElement>(); var media = new List<FrameworkElement>(); var links = new List<FrameworkElement>();
        var retained = new HashSet<string>();
        FrameworkElement Item(string id, string key, Func<FrameworkElement> create)
        {
            retained.Add(id);
            if (!force && sharedItems.TryGetValue(id, out var old) && old.Key == key) return old.View;
            var view = create(); sharedItems[id] = new(key, view); return view;
        }
        foreach (var file in history.Where(m => m.Kind is "file" or "image" or "video").TakeLast(100).Reverse())
        {
            // Read receipts, reactions and progress ticks do not invalidate a thumbnail.
            var key = Codec.Encode(new { file.Kind, file.FileName, file.FilePath, file.FileSize, file.Hash, Available = File.Exists(file.FilePath) });
            var view = Item("file:" + file.Id, key, () =>
            {
                if (file.Kind is "image" or "video")
                    return new MediaThumbnail(file, () => { if (!File.Exists(file.FilePath)) GoToMessage(file.Id); else if (file.Kind == "image") ShowImage(file); else OpenAttachment(file); }, uiLifetime.Token);
                var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10), ToolTip = file.FileName };
                var content = new StackPanel(); var title = Paragraph(file.FileName, 12, true); title.MaxWidth = 175; title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap; title.Margin = new Thickness(0, 0, 0, 4); content.Children.Add(title);
                var subtitle = Paragraph(FormatBytes(file.FileSize), 11, muted: true); subtitle.Margin = new Thickness(); content.Children.Add(subtitle); button.Content = content;
                button.Click += (_, _) => { if (File.Exists(file.FilePath)) OpenAttachment(file); else GoToMessage(file.Id); }; return button;
            });
            (file.Kind == "file" ? files : media).Add(view);
        }
        foreach (var link in history.Where(m => m.Kind == "text").SelectMany(m => System.Text.RegularExpressions.Regex.Matches(m.Text, @"https?://[^\s<>]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)).Select(match => match.Value.TrimEnd('.', ',', '!', '?', ')', ']'))).Distinct().TakeLast(100).Reverse())
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) continue;
            links.Add(Item("link:" + link, link, () => new LinkPreviewCard(uri, linkPreviews, () => { try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } })));
        }
        foreach (var id in sharedItems.Keys.Where(id => !retained.Contains(id)).ToArray()) sharedItems.Remove(id);
        ReconcileShared(SharedFiles, files); ReconcileShared(SharedMedia, media); ReconcileShared(SharedLinks, links);
        NoFiles.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMedia.Visibility = media.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoLinks.Visibility = links.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static void ReconcileShared(Panel panel, List<FrameworkElement> desired)
    {
        var keep = desired.ToHashSet();
        for (int i = panel.Children.Count - 1; i >= 0; i--) if (!keep.Contains((FrameworkElement)panel.Children[i])) panel.Children.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < panel.Children.Count && ReferenceEquals(panel.Children[i], desired[i])) continue;
            panel.Children.Remove(desired[i]); panel.Children.Insert(i, desired[i]);
        }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lantern.Core;

namespace Lantern.App;

public partial class MainWindow
{
    private StickerSearch? stickerSearch;
    private int stickerQueryGeneration;
    private void ClearStickerSuggestions()
    {
        stickerQueryGeneration++;
        StickerSuggestions?.Children.Clear();
        if (StickerSuggestionBar is not null) StickerSuggestionBar.Visibility = Visibility.Collapsed;
    }
    private void ComposerEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Composer?.IsEnabled == true) ScheduleStickerSuggestions(); else ClearStickerSuggestions();
    }
    private async void ScheduleStickerSuggestions()
    {
        ClearStickerSuggestions();
        if (Composer is null || !Composer.IsEnabled || appLocking || closing || selectedId.Length == 0 || ConversationProtected || string.IsNullOrWhiteSpace(Composer.Text)) return;
        int generation = stickerQueryGeneration; string text = Composer.Text, conversation = selectedId;
        try
        {
            await Task.Delay(200, uiLifetime.Token);
            if (generation != stickerQueryGeneration) return;
            await PrepareStickerLibraryAsync();
            if (generation != stickerQueryGeneration || selectedId != conversation || closing || appLocking || !Composer.IsEnabled || ConversationProtected) return;
            stickerSearch ??= new StickerSearch(Stickers.Packs());
            foreach (var (pack, sticker) in stickerSearch.Suggest(text))
            {
                var button = new Button { Content = new StickerImage(Stickers.PathFor(pack, sticker), 64, true), Width = 74, Height = 74, Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = sticker.Name + " · " + pack.Name };
                System.Windows.Automation.AutomationProperties.SetName(button, "Gửi sticker " + sticker.Name);
                button.Click += async (_, _) =>
                {
                    if (generation != stickerQueryGeneration || conversation != selectedId) return;
                    if (await SendStickerAsync(pack, sticker, conversation) && selectedId == conversation && Composer.Text == text) Composer.Clear();
                };
                StickerSuggestions.Children.Add(button);
            }
            StickerSuggestionBar.Visibility = StickerSuggestions.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            StickerSuggestionBar.ScrollToLeftEnd();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { /* Keep typing usable if a local pack is unavailable. */ }
    }
    private async Task<bool> SendStickerAsync(StickerPack pack, StickerItem sticker, string conversation)
    {
        if (!Composer.IsEnabled || appLocking || closing || ConversationProtected || selectedId != conversation) return false;
        try
        {
            await node.OfferFileAsync(conversation, Stickers.PathFor(pack, sticker), uiLifetime.Token,
                displayFileName: Protocol.SafeFileName(sticker.Name) + Path.GetExtension(sticker.File),
                sticker: new StickerReference(pack.Id, pack.Name, sticker.Id, sticker.Name));
            string key = pack.Id + "/" + sticker.Id; App.Settings.RecentStickers.Remove(key); App.Settings.RecentStickers.Insert(0, key);
            App.Settings.RecentStickers = App.Settings.RecentStickers.Take(40).ToList(); App.SaveSettings(); return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
    }
}

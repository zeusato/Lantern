using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Lantern.Core;

namespace Lantern.App;

public partial class MainWindow
{
    private StickerLibrary? stickerLibrary;
    private Task<StickerLibrary>? stickerLoading;
    private StickerLibrary Stickers => stickerLibrary ?? throw new InvalidOperationException("Thư viện sticker đang được chuẩn bị.");
    private async Task PrepareStickerLibraryAsync()
    {
        if (stickerLibrary is not null) return;
        var workspace = App.DataDirectory;
        stickerLoading ??= Task.Run(() =>
        {
                var library = new StickerLibrary(workspace);
                var assembly = typeof(MainWindow).Assembly;
                foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Lantern.App.Assets.Stickers.", StringComparison.Ordinal) && n.EndsWith(".pack.json", StringComparison.Ordinal)))
                {
                    using var stream = assembly.GetManifestResourceStream(resource)!; using var reader = new StreamReader(stream);
                    var pack = Codec.Decode<StickerPack>(reader.ReadToEnd()); var prefix = resource[..^"pack.json".Length];
                    library.InstallBundled(pack, file => { using var asset = assembly.GetManifestResourceStream(prefix + file) ?? throw new InvalidDataException("Thiếu ảnh sticker đóng gói."); using var output = new MemoryStream(); asset.CopyTo(output); return output.ToArray(); });
                }
                return library;
        });
        try { stickerLibrary = await stickerLoading; }
        catch { stickerLoading = null; throw; }
    }
    private async void StickerClick(object sender, RoutedEventArgs e)
    {
        if (!Composer.IsEnabled || appLocking || ConversationProtected) return;
        try
        {
            var conversation = selectedId;
            if (stickerLibrary is null) ShowToast("Đang chuẩn bị thư viện sticker…");
            await PrepareStickerLibraryAsync();
            if (appLocking || closing || ConversationProtected || selectedId != conversation || !Composer.IsEnabled) return;
            if (emojiPopup is not null) emojiPopup.IsOpen = false;
            var popup = new Popup { PlacementTarget = StickerButton, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true }; emojiPopup = popup;
            var palette = new StickerPalette(Stickers, async (pack, sticker) =>
            {
                popup.IsOpen = false;
                if (!Composer.IsEnabled || appLocking || ConversationProtected) return;
                var target = selectedId;
                try
                {
                    await node.OfferFileAsync(target, Stickers.PathFor(pack, sticker), uiLifetime.Token,
                        displayFileName: Protocol.SafeFileName(sticker.Name) + Path.GetExtension(sticker.File),
                        sticker: new StickerReference(pack.Id, pack.Name, sticker.Id, sticker.Name));
                    string key = pack.Id + "/" + sticker.Id; App.Settings.RecentStickers.Remove(key); App.Settings.RecentStickers.Insert(0, key);
                    App.Settings.RecentStickers = App.Settings.RecentStickers.Take(40).ToList(); App.SaveSettings();
                }
                catch (Exception ex) { ShowError(ex); }
            }, () => { popup.IsOpen = false; OpenStickerWorkshop(); });
            var border = new Border { Child = palette, Padding = new Thickness(16), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); popup.Child = border;
            popup.Opened += (_, _) => { if (!ScreenCaptureProtection.ProtectPopup(border, this)) popup.IsOpen = false; };
            popup.IsOpen = true;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void OpenStickerWorkshop()
    {
        if (appLocking) return;
        try
        {
            if (stickerLibrary is null) ShowToast("Đang chuẩn bị thư viện sticker…");
            await PrepareStickerLibraryAsync(); if (appLocking || closing) return;
            var target = selectedId; bool canShare = Composer.IsEnabled && !ConversationProtected;
            async Task Share(StickerPack pack)
            {
                if (appLocking || ConversationProtected || target != selectedId || !Composer.IsEnabled) throw new InvalidOperationException("Hội thoại không còn cho phép gửi.");
                var directory = Path.Combine(Path.GetTempPath(), "Lantern-stickers-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, Protocol.SafeFileName(pack.Name) + ".lanternstickers");
                try { Stickers.Export(pack, path); await node.OfferFileAsync(target, path, uiLifetime.Token); }
                finally { File.Delete(path); Directory.Delete(directory); }
            }
            ShowDialog("Xưởng chế tác sticker", new StickerWorkshop(this, Stickers, canShare ? Share : null), "Đóng", width: 760);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void ImportReceivedPack(ChatMessage message)
    {
        if (appLocking || ConversationProtected || message.ConversationId != selectedId) return;
        try
        {
            await PrepareStickerLibraryAsync(); if (appLocking || closing || ConversationProtected || selectedId != message.ConversationId) return;
            var current = store.Message(message.Id); if (current is null || current.ExpiresAt <= DateTimeOffset.UtcNow || !File.Exists(current.FilePath)) return;
            if (!ShowDialog("Thêm bộ sticker", new TextBlock { Text = "Thêm một bản sao của bộ sticker này vào thư viện trên máy?", TextWrapping = TextWrapping.Wrap }, "Thêm bộ")) return;
            if (appLocking || ConversationProtected || store.Message(message.Id) is not { } fresh || fresh.ExpiresAt <= DateTimeOffset.UtcNow) return;
            var pack = Stickers.Import(fresh.FilePath); ShowToast($"Đã thêm {pack.Stickers.Count} sticker vào bộ {pack.Name}.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void SaveReceivedSticker(ChatMessage message)
    {
        if (appLocking || ConversationProtected || message.ConversationId != selectedId || !node.ConversationRules(message.ConversationId).ForwardAllowed) return;
        try
        {
            await PrepareStickerLibraryAsync(); if (appLocking || closing || ConversationProtected || selectedId != message.ConversationId || !node.ConversationRules(message.ConversationId).ForwardAllowed) return;
            if (store.Message(message.Id) is not { Sticker: { } reference } current || current.ExpiresAt <= DateTimeOffset.UtcNow) return;
            var pack = Stickers.Packs().FirstOrDefault(p => p.Id == reference.PackId) ?? new StickerPack { Id = reference.PackId, Name = reference.PackName };
            if (pack.Stickers.Any(s => s.Id == reference.StickerId)) { ShowToast("Sticker đã có trong thư viện."); return; }
            var bytes = StickerImage.ValidateFile(current.FilePath);
            var item = Stickers.Add(pack, reference.Name, bytes); item.Id = reference.StickerId; Stickers.Save(pack);
            ShowToast("Đã lưu sticker vào bộ " + pack.Name + ".");
        }
        catch (Exception ex) { ShowError(ex); }
    }
}

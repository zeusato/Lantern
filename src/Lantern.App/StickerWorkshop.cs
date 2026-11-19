using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Globalization;
using Lantern.Core;
using Microsoft.Win32;

namespace Lantern.App;

internal sealed class StickerWorkshop : StackPanel
{
    private readonly Window owner;
    private readonly StickerLibrary library;
    private readonly ComboBox packs = new() { MinWidth = 220, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox search = new() { Margin = new Thickness(0, 4, 0, 8), ToolTip = "Tìm sticker theo tên" };
    private readonly WrapPanel items = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private int page;
    private StickerPack? Selected => packs.SelectedItem as StickerPack;
    internal StickerWorkshop(Window owner, StickerLibrary library, Func<StickerPack, Task>? share)
    {
        this.owner = owner; this.library = library;
        SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush");
        StickerPackPresentation.Configure(packs, library);
        Children.Add(new TextBlock { Text = "Xưởng chế tác sticker", FontSize = 24, FontWeight = FontWeights.SemiBold });
        Children.Add(new TextBlock { Text = "Bộ sticker của bạn · PNG và GIF động · Thay đổi được lưu ngay", Margin = new Thickness(0, 6, 0, 8) });
        Children.Add(packs);
        var actions = new WrapPanel();
        actions.Children.Add(Action("Import ảnh", ImportImages)); actions.Children.Add(Action("Tạo sticker", CreateSticker));
        actions.Children.Add(Action("Đổi tên bộ", () => { if (Selected is not { } pack) return; var name = AskName("Tên bộ sticker", pack.Name); if (name is null) return; pack.Name = name; library.Save(pack); Refresh(pack.Id); }));
        actions.Children.Add(Action("Xóa bộ", () => { if (Selected is not { } pack) return; if (!InAppDialog.Show(owner, "Xóa bộ", new TextBlock { Text = "Xóa bộ “" + pack.Name + "” khỏi thư viện trên máy này?", TextWrapping = TextWrapping.Wrap }, "Xóa bộ")) return; library.Delete(pack); Refresh(); }));
        Children.Add(actions);
        var transfer = new WrapPanel();
        transfer.Children.Add(Action("Nhập bộ từ tệp", () => { var dialog = new OpenFileDialog { Filter = "Bộ sticker Lantern|*.lanternstickers" }; if (dialog.ShowDialog(owner) == true) { var pack = library.Import(dialog.FileName); Refresh(pack.Id); status.Text = "Đã nhập một bản sao của bộ sticker."; } }));
        transfer.Children.Add(Action("Xuất bộ", () => { if (Selected is not { } pack) return; var dialog = new SaveFileDialog { FileName = Protocol.SafeFileName(pack.Name) + ".lanternstickers", Filter = "Bộ sticker Lantern|*.lanternstickers" }; if (dialog.ShowDialog(owner) == true) { library.Export(pack, dialog.FileName); status.Text = "Đã xuất bộ sticker."; } }));
        var send = new Button { Content = "Gửi bộ vào chat", Margin = new Thickness(3), IsEnabled = share is not null };
        send.Click += async (_, _) => { if (Selected is not { } pack || share is null) return; send.IsEnabled = false; try { await share(pack); status.Text = "Đã gửi bộ vào chat. Máy nhận tải tệp rồi bấm Thêm bộ sticker."; } catch (Exception ex) { status.Text = ex.Message; } finally { send.IsEnabled = true; } }; transfer.Children.Add(send); Children.Add(transfer);
        Children.Add(new TextBlock { Text = "Tìm sticker trong bộ", Margin = new Thickness(0, 8, 0, 0) }); Children.Add(search); System.Windows.Automation.AutomationProperties.SetName(search, "Tìm sticker trong bộ"); System.Windows.Automation.AutomationProperties.SetName(packs, "Bộ sticker đang chỉnh sửa");
        Children.Add(new ScrollViewer { Content = items, Height = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var pages = new WrapPanel(); pages.Children.Add(Action("Trang trước", () => { page = Math.Max(0, page - 1); Render(); })); pages.Children.Add(Action("Trang sau", () => { page++; Render(); })); Children.Add(pages); Children.Add(status);
        packs.SelectionChanged += (_, _) => { page = 0; Render(); }; search.TextChanged += (_, _) => { page = 0; Render(); }; Refresh();
    }
    private Button Action(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(3) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }; return button;
    }
    private static StackPanel Form()
    {
        var panel = new StackPanel(); panel.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush"); return panel;
    }
    private void Refresh(string? id = null)
    {
        packs.ItemsSource = library.Packs(); packs.SelectedItem = packs.Items.Cast<StickerPack>().FirstOrDefault(p => p.Id == id) ?? packs.Items.Cast<StickerPack>().FirstOrDefault(); Render();
    }
    private void Render()
    {
        items.Children.Clear(); if (Selected is not { } pack) { status.Text = "Chưa có bộ sticker. Bấm Import ảnh hoặc Tạo sticker để bắt đầu."; return; }
        string query = StickerLibrary.SearchKey(search.Text); var matches = pack.Stickers.Where(s => StickerLibrary.SearchKey(s.Name + " " + string.Join(" ", s.Keywords)).Contains(query)).ToArray();
        page = Math.Clamp(page, 0, Math.Max(0, (matches.Length - 1) / 40));
        foreach (var sticker in matches.Skip(page * 40).Take(40))
        {
            var tile = new StackPanel { Width = 116, Margin = new Thickness(4) };
            tile.Children.Add(new StickerImage(library.PathFor(pack, sticker), 82, true) { ToolTip = sticker.Name });
            tile.Children.Add(new TextBlock { Text = sticker.Name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = sticker.Name, HorizontalAlignment = HorizontalAlignment.Center });
            var edit = new Button { Content = "Sửa", Padding = new Thickness(4), Margin = new Thickness(0, 4, 0, 0) };
            edit.Click += (_, _) => Edit(pack, sticker); tile.Children.Add(edit); items.Children.Add(tile);
        }
        status.Text = $"{pack.Stickers.Count} sticker · {matches.Length} kết quả · Trang {page + 1}/{Math.Max(1, (matches.Length + 39) / 40)}";
    }
    private string? AskName(string title, string value)
    {
        var panel = Form(); panel.Children.Add(new TextBlock { Text = title, FontSize = 20, Margin = new Thickness(0, 0, 0, 10) }); var input = new TextBox { Text = value, MaxLength = 100 }; panel.Children.Add(input);
        return InAppDialog.Show(owner, title, panel, "Lưu", () => !string.IsNullOrWhiteSpace(input.Text) && !input.Text.Any(char.IsControl), input) ? input.Text.Trim() : null;
    }
    private StickerPack? Destination()
    {
        var panel = Form(); panel.Children.Add(new TextBlock { Text = "Chọn bộ sticker", FontSize = 20 });
        var choose = new ComboBox { ItemsSource = library.Packs(), SelectedItem = null, Margin = new Thickness(0, 12, 0, 8) }; StickerPackPresentation.Configure(choose, library);
        if (Selected is { } current) choose.SelectedItem = choose.Items.Cast<StickerPack>().FirstOrDefault(p => p.Id == current.Id);
        panel.Children.Add(choose); panel.Children.Add(new TextBlock { Text = "Hoặc nhập tên để tạo bộ mới" }); var name = new TextBox { MaxLength = 100, Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(name);
        if (!InAppDialog.Show(owner, "Chọn bộ sticker", panel, "Tiếp tục", () => !string.IsNullOrWhiteSpace(name.Text) || choose.SelectedItem is StickerPack)) return null;
        return !string.IsNullOrWhiteSpace(name.Text) ? new StickerPack { Name = StickerLibrary.CheckName(name.Text) } : choose.SelectedItem as StickerPack;
    }
    private void ImportImages()
    {
        var dialog = new OpenFileDialog { Filter = "Sticker PNG/GIF|*.png;*.gif", Multiselect = true, Title = "Chọn một hoặc nhiều sticker" }; if (dialog.ShowDialog(owner) != true) return;
        if (dialog.FileNames.Length > StickerLibrary.MaxStickers) throw new InvalidDataException("Mỗi bộ tối đa 500 sticker.");
        var pack = Destination(); if (pack is null) return;
        if (pack.Stickers.Count + dialog.FileNames.Length > StickerLibrary.MaxStickers) throw new InvalidDataException("Bộ này không đủ chỗ. Tối đa 500 sticker/bộ.");
        var panel = Form(); panel.Children.Add(new TextBlock { Text = "Đặt tên từng sticker để tìm kiếm", FontSize = 20, Margin = new Thickness(0, 0, 0, 12) });
        var names = new List<(string Path, TextBox Name)>();
        foreach (var path in dialog.FileNames)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new StickerImage(path, 45, true)); var name = new TextBox { Text = Path.GetFileNameWithoutExtension(path), MaxLength = 100, Width = 310, VerticalAlignment = VerticalAlignment.Center }; row.Children.Add(name); panel.Children.Add(row); names.Add((path, name));
        }
        if (!InAppDialog.Show(owner, "Import sticker", panel, "Nhập vào bộ", () => names.All(n => !string.IsNullOrWhiteSpace(n.Name.Text) && !n.Name.Text.Any(char.IsControl)))) return;
        var images = new List<(string Name, byte[] Bytes)>(); long total = 0;
        foreach (var entry in names)
        {
            total += new FileInfo(entry.Path).Length; if (total > StickerLibrary.MaxPackBytes) throw new InvalidDataException("Lần nhập vượt 128 MB. Hãy chia thành nhiều bộ.");
            images.Add((entry.Name.Text, StickerImage.ValidateFile(entry.Path)));
        }
        library.AddRange(pack, images); Refresh(pack.Id);
    }
    private void CreateSticker()
    {
        var dialog = new OpenFileDialog { Filter = "Ảnh tĩnh|*.png;*.jpg;*.jpeg;*.bmp", Title = "Chọn ảnh để chế tác" }; if (dialog.ShowDialog(owner) != true) return;
        var editor = new StickerEditor(AvatarEditor.Load(dialog.FileName)); byte[]? output = null;
        if (!InAppDialog.Show(owner, "Chế tác sticker", editor, "Dùng sticker này", () => { try { output = editor.Export(); return true; } catch (InvalidDataException) { return false; } }, width: 780)) return;
        var name = AskName("Tên sticker", Path.GetFileNameWithoutExtension(dialog.FileName)); if (name is null) return;
        var pack = Destination(); if (pack is null) return;
        library.Add(pack, name, output!); Refresh(pack.Id);
    }
    private void Edit(StickerPack pack, StickerItem item)
    {
        try
        {
            var panel = Form(); panel.Children.Add(new StickerImage(library.PathFor(pack, item), 150));
            var name = new TextBox { Text = item.Name, MaxLength = 100, Margin = new Thickness(0, 10, 0, 8) }; panel.Children.Add(name);
            panel.Children.Add(new TextBlock { Text = "Từ khóa gợi ý (ngăn cách bằng dấu phẩy)" }); var keywords = new TextBox { Text = string.Join(", ", item.Keywords), MaxLength = 2000, Margin = new Thickness(0, 4, 0, 8) }; panel.Children.Add(keywords);
            panel.Children.Add(new TextBlock { Text = "Vị trí trong bộ" }); var position = new ComboBox { ItemsSource = Enumerable.Range(1, pack.Stickers.Count).ToArray(), SelectedIndex = pack.Stickers.IndexOf(item), Margin = new Thickness(0, 4, 0, 8) }; panel.Children.Add(position);
            var remove = new CheckBox { Content = "Xóa sticker này khỏi bộ" }; panel.Children.Add(remove);
            if (!InAppDialog.Show(owner, "Sửa sticker", panel, "Lưu", () => remove.IsChecked == true || !string.IsNullOrWhiteSpace(name.Text) && !name.Text.Any(char.IsControl))) return;
            var terms = keywords.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList(); if (terms.Count > 40) throw new InvalidDataException("Tối đa 40 từ khóa."); foreach (var term in terms) StickerLibrary.CheckName(term);
            pack.Stickers.Remove(item); if (remove.IsChecked != true) { item.Name = name.Text.Trim(); item.Keywords = terms; pack.Stickers.Insert(Math.Clamp(position.SelectedIndex, 0, pack.Stickers.Count), item); } library.Save(pack); Render();
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}

internal sealed class StickerPalette : StackPanel
{
    internal StickerPalette(StickerLibrary library, Action<StickerPack, StickerItem> send, Action workshop)
    {
        SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush"); Width = 350;
        Children.Add(new TextBlock { Text = "Sticker", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var search = new TextBox { ToolTip = "Tìm sticker, tên bộ hoặc từ khóa", MinHeight = 32, Height = 34, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 8) }; Children.Add(search);
        System.Windows.Automation.AutomationProperties.SetName(search, "Tìm sticker");
        var packs = library.Packs(); var all = packs.SelectMany(p => p.Stickers.Select(s => (Pack: p, Sticker: s))).ToArray();
        var heading = new TextBlock { Margin = new Thickness(0, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis }; Children.Add(heading);
        var grid = new WrapPanel(); var scroll = new ScrollViewer { Content = grid, Height = 264, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Children.Add(scroll);
        var empty = new TextBlock { TextWrapping = TextWrapping.Wrap }; Children.Add(empty);
        var strip = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; Children.Add(strip);
        var recent = new Button { Content = "◷", Width = 36, Height = 40, Padding = new Thickness(0), ToolTip = "Dùng gần đây" }; DockPanel.SetDock(recent, Dock.Left); strip.Children.Add(recent);
        var manage = new Button { Content = "+", Width = 32, Height = 40, Padding = new Thickness(0), ToolTip = "Quản lý và chế tác sticker" }; DockPanel.SetDock(manage, Dock.Right); strip.Children.Add(manage); manage.Click += (_, _) => workshop();
        var tabs = new ListBox { ItemsSource = packs, Height = 48, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        tabs.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        var itemsPanel = new FrameworkElementFactory(typeof(VirtualizingStackPanel)); itemsPanel.SetValue(VirtualizingStackPanel.OrientationProperty, Orientation.Horizontal); tabs.ItemsPanel = new ItemsPanelTemplate(itemsPanel);
        VirtualizingStackPanel.SetIsVirtualizing(tabs, true); VirtualizingStackPanel.SetVirtualizationMode(tabs, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(tabs, true); ScrollViewer.SetHorizontalScrollBarVisibility(tabs, ScrollBarVisibility.Hidden); ScrollViewer.SetVerticalScrollBarVisibility(tabs, ScrollBarVisibility.Disabled);
        var cover = new FrameworkElementFactory(typeof(Image)); cover.SetValue(FrameworkElement.WidthProperty, 32d); cover.SetValue(FrameworkElement.HeightProperty, 32d); cover.SetBinding(Image.SourceProperty, new Binding { Converter = new StickerPackPresentation(library, true) }); cover.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Name"));
        tabs.ItemTemplate = new DataTemplate { VisualTree = cover }; strip.Children.Add(tabs);
        System.Windows.Automation.AutomationProperties.SetName(tabs, "Chọn bộ sticker"); System.Windows.Automation.AutomationProperties.SetName(manage, "Quản lý sticker"); System.Windows.Automation.AutomationProperties.SetName(recent, "Sticker dùng gần đây");
        var container = new Style(typeof(ListBoxItem)); container.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(3))); container.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Name"))); container.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("Name"))); tabs.ItemContainerStyle = container;
        tabs.PreviewMouseWheel += (_, e) => { int index = Math.Clamp(tabs.SelectedIndex + (e.Delta < 0 ? 1 : -1), 0, Math.Max(0, packs.Count - 1)); if (packs.Count > 0) { tabs.SelectedIndex = index; tabs.ScrollIntoView(tabs.SelectedItem); } e.Handled = true; };
        bool showingRecent = App.Settings.RecentStickers.Any(key => all.Any(x => key == x.Pack.Id + "/" + x.Sticker.Id));
        (StickerPack Pack, StickerItem Sticker)[] matches = []; int shown = 0;
        void More()
        {
            foreach (var (pack, sticker) in matches.Skip(shown).Take(40))
            {
                var button = new Button { Content = new StickerImage(library.PathFor(pack, sticker), 68, true), Width = 80, Height = 80, Padding = new Thickness(3), BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = sticker.Name + " · " + pack.Name };
                System.Windows.Automation.AutomationProperties.SetName(button, "Gửi sticker " + sticker.Name); button.Click += (_, _) => send(pack, sticker); grid.Children.Add(button); shown++;
            }
        }
        void Render()
        {
            grid.Children.Clear(); shown = 0; string query = StickerLibrary.SearchKey(search.Text.Trim());
            var result = all.Where(x => query.Length > 0 ? StickerLibrary.SearchKey(x.Sticker.Name + " " + x.Pack.Name + " " + string.Join(" ", x.Sticker.Keywords)).Contains(query) : showingRecent ? App.Settings.RecentStickers.Contains(x.Pack.Id + "/" + x.Sticker.Id) : tabs.SelectedItem is StickerPack p && p.Id == x.Pack.Id);
            if (query.Length == 0 && showingRecent) result = result.OrderBy(x => App.Settings.RecentStickers.IndexOf(x.Pack.Id + "/" + x.Sticker.Id));
            matches = result.ToArray(); heading.Text = query.Length > 0 ? "Kết quả tìm kiếm" : showingRecent ? "Dùng gần đây" : (tabs.SelectedItem as StickerPack)?.Name ?? "Sticker";
            empty.Text = matches.Length > 0 ? "" : showingRecent && query.Length == 0 ? "Chọn một bộ bên dưới để gửi sticker." : "Không tìm thấy sticker phù hợp."; empty.Visibility = matches.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            More(); scroll.ScrollToTop();
        }
        scroll.ScrollChanged += (_, e) => { if (e.VerticalChange > 0 && scroll.VerticalOffset + scroll.ViewportHeight >= scroll.ExtentHeight - 80 && shown < matches.Length) More(); };
        recent.Click += (_, _) => { showingRecent = true; tabs.SelectedIndex = -1; search.Clear(); Render(); };
        tabs.SelectionChanged += (_, _) => { if (tabs.SelectedIndex < 0) return; showingRecent = false; search.Clear(); Render(); };
        search.TextChanged += (_, _) => Render(); if (!showingRecent && packs.Count > 0) tabs.SelectedIndex = 0; Render();
    }
}

internal sealed class StickerPackPresentation(StickerLibrary library, bool thumbnail) : IValueConverter
{
    internal static void Configure(ComboBox combo, StickerLibrary library)
    {
        var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(FrameworkElement.WidthProperty, 28d); image.SetValue(FrameworkElement.HeightProperty, 28d); image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0)); image.SetBinding(Image.SourceProperty, new Binding { Converter = new StickerPackPresentation(library, true) }); row.AppendChild(image);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); name.SetBinding(TextBlock.TextProperty, new Binding { Converter = new StickerPackPresentation(library, false) }); row.AppendChild(name);
        combo.ItemTemplate = new DataTemplate { VisualTree = row }; combo.MaxDropDownHeight = 360;
        VirtualizingStackPanel.SetIsVirtualizing(combo, true); VirtualizingStackPanel.SetVirtualizationMode(combo, VirtualizationMode.Recycling);
    }
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!thumbnail) return value is StickerPack pack ? $"{pack.Name} · {pack.Stickers.Count}" : value?.ToString();
        if (value is StickerPack { Stickers.Count: > 0 } p)
        {
            try { return ClipboardImages.LoadLocal(library.PathFor(p, p.Stickers[0]), 40); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        return null;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

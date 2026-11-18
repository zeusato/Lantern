using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.App;
using Lantern.Core;

internal sealed partial class UiHarness
{
    private void CheckGroupCreation(MainWindow window, Action<FrameworkElement, string> preview)
    {
        var peers = Enumerable.Range(1, 40).Select(index => new Peer { Id = index.ToString("X64"), Name = index == 1 ? "Nguyễn An" : index == 2 ? "Trần Bình" : "Thành viên " + index, Trusted = true }).ToArray();
        var form = (GroupCreationForm)Call(window, "BuildGroupCreationForm", (object)peers)!;
        void Layout() { form.Measure(new Size(500, 650)); form.Arrange(new Rect(0, 0, 500, 650)); form.UpdateLayout(); }
        Layout();
        Check(!form.Validate() && Visuals<Button>(form).Any(button => button.Content as string == "Chọn ảnh nhóm"), "Group creation includes a required name, photo chooser and member picker");
        form.NameInput.Text = "Nhóm thiết kế"; Check(!form.Validate(), "A named group still needs at least two selected members");
        var picker = form.Members;
        picker.Search.Text = "nguyen an"; Layout();
        var first = Visuals<CheckBox>(picker).Single(); Check((string)first.Tag == peers[0].Id, "Member search supports Vietnamese names without accents"); first.IsChecked = true;
        picker.Search.Text = "Bình"; Layout(); Visuals<CheckBox>(picker).Single().IsChecked = true;
        Check(form.Validate() && picker.SelectedIds.ToHashSet().SetEquals(peers.Take(2).Select(peer => peer.Id)), "Selections accumulate across different search queries");
        picker.Search.Text = "không có kết quả"; Layout();
        Check(!Visuals<CheckBox>(picker).Any() && picker.SelectedIds.Length == 2 && Visuals<WrapPanel>(picker).Single(panel => panel.Name == "SelectedMembers").Children.Count == 2, "An empty search result preserves the selected-member list");
        picker.Search.Clear(); Layout(); Check(Visuals<CheckBox>(picker).Count(choice => choice.IsChecked == true) == 2, "Clearing search restores checked state for previously selected members");
        picker.Search.Text = "Bình"; Layout();
        Visuals<WrapPanel>(picker).Single(panel => panel.Name == "SelectedMembers").Children.OfType<Button>().Single(button => (string)button.Tag == peers[0].Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(picker.SelectedIds.SequenceEqual(new[] { peers[1].Id }) && picker.Search.Text == "Bình", "Selected members can be removed without changing the current search");
        picker.Search.Text = "Nguyễn"; Layout(); Visuals<CheckBox>(picker).Single().IsChecked = true; picker.Search.Clear();
        var limited = new GroupMemberPicker(peers, 2); limited.Measure(new Size(500, 400)); limited.Arrange(new Rect(0, 0, 500, 400)); limited.UpdateLayout();
        foreach (var choice in Visuals<CheckBox>(limited).Take(2)) choice.IsChecked = true;
        Check(Visuals<CheckBox>(limited).Where(choice => choice.IsChecked != true).All(choice => !choice.IsEnabled), "Selection stops at the group's remaining capacity while selected members remain removable");
        var pixels = new byte[400 * 200 * 4];
        for (int y = 0; y < 200; y++) for (int x = 0; x < 400; x++) { var offset = (y * 400 + x) * 4; pixels[offset] = (byte)(x / 2); pixels[offset + 1] = (byte)y; pixels[offset + 2] = 50; pixels[offset + 3] = 255; }
        var editor = new AvatarEditor(BitmapSource.Create(400, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 1600));
        form.Avatar = editor.Export(); var original = form.Avatar; Visuals<Slider>(editor).Single().Value = 2;
        Check(AvatarImages.Get(form.Avatar) is { PixelWidth: 192, PixelHeight: 192 } && editor.Export() != original, "Group photos use a selectable square crop with zoom");
        form.AvatarHost.Child = (FrameworkElement)Call(window, "AvatarSettings", (Func<string>)(() => form.Avatar), (Action<string>)(value => form.Avatar = value), "Chọn ảnh nhóm")!;
        Layout(); preview(form, "create-group.png");
        var node = Field<PeerNode>(window, "node"); var store = Field<LocalStore>(window, "store");
        var group = new ChatGroup { Name = form.NameInput.Text, Avatar = form.Avatar, OwnerId = node.Identity.Id, Members = picker.SelectedIds.Append(node.Identity.Id).ToList() }; store.SaveGroup(group);
        typeof(MainWindow).GetField("selectedId", Private)!.SetValue(window, group.Id); Call(window, "Refresh");
        Check(((ListBox)window.FindName("ContactsList")).Items.OfType<ContactRow>().Single(row => row.Id == group.Id).AvatarSource is not null && ((Image)window.FindName("DetailAvatar")).Source is not null, "Created group photos appear in the conversation list and group details");
    }
}

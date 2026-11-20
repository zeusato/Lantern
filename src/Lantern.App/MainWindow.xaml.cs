using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lantern.Core;
using Microsoft.Win32;

namespace Lantern.App;

public sealed record ContactRow(string Id, string Name, string Initials, string Subtitle, bool IsGroup, int Unread = 0, string Presence = "offline", string Avatar = "")
{
    public ImageSource? AvatarSource => AvatarImages.Get(Avatar);
    public Visibility InitialsVisibility => AvatarSource is null ? Visibility.Visible : Visibility.Collapsed;
    public string UnreadLabel => Unread > 99 ? "99+" : Unread.ToString();
    public Visibility UnreadVisibility => Unread > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PresenceVisibility => IsGroup ? Visibility.Collapsed : Visibility.Visible;
    public string PresenceColor => Presence switch { "online" => "#20A464", "idle" => "#DCA822", "dnd" => "#D94A53", _ => "#8D98A0" };
    public string PresenceLabel => Presence switch { "online" => "Đang online", "idle" => "Vắng mặt · 15 phút", "dnd" => "Không làm phiền", _ => "Offline" };
}

public partial class MainWindow : Window
{
    private Identity identity = null!;
    private LocalStore store = null!;
    private PeerNode node = null!;
    private DispatcherTimer refreshTimer = null!;
    private DispatcherTimer sessionTimer = null!;
    private DispatcherTimer updateTimer = null!;
    private readonly UpdateController updates = new();
    private readonly LinkPreviews linkPreviews = new();
    private int sessionTicks;
    private readonly Dictionary<string, (TextBlock Label, DateTimeOffset Deadline)> expiryLabels = [];
    private TrayIcon? tray;
    private int taskbarUnread = -1;
    private readonly Dictionary<string, (ProgressBar Bar, TextBlock Label)> progress = [];
    private readonly Dictionary<string, RichTextContent> drafts = [];
    private readonly Dictionary<string, string> replyDrafts = [];
    private string selectedId = "";
    private string renderKey = "";
    private bool refreshPending, refreshing, closing, detailsVisible;
    private int messageLimit = 150;
    private bool markingRead;
    private bool replaceEmoticons;
    private bool readAgain;
    private System.Windows.Controls.Primitives.Popup? emojiPopup;
    private readonly List<MediaElement> players = [];
    private readonly List<Window> mediaWindows = [];
    private readonly HashSet<string> unlockedConversations = [];
    private bool appLocking;
    private bool exitRequested, closeReady;
    private LoginView? loginView;
    private bool ConversationProtected => selectedId.Length > 0 && App.Vault.IsConversationLocked(selectedId) && !unlockedConversations.Contains(selectedId);
    private readonly CancellationTokenSource uiLifetime = new();
    public MainWindow(bool requireLogin = false)
    {
        InitializeComponent();
        TaskbarItemInfo = new System.Windows.Shell.TaskbarItemInfo();
        EmojiButton.Content = EmojiAssets.Icon("🙂", 25);
        PreviewKeyDown += WindowKeyDown;
        if (requireLogin) { appLocking = true; ShowLogin(initial: true); }
        else InitializeSession();
    }
    private void InitializeSession()
    {
        identity = new Identity(App.DataDirectory, App.Settings.Name, App.Vault);
        store = new LocalStore(App.DataDirectory, App.Vault); node = new PeerNode(identity, store) { AutoReceiveImages = true };
        InitializeMobile();
        node.Changed += QueueRefresh;
        node.GamesChanged += QueueRefresh;
        node.MessageReceived += message => Dispatcher.BeginInvoke(() => NotifyMessage(message));
        node.Notice += message => Dispatcher.BeginInvoke(() => ShowToast(message));
        node.TransferProgress += update => Dispatcher.BeginInvoke(() =>
        {
            if (!progress.TryGetValue(update.MessageId, out var view)) return;
            view.Bar.Value = update.Total == 0 ? 100 : update.Bytes * 100d / update.Total;
            view.Label.Text = $"{FormatBytes(update.Bytes)} / {FormatBytes(update.Total)}";
        });
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        refreshTimer.Tick += (_, _) => { if (refreshPending && !closing) { refreshPending = false; Refresh(); } };
        refreshTimer.Start();
        sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        sessionTimer.Tick += async (_, _) => { if (hasTimedPolls) refreshPending = true; foreach (var view in expiryLabels.Values) view.Label.Text = "Tự hủy sau " + Duration(Math.Max(0, (int)Math.Ceiling((view.Deadline - DateTimeOffset.UtcNow).TotalSeconds))); if (++sessionTicks % 30 == 0 && !closing) await UpdatePresence(); };
        sessionTimer.Start();
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) }; updateTimer.Tick += async (_, _) => { if (App.Settings.AutoUpdates) await updates.CheckAsync(uiLifetime.Token); }; updateTimer.Start();
        updates.Ready += () => Dispatcher.BeginInvoke(() => { UpdateButton.ToolTip = updates.Status; UpdateButton.SetResourceReference(Control.BackgroundProperty, "AccentSoftBrush"); ShowToast(updates.Status); tray?.ShowMessage("", "Lantern · Có bản cập nhật", "Đã tải bản mới. Mở mục Cập nhật để cài và khởi động lại."); });
        InitializeFormattingToolbar();
        CommandManager.AddPreviewExecutedHandler(Composer, ComposerPaste);
        CommandManager.AddPreviewCanExecuteHandler(Composer, ComposerCanPaste);
        Activated += (_, _) => { refreshPending = true; if (appLocking) loginView?.FocusPassword(); };
        StateChanged += (_, _) => refreshPending = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemThemeChanged;
    }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => { if (App.Settings.Theme == "system" || SystemParameters.HighContrast) { App.ApplyTheme(App.Settings.Theme); renderKey = ""; Refresh(); } });
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (node is null) return;
        await StartSession();
    }
    private async Task StartSession()
    {
        try
        {
            if (!App.Settings.Onboarded)
            {
                var panel = new StackPanel();
                panel.Children.Add(Paragraph("Chào mừng đến với Lantern", 27, true));
                panel.Children.Add(Paragraph("Một nơi trò chuyện và chia sẻ cùng những người ở gần. Chỉ cần chung mạng LAN.", muted: true));
                panel.Children.Add(Paragraph("Tên hiển thị", bold: true));
                var name = new TextBox { Text = App.Settings.Name, MaxLength = 80 }; panel.Children.Add(name);
                panel.Children.Add(Paragraph("Tên này xuất hiện khi người khác tìm máy. Danh tính riêng sẽ tự được tạo và bảo vệ bằng tài khoản Windows.", muted: true));
                if (ShowDialog("Bắt đầu với Lantern", panel, "Bắt đầu", () => !string.IsNullOrWhiteSpace(name.Text)))
                { App.Settings.Name = name.Text.Trim(); identity.Name = App.Settings.Name; App.Settings.Onboarded = true; App.SaveSettings(); }
                else { Close(); return; }
            }
            await node.StartAsync(App.TestMode ? 0 : App.Settings.Port, !App.TestMode, App.TestMode);
            await mobile.StartAsync(App.TestMode ? 0 : 45854, App.TestMode);
            await UpdatePresence();
            MyInitials.Text = Initials(identity.Name);
            NetworkStatus.Text = App.TestMode ? "Kiểm thử cục bộ · Kết nối mã hóa" : "LAN · Kết nối trực tiếp · Mã hóa TLS";
            Title = $"Lantern · {identity.Name}";
            tray = new TrayIcon(() => Dispatcher.BeginInvoke(RestoreFromTray), () => Dispatcher.BeginInvoke(LockApp), () => Dispatcher.BeginInvoke(ExitApplication), id => Dispatcher.BeginInvoke(() => OpenNotification(id)));
            Refresh();
            if (App.Settings.AutoUpdates && !App.TestMode) _ = updates.CheckAsync(uiLifetime.Token);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void QueueRefresh() => Dispatcher.BeginInvoke(() => refreshPending = true);
    private void RestoreFromTray()
    {
        if (closing) return;
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (appLocking) { Activate(); loginView?.FocusPassword(); return; }
        var dialog = OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible);
        if (dialog is not null) { if (dialog.WindowState == WindowState.Minimized) dialog.WindowState = WindowState.Normal; dialog.Activate(); }
        else Activate();
        refreshPending = true;
    }
    private void HideToTray()
    {
        ClearPinnedMessages();
        InAppDialog.CancelAll(this);
        if (emojiPopup is not null) emojiPopup.IsOpen = false;
        CloseMediaViews(); foreach (var player in players) player.Pause();
        Hide(); refreshPending = true;
    }
    private void ExitApplication()
    {
        if (closing) return;
        exitRequested = true; Close();
    }
    internal void PrepareForSystemShutdown() => exitRequested = true;
    private bool IsConversationVisible(string id) => !appLocking && IsActive && IsVisible && WindowState != WindowState.Minimized && selectedId == id && !ConversationProtected;
    private void NotifyMessage(ChatMessage message)
    {
        if (closing || !App.Settings.MessageToasts || App.Settings.DoNotDisturb || IsConversationVisible(message.ConversationId)) return;
        bool locked = appLocking || node.ConversationRules(message.ConversationId).CaptureProtected || App.Vault.IsConversationLocked(message.ConversationId) && !unlockedConversations.Contains(message.ConversationId);
        var name = node.FindPeer(message.SenderId)?.DisplayName ?? "Tin nhắn mới";
        var notification = MessageNotifications.Create(message, name, App.Settings.MessageToastMode, locked);
        tray?.ShowMessage(message.ConversationId, notification.Title, notification.Body);
    }
    private void OpenNotification(string id)
    {
        RestoreFromTray();
        if (appLocking || !IsEnabled) return;
        if (id.Length == 0) { UpdatesClick(this, new RoutedEventArgs()); return; }
        ContactSearch.Clear(); Refresh(); ContactsList.SelectedItem = ContactsList.Items.OfType<ContactRow>().FirstOrDefault(row => row.Id == id);
    }
    private static string Initials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeLast(2).Select(x => char.ToUpperInvariant(x[0])));
    private static string FormatBytes(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB" : bytes < 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024):0.#} MB" : $"{bytes / (1024d * 1024 * 1024):0.##} GB";
    private static Brush Brush(string name) => (Brush)Application.Current.FindResource(name);
    private static TextBlock Paragraph(string text, double size = 14, bool bold = false, bool muted = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), LineHeight = size * 1.5 };
        block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); return block;
    }
    private void Refresh()
    {
        if (closing || !IsInitialized || node is null) return;
        var unread = store.UnreadCount(); tray?.UpdateUnread(unread);
        if (unread != taskbarUnread)
        {
            taskbarUnread = unread; TaskbarItemInfo.Overlay = unread > 0 ? TaskbarBadge.Create(unread) : null;
            TaskbarItemInfo.Description = unread > 0 ? $"Lantern · {unread:N0} tin chưa đọc" : "Lantern · Không có tin chưa đọc";
        }
        if (appLocking) return;
        MyAvatar.Source = AvatarImages.Get(store.Avatar);
        MyInitials.Visibility = MyAvatar.Source is null ? Visibility.Visible : Visibility.Collapsed;
        refreshing = true;
        var unreadCounts = store.UnreadCounts();
        var query = ContactSearch.Text.Trim();
        var rows = node.Peers.Where(p => !p.Blocked).Select(p => new ContactRow(p.Id, p.DisplayName, p.Initials, !p.Trusted ? p.Connected ? "Chờ xác minh" : "Máy trong LAN" : p.Connected ? "Đang online" : "Offline · Tin nhắn sẽ chờ gửi", false)).Concat(store.Groups().Select(g => new ContactRow(g.Id, g.Name, "#", $"Nhóm · {g.Members.Count} thành viên", true, Avatar: g.Avatar))).Where(r => r.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        rows = rows.Select(row => { var peer = node.FindPeer(row.Id); var presence = peer is { Connected: true } ? peer.Presence : "offline"; var result = row with { Unread = unreadCounts.GetValueOrDefault(row.Id), Presence = presence, Avatar = peer?.Avatar ?? row.Avatar }; return peer is null || !peer.Trusted ? result : result with { Subtitle = result.PresenceLabel }; }).ToList();
        var preferences = store.Preferences().ToDictionary(p => p.Id); var activity = store.ConversationActivity(); var groups = store.Groups().ToDictionary(g => g.Id);
        bool Online(ContactRow row) => row.IsGroup ? groups.TryGetValue(row.Id, out var g) && g.Members.Contains(identity.Id) && g.Members.Any(id => id != identity.Id && node.IsConnected(id)) : node.IsConnected(row.Id);
        rows = rows.Where(row => preferences.GetValueOrDefault(row.Id)?.Hidden != true).OrderByDescending(Online)
            .ThenByDescending(row => preferences.GetValueOrDefault(row.Id)?.Pinned == true).ThenByDescending(row => activity.GetValueOrDefault(row.Id)).ThenBy(row => row.Name)
            .Select(row => row with { Subtitle = (preferences.GetValueOrDefault(row.Id)?.Pinned == true ? "📌 · " : "") + (row.IsGroup && !groups[row.Id].Members.Contains(identity.Id) ? "Đã rời nhóm · Chỉ đọc" : row.Subtitle) + (preferences.GetValueOrDefault(row.Id) is { Category.Length: > 0 } p ? " · " + p.Category : "") }).ToList();
        ContactsList.ItemsSource = rows; ContactsList.SelectedItem = rows.Find(r => r.Id == selectedId);
        NoContacts.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OnlineCount.Text = $"{node.Peers.Count(p => p.Connected && !p.Blocked)} online";
        refreshing = false;
        RefreshConversation();
        MarkSelectedRead();
    }
    private async void MarkSelectedRead()
    {
        if (closing || !IsActive || !IsVisible || WindowState == WindowState.Minimized || selectedId.Length == 0 || ConversationProtected || appLocking || captureUnavailable) return;
        if (markingRead) { readAgain = true; return; }
        markingRead = true;
        try { await node.MarkReadAsync(selectedId); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        finally { markingRead = false; if (!closing) { tray?.UpdateUnread(store.UnreadCount()); if (readAgain) { readAgain = false; refreshPending = true; } } }
    }
    private Dictionary<string, string> renderedSenderAvatars = [];
    private void RefreshConversation()
    {
        if (selectedId.Length == 32 && !store.Groups().Any(g => g.Id == selectedId))
        { CloseMediaViews(); drafts.Remove(selectedId); replyDrafts.Remove(selectedId); selectedId = ""; Composer.Clear(); }
        bool protectedDetails = ConversationProtected || appLocking;
        if (protectedDetails) RefreshGamePanel(true);
        if (protectedDetails) ClearPinnedMessages();
        DetailsContent.IsEnabled = !protectedDetails; DetailsContent.Visibility = protectedDetails ? Visibility.Collapsed : Visibility.Visible;
        InfoTabs.IsEnabled = !protectedDetails; DetailsLocked.Visibility = protectedDetails ? Visibility.Visible : Visibility.Collapsed;
        var peer = node.FindPeer(selectedId); var group = store.Groups().Find(g => g.Id == selectedId);
        RenamePeerButton.Visibility = peer is null ? Visibility.Collapsed : Visibility.Visible;
        ShareContactButton.Visibility = peer is { Trusted: true, Blocked: false } ? Visibility.Visible : Visibility.Collapsed;
        AddGroupMembersButton.Visibility = group?.OwnerId == identity.Id ? Visibility.Visible : Visibility.Collapsed;
        AddGroupMembersButton.IsEnabled = !protectedDetails && group is { Members.Count: < 32 };
        LeaveGroupButton.Visibility = group is not null && group.Members.Contains(identity.Id) ? Visibility.Visible : Visibility.Collapsed;
        DeleteGroupButton.Visibility = group?.OwnerId == identity.Id ? Visibility.Visible : Visibility.Collapsed;
        PinnedMessagesButton.Content = $"Tin nhắn đã ghim · {store.PinnedMessages(selectedId).Count}";
        PinnedMessagesButton.IsEnabled = !protectedDetails && selectedId.Length > 0;
        ConversationRulesButton.IsEnabled = !protectedDetails && (group is not null || peer is { Trusted: true, Blocked: false });
        var seconds = store.Policies().Find(p => p.Id == selectedId)?.DisappearSeconds ?? 0;
        ExpirySettingsButton.Content = "Tin tự hủy · " + (seconds == 0 ? "Tắt" : Duration(seconds));
        ExpirySettingsButton.IsEnabled = group is not null || peer is { Trusted: true, Blocked: false };
        bool hasChat = peer is not null || group is not null;
        bool allowed = group is not null ? group.Members.Contains(identity.Id) : peer is { Trusted: true, Blocked: false };
        ConversationPasswordButton.IsEnabled = hasChat;
        LockConversationButton.Visibility = hasChat && App.Vault.IsConversationLocked(selectedId) && !ConversationProtected ? Visibility.Visible : Visibility.Collapsed;
        HeaderLockButton.Visibility = LockConversationButton.Visibility;
        Composer.IsEnabled = allowed; SendButton.IsEnabled = allowed; AttachButton.IsEnabled = allowed;
        PollButton.Visibility = selectedId.Length > 0 && store.Groups().Any(g => g.Id == selectedId && g.Members.Contains(identity.Id)) ? Visibility.Visible : Visibility.Collapsed; PollButton.IsEnabled = allowed;
        EmojiButton.IsEnabled = allowed && !ConversationProtected;
        StickerButton.IsEnabled = allowed && !ConversationProtected;
        PairBanner.Visibility = peer is { Trusted: false, Blocked: false } ? Visibility.Visible : Visibility.Collapsed;
        BlockButton.Visibility = peer is not null && peer.Trusted ? Visibility.Visible : Visibility.Collapsed;
        ChatTitle.Text = group?.Name ?? peer?.DisplayName ?? "Không gian của bạn";
        var presenceLabel = peer is null ? "" : new ContactRow("", "", "", "", false, Presence: peer.Connected ? peer.Presence : "offline").PresenceLabel;
        ChatSubtitle.Text = group is not null ? group.Members.Contains(identity.Id) ? $"Nhóm riêng · {group.Members.Count} thành viên" : "Đã rời nhóm · Lịch sử chỉ đọc" : peer is not null ? $"{presenceLabel}{(peer.Trusted ? " · Đã xác minh" : " · Chưa xác minh")}" : "Những cuộc trò chuyện ở ngay bên cạnh";
        DetailName.Text = group?.Name ?? peer?.DisplayName ?? "Kết nối riêng tư";
        DetailInitials.Text = group is not null ? "#" : peer?.Initials ?? "L";
        DetailAvatar.Source = AvatarImages.Get(group?.Avatar ?? peer?.Avatar ?? "");
        DetailInitials.Visibility = DetailAvatar.Source is null ? Visibility.Visible : Visibility.Collapsed;
        DetailStatus.Text = group is not null ? string.Join("\n", group.Members.Select(id => id == identity.Id ? identity.Name + " (bạn)" : node.FindPeer(id)?.DisplayName ?? "Thành viên chưa ghép đôi")) : peer is null ? "Dữ liệu truyền trực tiếp giữa các máy trong mạng LAN." : $"{peer.Address}:{peer.Port}\nID: {peer.Id[..12]}…\n{(peer.Trusted ? "Đã xác minh danh tính" : "Cần đối chiếu mã xác minh")}";
        MessageSearch.IsEnabled = !protectedDetails;
        RenamePeerButton.IsEnabled = !protectedDetails && peer is not null;
        ShareContactButton.IsEnabled = !protectedDetails && peer is { Trusted: true, Blocked: false };
        ConversationPasswordButton.IsEnabled = !protectedDetails && hasChat;
        ExpirySettingsButton.IsEnabled = !protectedDetails && allowed;
        BlockButton.IsEnabled = !protectedDetails;
        LockConversationButton.IsEnabled = !protectedDetails;
        SelectMessagesButton.IsEnabled = ClearHistoryButton.IsEnabled = !protectedDetails && hasChat;
        if (protectedDetails || selectionConversation != selectedId) ResetMessageSelection();
        RefreshReplyPreview();
        if (ConversationProtected || appLocking)
        {
            Composer.Clear(); MessageSearch.Clear(); ClearSharedContent(); RefreshSharedButton.IsEnabled = false;
            Composer.IsEnabled = false; SendButton.IsEnabled = false; AttachButton.IsEnabled = false; PairBanner.Visibility = Visibility.Collapsed;
            foreach (var player in players) player.Close(); players.Clear(); MessagesPanel.Children.Clear(); expiryLabels.Clear(); progress.Clear();
            WelcomePanel.Visibility = Visibility.Visible; WelcomeTitle.Text = "Cuộc trò chuyện\nđang được khóa."; WelcomeBody.Text = "Nhập mật khẩu riêng để xem nội dung\nvà tệp đã chia sẻ trong cuộc trò chuyện này."; WelcomeAction.Content = "Mở khóa hội thoại"; WelcomeAction.Visibility = Visibility.Visible; NoFiles.Visibility = Visibility.Collapsed; renderKey = "locked:" + selectedId; return;
        }
        if (!UpdateCaptureProtection()) { HideGamePanel(); return; }
        RefreshGamePanel(false);
        RefreshPinnedMessages();
        WelcomeAction.Content = "Tìm người trong LAN";
        var messages = hasChat ? store.Messages(selectedId, MessageSearch.Text, messageLimit) : [];
        WelcomePanel.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WelcomeTitle.Text = hasChat ? "Bắt đầu bằng\nmột lời chào." : "Một lời chào,\nngay trong mạng của bạn.";
        WelcomeBody.Text = hasChat ? "Gửi một tin nhắn, một tấm ảnh\nhay điều bạn muốn chia sẻ." : "Chat, gửi ảnh và chia sẻ tệp trực tiếp.\nMọi thứ ở lại trên máy của bạn.";
        WelcomeAction.Visibility = hasChat ? Visibility.Collapsed : Visibility.Visible;
        var senderAvatars = messages.Where(m => !m.Outgoing).Select(m => m.SenderId).Distinct().ToDictionary(id => id, id => node.FindPeer(id)?.Avatar ?? "");
        if (senderAvatars.Count != renderedSenderAvatars.Count || senderAvatars.Any(pair => !renderedSenderAvatars.TryGetValue(pair.Key, out var old) || old != pair.Value)) renderKey = "";
        renderedSenderAvatars = senderAvatars;
        var key = selectedId + "|" + store.MessageVersion(selectedId) + "|" + node.ConversationRules(selectedId).ForwardAllowed + "|" + string.Join(',', store.PinnedMessages(selectedId).Order()) + "|" + MessageSearch.Text + "|" + string.Join(";", messages.Select(m => m.Id + m.Status + m.Error + m.ExpiresAt + node.FindPeer(m.SenderId)?.DisplayName + Codec.Encode(m.Reactions) + Codec.Encode(m.Poll) + m.Poll?.IsClosed + Codec.Encode(m.PendingPollVote) + string.Join(',', m.Delivery.Values)));
        RefreshSharedContent();
        if (key == renderKey) return;
        renderKey = key; hasTimedPolls = false;
        var shouldScroll = ChatScroll.ScrollableHeight - ChatScroll.VerticalOffset < 80;
        foreach (var player in players) player.Close(); players.Clear();
        MessagesPanel.Children.Clear(); expiryLabels.Clear(); progress.Clear();
        if (messages.Count >= messageLimit)
        {
            var more = new Button { Content = "Xem tin nhắn trước đó", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) };
            more.Click += (_, _) => { messageLimit += 150; renderKey = ""; RefreshConversation(); }; MessagesPanel.Children.Add(more);
        }
        DateOnly? date = null;
        for (int index = 0; index < messages.Count; index++)
        {
            var msg = messages[index];
            var day = DateOnly.FromDateTime(msg.Time.LocalDateTime);
            if (date != day)
            {
                date = day; var divider = Paragraph(day == DateOnly.FromDateTime(DateTime.Now) ? "HÔM NAY" : day.ToString("dd / MM / yyyy"), 10, true, true); divider.HorizontalAlignment = HorizontalAlignment.Center; divider.Margin = new Thickness(0, 10, 0, 22); MessagesPanel.Children.Add(divider);
            }
            var joinsPrevious = index > 0 && MessageGrouping.CanJoin(messages[index - 1], msg);
            var joinsNext = index + 1 < messages.Count && MessageGrouping.CanJoin(msg, messages[index + 1]);
            MessagesPanel.Children.Add(MessageBubble(msg, joinsPrevious, joinsNext));
        }
        if (shouldScroll) Dispatcher.BeginInvoke(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }
    private FrameworkElement MessageBubble(ChatMessage msg, bool joinsPrevious = false, bool joinsNext = false)
    {
        var alignment = msg.Outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var hasReactions = msg.Reactions.Values.Any(value => value.Length > 0);
        var outer = new StackPanel { HorizontalAlignment = alignment, Margin = new Thickness(msg.Outgoing ? 30 : 0, 0, msg.Outgoing ? 0 : 30, joinsNext ? hasReactions ? 16 : 5 : 16), MaxWidth = 560 };
        var bubble = new Border { Name = "MessageSurface", CornerRadius = new CornerRadius(12, 12, msg.Outgoing ? 4 : 12, msg.Outgoing ? 12 : 4), Padding = new Thickness(12, 9, 12, 9), BorderThickness = new Thickness(msg.Outgoing ? 0 : 1) };
        bubble.SetResourceReference(Border.BackgroundProperty, msg.Outgoing ? "OwnBrush" : "SurfaceBrush"); bubble.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        if (msg.Sticker is not null) { bubble.Background = Brushes.Transparent; bubble.BorderThickness = new Thickness(0); }
        var content = new StackPanel(); bubble.Child = content;
        var messageMenu = MessageMenu(msg); bubble.ContextMenu = messageMenu;
        outer.ContextMenu = messageMenu;
        messageMenu.Opened += (_, _) => { if (!ScreenCaptureProtection.ProtectPopup(messageMenu, this)) messageMenu.IsOpen = false; };
        if (messageSelection)
        {
            var choose = new CheckBox { Content = "Chọn tin nhắn", IsChecked = selectedMessages.Contains(msg.Id), Margin = new Thickness(0, 0, 0, 6) };
            choose.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
            choose.Click += (_, _) => { if (choose.IsChecked == true) selectedMessages.Add(msg.Id); else selectedMessages.Remove(msg.Id); UpdateSelectionCount(); };
            content.Children.Add(choose);
        }
        if (!joinsPrevious && !msg.Outgoing && msg.GroupId.Length > 0) { var author = Paragraph(node.FindPeer(msg.SenderId)?.DisplayName ?? msg.SenderName, 11, true, true); author.Margin = new Thickness(0, 0, 0, 5); content.Children.Add(author); }
        var bubbleLayer = new Grid { HorizontalAlignment = alignment }; bubbleLayer.Children.Add(bubble); outer.Children.Add(bubbleLayer);
        var overlay = new Canvas { ClipToBounds = false }; Panel.SetZIndex(overlay, 2); bubbleLayer.Children.Add(overlay);
        var toolbar = new MessageActions(outer, () => BeginReply(msg), messageMenu);
        if (msg.Outgoing) Canvas.SetRight(toolbar, 0); else Canvas.SetLeft(toolbar, 0);
        Canvas.SetTop(toolbar, -18); overlay.Children.Add(toolbar);
        var reactions = new MessageReactions(outer, msg.Reactions, identity.Id, id => node.FindPeer(id)?.DisplayName ?? "Thành viên", emoji => ToggleReaction(msg.Id, emoji), id => AvatarImages.Get(id == identity.Id ? store.Avatar : node.FindPeer(id)?.Avatar ?? ""));
        Canvas.SetBottom(reactions, -11); overlay.Children.Add(reactions);
        if (msg.Outgoing) Canvas.SetRight(reactions, 0);
        else
        {
            void PlaceReactions() => Canvas.SetLeft(reactions, Math.Max(0, bubbleLayer.ActualWidth - reactions.ActualWidth));
            bubbleLayer.SizeChanged += (_, _) => PlaceReactions(); reactions.SizeChanged += (_, _) => PlaceReactions();
        }
        outer.Tag = msg.Id;
        if (msg.Forwarded) content.Children.Add(Paragraph("↪ Được chuyển tiếp", 10, muted: true));
        if (store.PinnedMessages(msg.ConversationId).Contains(msg.Id)) content.Children.Add(Paragraph("📌 Đã ghim", 10, muted: true));
        if (msg.ReplyToId.Length > 0)
        {
            var original = store.Message(msg.ReplyToId);
            if (original?.ConversationId != msg.ConversationId) original = null;
            var quoted = new Button { Content = new TextBlock { Text = original is null ? "Tin nhắn gốc không còn trên máy này" : Author(original) + "\n" + Snippet(original), TextWrapping = TextWrapping.Wrap, MaxWidth = 400 }, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10), ToolTip = "Đi tới tin nhắn gốc" };
            quoted.Click += (_, _) => GoToMessage(msg.ReplyToId); content.Children.Add(quoted);
        }
        if (msg.GameId.Length > 0) content.Children.Add(GameCard(msg));
        else if (msg.Poll is not null) content.Children.Add(PollCard(msg));
        else if (msg.Kind == "contact" && msg.Contact is { } card)
        {
            content.Children.Add(Paragraph("👤 " + (node.FindPeer(card.Id)?.DisplayName ?? card.Name), 16, true));
            content.Children.Add(Paragraph($"Danh tính: {card.Id[..12]}…", 11, muted: true));
            var connect = new Button { Content = "Kết nối với liên hệ", HorizontalAlignment = HorizontalAlignment.Left };
            connect.Click += async (_, _) => { try { if (card.Id == identity.Id) { ShowToast("Đây là liên hệ của bạn."); return; } await node.ConnectAsync(card.Address, card.Port, card.Id); ContactSearch.Clear(); Refresh(); ContactsList.SelectedItem = ContactsList.Items.OfType<ContactRow>().FirstOrDefault(r => r.Id == card.Id); ShowToast("Đối chiếu mã xác minh để tin cậy liên hệ mới."); } catch (Exception ex) { ShowError(ex); } }; content.Children.Add(connect);
        }
        else if (msg.Kind == "text")
        {
            var text = RichDocuments.MessageText(msg, OpenChatLink); text.ContextMenu = messageMenu; content.Children.Add(text);
        }
        else if (msg.Sticker is not null && File.Exists(msg.FilePath))
        {
            var sticker = new StickerImage(msg.FilePath, 160) { ToolTip = msg.Sticker.Name, ContextMenu = messageMenu };
            content.Children.Add(sticker);
        }
        else
        {
            var available = File.Exists(msg.FilePath);
            if (available && msg.Kind == "image")
            {
                try
                {
                    var bitmap = ClipboardImages.LoadLocal(msg.FilePath, 640);
                    var image = new Image { Source = bitmap, ContextMenu = messageMenu, MaxWidth = 380, MaxHeight = 280, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand };
                    image.MouseLeftButtonUp += (_, _) => ShowImage(msg); content.Children.Add(image);
                }
                catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException) { content.Children.Add(Paragraph("Định dạng ảnh này cần mở bằng ứng dụng khác.", 12, muted: true)); }
            }
            if (available && msg.Kind == "video") content.Children.Add(VideoPlayer(msg));
            var name = Paragraph(msg.FileName, 13, true); name.Margin = new Thickness(0, 0, 0, 4); content.Children.Add(name);
            var info = Paragraph(FormatBytes(msg.FileSize), 11, muted: true); info.Margin = new Thickness(0, 0, 0, 8); content.Children.Add(info);
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, Width = 200, HorizontalAlignment = HorizontalAlignment.Left, Value = available ? 100 : 0, Visibility = available ? Visibility.Collapsed : Visibility.Visible, Margin = new Thickness(0, 0, 0, 8) }; content.Children.Add(bar); progress[msg.Id] = (bar, info);
            var buttons = new WrapPanel(); content.Children.Add(buttons);
            if (available)
            {
                AddButton(buttons, HtmlPreview.Supports(msg) ? "Mở HTML" : "Mở", () => OpenAttachment(msg));
                AddButton(buttons, "Lưu thành…", () => SaveAttachment(msg));
                if (msg.FileName.EndsWith(".lanternstickers", StringComparison.OrdinalIgnoreCase)) AddButton(buttons, "Thêm bộ sticker", () => ImportReceivedPack(msg));
            }
            else if (!msg.Outgoing)
            {
                if (HtmlPreview.Supports(msg)) AddButton(buttons, "Mở HTML", async () => await OpenHtmlAsync(msg));
                if (msg.Kind == "image" && msg.Status is "offered" or "receiving")
                    content.Children.Add(Paragraph(node.IsConnected(msg.SenderId) ? "Đang tự động nhận ảnh…" : "Ảnh sẽ tự tải khi người gửi online.", 11, muted: true));
                else AddButton(buttons, msg.Status == "receiving" || File.Exists(msg.FilePath + ".part") ? "Tiếp tục tải" : "Nhận tệp", async () => { try { await node.AcceptFileAsync(msg.Id); } catch (Exception ex) { ShowError(ex); } });
                AddButton(buttons, "Từ chối", async () => { try { await node.DeclineFileAsync(msg.Id); } catch (Exception ex) { ShowError(ex); } });
            }
            if (!string.IsNullOrEmpty(msg.Error)) content.Children.Add(Paragraph(msg.Error, 11, muted: true));
        }
        // Keep timestamps/read receipts only on the last bubble. Pending/error state stays actionable.
        var needsAttention = msg.Status is "queued" or "sending" or "error" or "receiving" || msg.Error.Length > 0;
        if (!joinsNext || needsAttention)
        {
            var footer = Paragraph((joinsNext ? "" : msg.Time.LocalDateTime.ToString("HH:mm") + "  ·  ") + StatusText(msg), 10, muted: true);
            footer.Name = joinsNext ? "MessagePendingStatus" : "MessageFooter"; footer.HorizontalAlignment = HorizontalAlignment.Left; footer.Margin = new Thickness(0, 4, 16, 0); content.Children.Add(footer);
        }
        if (msg.DisappearAfterSeconds > 0)
        {
            var expiry = Paragraph(msg.ExpiresAt is { } deadline ? "Tự hủy sau " + Duration(Math.Max(0, (int)Math.Ceiling((deadline - DateTimeOffset.UtcNow).TotalSeconds))) : "Tự hủy sau " + Duration(msg.DisappearAfterSeconds) + " kể từ lúc đọc", 10, muted: true);
            expiry.Margin = new Thickness(0, 3, 0, 0); content.Children.Add(expiry);
            if (msg.ExpiresAt.HasValue) expiryLabels[msg.Id] = (expiry, msg.ExpiresAt.Value);
        }
        if (msg.Outgoing) return outer;
        var row = new Grid { HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 604, Tag = msg.Id, Margin = outer.Margin }; outer.Margin = new Thickness(0);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(outer, 1); row.Children.Add(outer);
        if (!joinsPrevious)
        {
            var peer = node.FindPeer(msg.SenderId);
            var name = peer?.DisplayName ?? msg.SenderName;
            var photo = AvatarImages.Get(peer?.Avatar ?? "");
            var avatar = new Border { Name = "MessageAvatar", Width = 34, Height = 34, CornerRadius = new CornerRadius(17), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = name };
            avatar.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            if (photo is not null) avatar.Background = new ImageBrush(photo) { Stretch = Stretch.UniformToFill };
            else { var initial = Paragraph(string.IsNullOrWhiteSpace(name) ? "?" : System.Globalization.StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant(), 14, true); initial.Margin = new Thickness(0); initial.HorizontalAlignment = HorizontalAlignment.Center; initial.VerticalAlignment = VerticalAlignment.Center; avatar.Child = initial; }
            System.Windows.Automation.AutomationProperties.SetName(avatar, "Ảnh đại diện " + name); row.Children.Add(avatar);
        }
        return row;
    }
    private async Task ToggleReaction(string id, string emoji)
    {
        if (appLocking || ConversationProtected) return;
        try
        {
            var reaction = store.Message(id)?.Reactions.GetValueOrDefault(identity.Id) == emoji ? "" : emoji;
            await node.ReactAsync(id, reaction);
            if (reaction.Length > 0) EmojiUsage.Remember(reaction);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private string Author(ChatMessage message) => message.SenderId == identity.Id ? "Bạn" : node.FindPeer(message.SenderId)?.DisplayName ?? message.SenderName;
    private static string Snippet(ChatMessage message)
    {
        var text = message.Sticker is not null ? "Sticker · " + message.Sticker.Name : message.Kind == "text" ? message.Text : message.Kind == "contact" ? "👤 " + message.Contact?.Name : "📎 " + message.FileName;
        text = text.Replace('\r', ' ').Replace('\n', ' '); return text.Length > 180 ? text[..180] + "…" : text;
    }
    private void BeginReply(ChatMessage message)
    {
        if (appLocking || ConversationProtected || message.ConversationId != selectedId) return;
        replyDrafts[selectedId] = message.Id; RefreshReplyPreview(); Composer.Focus();
    }
    private void RefreshReplyPreview()
    {
        var id = replyDrafts.GetValueOrDefault(selectedId, "");
        var message = id.Length > 0 ? store.Message(id) : null;
        if (message is null) replyDrafts.Remove(selectedId);
        ReplyBanner.Visibility = message is not null && !ConversationProtected && !appLocking ? Visibility.Visible : Visibility.Collapsed;
        ReplyPreview.Text = message is null ? "" : "Trả lời " + Author(message) + " · " + Snippet(message);
    }
    private void CancelReplyClick(object sender, RoutedEventArgs e) { replyDrafts.Remove(selectedId); RefreshReplyPreview(); Composer.Focus(); }
    private void GoToMessage(string id)
    {
        if (appLocking || ConversationProtected) return;
        if (store.Message(id)?.ConversationId != selectedId) { ShowToast("Tin nhắn gốc không còn trong hội thoại này."); return; }
        MessageSearch.Clear(); messageLimit = Math.Max(messageLimit, store.Messages(selectedId, limit: int.MaxValue).Count); renderKey = ""; RefreshConversation();
        var conversation = selectedId;
        Dispatcher.BeginInvoke(() => {
            if (appLocking || ConversationProtected || captureUnavailable || selectedId != conversation) return;
            if (MessagesPanel.Children.OfType<FrameworkElement>().FirstOrDefault(element => element.Tag as string == id) is { } row) HighlightMessage(row);
        }, DispatcherPriority.Loaded);
    }
    private static string StatusText(ChatMessage msg)
    {
        if (msg.Outgoing && msg.Delivery.Count > 1) return $"{msg.Delivery.Values.Count(s => s is "delivered" or "read")}/{msg.Delivery.Count} đã nhận";
        return msg.Status switch { "queued" => "Chờ gửi", "delivered" => msg.Outgoing ? "Đã nhận" : "Đã đến", "read" => "Đã đọc", "offered" => msg.Outgoing ? "Chờ nhận tệp" : "Tệp chờ tải", "receiving" => "Đang tải · có thể tiếp tục", "received" => "Đã lưu trên máy", "declined" => "Đã từ chối", "error" => "Cần tải lại", _ => "Đang gửi" };
    }
    private static void AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 3, 7, 0), Padding = new Thickness(10, 6, 10, 6), MinHeight = 32, FontSize = 12 };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    private FrameworkElement VideoPlayer(ChatMessage msg)
    {
        var stack = new StackPanel { Width = 300, Margin = new Thickness(0, 0, 0, 10) };
        var video = new MediaElement { Source = new Uri(msg.FilePath), Width = 300, Height = 180, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform };
        players.Add(video); stack.Children.Add(new Border { Background = Brushes.Black, Child = video, CornerRadius = new CornerRadius(6) });
        var controls = new WrapPanel(); bool playing = false;
        var play = new Button { Content = "▶ Phát", FontSize = 12, Padding = new Thickness(10, 4, 10, 4), MinHeight = 30, Margin = new Thickness(0, 6, 8, 0) };
        play.Click += (_, _) => { if (playing) video.Pause(); else video.Play(); playing = !playing; play.Content = playing ? "Ⅱ Tạm dừng" : "▶ Phát"; };
        video.MediaEnded += (_, _) => { playing = false; video.Position = TimeSpan.Zero; video.Stop(); play.Content = "▶ Phát lại"; };
        video.MediaFailed += (_, _) => { playing = false; play.Content = "Không phát được"; ShowToast("Windows chưa hỗ trợ codec của video này. Bấm Mở để dùng ứng dụng khác."); };
        controls.Children.Add(play);
        var seek = new Slider { Minimum = 0, Maximum = 100, Width = 160, Margin = new Thickness(0, 11, 0, 0), ToolTip = "Tua video" };
        System.Windows.Automation.AutomationProperties.SetName(seek, "Tua video");
        seek.PreviewMouseLeftButtonUp += (_, _) => { if (video.NaturalDuration.HasTimeSpan) video.Position = TimeSpan.FromMilliseconds(video.NaturalDuration.TimeSpan.TotalMilliseconds * seek.Value / 100); };
        seek.PreviewKeyUp += (_, _) => { if (video.NaturalDuration.HasTimeSpan) video.Position = TimeSpan.FromMilliseconds(video.NaturalDuration.TimeSpan.TotalMilliseconds * seek.Value / 100); };
        controls.Children.Add(seek); stack.Children.Add(controls); return stack;
    }
    private void ShowImage(ChatMessage msg)
    {
        try
        {
            var bitmap = ClipboardImages.LoadLocal(msg.FilePath, 0);
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(16) };
            var window = new Window { Title = msg.FileName, Owner = this, Width = 900, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = image };
            mediaWindows.Add(window); window.Closed += (_, _) => mediaWindows.Remove(window);
            ProtectOwnedWindow(window, msg.ConversationId);
            window.Show();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException) { ShowError(ex); }
    }
    private void OpenAttachment(ChatMessage msg)
    {
        try
        {
            if (HtmlPreview.Supports(msg)) { _ = OpenHtmlAsync(msg); return; }
            if (msg.Kind == "image") { ShowImage(msg); return; }
            var ext = Path.GetExtension(msg.FilePath).ToLowerInvariant();
            if (new[] { ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".msi", ".scr", ".lnk", ".hta", ".reg", ".url" }.Contains(ext))
            { ShowToast("Với tệp thực thi, hãy lưu ra thư mục và kiểm tra trước khi mở."); SaveAttachment(msg); return; }
            Process.Start(new ProcessStartInfo(msg.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void SaveAttachment(ChatMessage msg)
    {
        var dialog = new SaveFileDialog { FileName = msg.FileName, Filter = "Tất cả tệp|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(msg.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                if (msg.Kind == "image" && ClipboardImages.IsLegacyClipboardPath(msg.FilePath))
                { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(ClipboardImages.LoadLocal(msg.FilePath, 0))); using var output = File.Create(dialog.FileName); encoder.Save(output); }
                else File.Copy(msg.FilePath, dialog.FileName, true);
            }
            ShowToast("Đã lưu tệp.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ContactSelected(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || ContactsList.SelectedItem is not ContactRow row) return;
        if (selectedId.Length > 0) drafts[selectedId] = Composer.Export();
        if (selectedId != row.Id) { detailsVisible = false; UpdateLayoutWidths(); CloseMediaViews(); if (emojiPopup is not null) emojiPopup.IsOpen = false; }
        ClearStickerSuggestions(); selectedId = row.Id; RestoreComposerDraft(selectedId); MessageSearch.Clear(); messageLimit = 150; renderKey = ""; RefreshConversation(); ChatScroll.ScrollToEnd();
        if (ConversationProtected) TryUnlockConversation();
        MarkSelectedRead();
        FocusComposer();
    }
    private void ContactSearchChanged(object sender, TextChangedEventArgs e) { if (node is not null) Refresh(); }
    private void MessageSearchChanged(object sender, TextChangedEventArgs e) { if (node is not null && !ConversationProtected && !appLocking) { renderKey = ""; RefreshConversation(); } }
    private async void SendClick(object sender, RoutedEventArgs e) => await SendMessage();
    private async Task SendMessage()
    {
        if (node is null || appLocking || ConversationProtected || InAppDialog.IsOpen(this)) return;
        if (string.IsNullOrWhiteSpace(Composer.Text) || selectedId.Length == 0) return;
        try
        {
            if (App.Settings.AutoEmoticons) { replaceEmoticons = true; try { Composer.ReplaceEmoticons(true); } finally { replaceEmoticons = false; } }
            var draft = Composer.Export(); var text = draft.PlainText();
            draft.Validate(text);
            var conversation = selectedId; var reply = replyDrafts.GetValueOrDefault(conversation, "");
            await node.SendTextAsync(conversation, text, reply, deliverInBackground: true, richText: draft.HasFormatting ? draft : null);
            EmojiUsage.Remember(EmojiAssets.UsedIn(text)); drafts.Remove(conversation); replyDrafts.Remove(conversation);
            if (selectedId == conversation) { Composer.Clear(); RefreshReplyPreview(); ChatScroll.ScrollToEnd(); Composer.Focus(); }
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void ComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.X && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { e.Handled = true; FormatClick(this, new RoutedEventArgs()); return; }
        if (e.Key == Key.Enter && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control) { e.Handled = true; await SendMessage(); }
    }
    private void ComposerCanPaste(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || !Composer.IsEnabled || appLocking || ConversationProtected) return;
        try { if (ClipboardImages.ContainsImage()) { e.CanExecute = true; e.Handled = true; } } catch (System.Runtime.InteropServices.ExternalException) { }
    }
    private async void ComposerPaste(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || !Composer.IsEnabled || appLocking || ConversationProtected) return;
        try
        {
            if (!ClipboardImages.ContainsImage()) return;
            e.Handled = true; var bitmap = ClipboardImages.Read(); if (bitmap is null) return;
            await SendClipboardImage(bitmap);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task SendClipboardImage(BitmapSource bitmap)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lantern-clipboard-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "Anh-clipboard-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(file, FileMode.CreateNew)) encoder.Save(stream);
            await ShareFiles([file]);
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }
    private void ComposerTextChanged(object sender, TextChangedEventArgs e)
    {
        ScheduleStickerSuggestions();
        if (replaceEmoticons || e.UndoAction is UndoAction.Undo or UndoAction.Redo || !App.Settings.AutoEmoticons || Composer is null) return;
        replaceEmoticons = true;
        try { Composer.ReplaceEmoticons(false); }
        finally { replaceEmoticons = false; }
    }
    private void EmojiClick(object sender, RoutedEventArgs e)
    {
        if (!Composer.IsEnabled) return;
        var panel = new StackPanel { Width = 350 };
        panel.Children.Add(Paragraph("Biểu cảm", 21, true));
        if (emojiPopup is not null) emojiPopup.IsOpen = false;
        var popup = new System.Windows.Controls.Primitives.Popup { PlacementTarget = EmojiButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, StaysOpen = false, AllowsTransparency = true }; emojiPopup = popup;
        var border = new Border { Child = panel, Padding = new Thickness(18), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); popup.Child = border;
        panel.Children.Add(new EmojiPalette(face => { InsertEmoji(face); popup.IsOpen = false; Composer.Focus(); }));
        var auto = new CheckBox { Content = "Tự đổi ký hiệu thành emoji", IsChecked = App.Settings.AutoEmoticons, Margin = new Thickness(0, 14, 0, 8) };
        auto.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        auto.Click += (_, _) => { App.Settings.AutoEmoticons = auto.IsChecked == true; App.SaveSettings(); }; panel.Children.Add(auto);
        panel.Children.Add(EmojiAssets.MessageText(":) → 🙂   :D → 😃   ;) → 😉   <3 → ❤️"));
        popup.Opened += (_, _) => { if (!ScreenCaptureProtection.ProtectPopup(border, this)) popup.IsOpen = false; };
        popup.IsOpen = true;
    }
    private void InsertEmoji(string face)
    {
        var at = Composer.SelectionStart;
        var insertion = EmojiAssets.ForInsertion(Composer.Text, at, Composer.SelectionLength, face);
        if (App.Settings.AutoEmoticons) insertion = Emoticons.Replace(insertion, includeTrailing: false);
        Composer.SelectedText = insertion; Composer.CaretIndex = Math.Min(at + insertion.Length, Composer.Text.Length);
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (appLocking || node is null || InAppDialog.IsOpen(this)) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { if (ActualWidth < 1150 || GameHost.Visibility == Visibility.Visible) CompactDetails(); else { detailsVisible = true; UpdateLayoutWidths(); MessageSearch.Focus(); } e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { AttachClick(this, new RoutedEventArgs()); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { LockApp(); e.Handled = true; }
        if (e.Key == Key.Escape) { MessageSearch.Clear(); Composer.Focus(); }
    }
    private async void AttachClick(object sender, RoutedEventArgs e)
    {
        if (!AttachButton.IsEnabled) return;
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Chọn ảnh, video hoặc tệp để chia sẻ" };
        if (dialog.ShowDialog(this) == true) await ShareFiles(dialog.FileNames);
    }
    private async Task ShareFiles(IEnumerable<string> files)
    {
        if (node is null || appLocking || ConversationProtected || InAppDialog.IsOpen(this)) return;
        var target = selectedId; AttachButton.IsEnabled = false;
        try { foreach (var path in files.Take(20)) { ShowToast("Đang chuẩn bị " + Path.GetFileName(path)); await node.OfferFileAsync(target, path, uiLifetime.Token); } ShowToast("Đã gửi lời mời nhận tệp."); }
        catch (Exception ex) { ShowError(ex); }
        finally { RefreshConversation(); }
    }
    private void FileDragOver(object sender, DragEventArgs e) { e.Effects = !appLocking && !InAppDialog.IsOpen(this) && e.Data.GetDataPresent(DataFormats.FileDrop) && AttachButton.IsEnabled ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void FileDrop(object sender, DragEventArgs e) { if (AttachButton.IsEnabled && e.Data.GetData(DataFormats.FileDrop) is string[] files) await ShareFiles(files.Where(File.Exists)); }
    private async void PairClick(object sender, RoutedEventArgs e)
    {
        var peer = node.FindPeer(selectedId); if (peer is null) return;
        try
        {
            if (!peer.Connected) await node.ConnectAsync(peer.Address, peer.Port, peer.Id);
            if (!node.IsConnected(peer.Id)) { ShowToast("Chưa kết nối được. Kiểm tra IP, cổng và firewall mạng riêng."); return; }
            var panel = new StackPanel(); panel.Children.Add(Paragraph("Xác minh " + peer.Name, 23, true));
            panel.Children.Add(Paragraph("Mở Xác minh trên cả hai máy và đối chiếu trực tiếp hoặc qua một kênh đã tin cậy. Chỉ xác nhận khi toàn bộ mã dưới đây giống nhau.", muted: true));
            var code = new TextBox { Text = Identity.VerificationCode(identity.Id, peer.Id), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 19, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 20) }; panel.Children.Add(code);
            if (ShowDialog("Ghép đôi an toàn", panel, "Mã giống nhau · Tin cậy máy này")) { await node.TrustAsync(peer.Id); ShowToast("Đã lưu danh tính. Máy bên kia cũng cần xác nhận mã."); Refresh(); }
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ChatsClick(object sender, RoutedEventArgs e) { ContactSearch.Clear(); ContactsList.Focus(); }
    private async void AddPeerClick(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Kết nối một máy trong LAN", 23, true)); panel.Children.Add(Paragraph("Xem IP và cổng ở thanh trạng thái Lantern trên máy bên kia.", muted: true));
        var address = new TextBox { Text = "192.168.1.", MaxLength = 21 }; panel.Children.Add(Paragraph("Địa chỉ IP hoặc IP:cổng", bold: true)); panel.Children.Add(address);
        var port = new TextBox { Text = "45853", Margin = new Thickness(0, 0, 0, 12), MaxLength = 5 }; panel.Children.Add(Paragraph("Cổng", bold: true)); panel.Children.Add(port);
        if (!ShowDialog("Thêm máy", panel, "Kết nối")) return;
        try { ShowToast("Đang kết nối…"); var parts = address.Text.Trim().Split(':'); if (parts.Length > 2) throw new ArgumentException("Nhập IPv4 hoặc IPv4:cổng."); await node.ConnectAsync(parts[0], int.Parse(parts.Length == 2 ? parts[1] : port.Text)); Refresh(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void NewGroupClick(object sender, RoutedEventArgs e)
    {
        if (appLocking || node is null) return;
        var available = node.Peers.Where(p => p.Trusted && !p.Blocked).ToList();
        if (available.Count < 2) { ShowToast("Cần ghép đôi với ít nhất 2 người để tạo nhóm."); return; }
        var form = BuildGroupCreationForm(available);
        if (!ShowDialog("Tạo nhóm", form, "Tạo nhóm", form.Validate, form.NameInput, width: 570)) return;
        try { var group = await node.CreateGroupAsync(form.NameInput.Text, form.Members.SelectedIds, form.Avatar); selectedId = group.Id; renderKey = ""; Refresh(); FocusComposer(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private GroupCreationForm BuildGroupCreationForm(IEnumerable<Peer> available)
    {
        var form = new GroupCreationForm(available);
        form.AvatarHost.Child = AvatarSettings(() => form.Avatar, avatar => form.Avatar = avatar, "Chọn ảnh nhóm");
        return form;
    }
    private async void AddGroupMembersClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking) return;
        var group = store.Groups().Find(g => g.Id == selectedId);
        if (group is null || group.OwnerId != identity.Id) return;
        var available = node.Peers.Where(p => p.Trusted && !p.Blocked && !group.Members.Contains(p.Id)).ToArray();
        if (available.Length == 0) { ShowToast("Ghép đôi với người mới trước khi thêm vào nhóm."); return; }
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Thêm thành viên", 24, true));
        panel.Children.Add(Paragraph(group.Name + $" · {group.Members.Count}/32 thành viên", 13, muted: true));
        var choices = new GroupMemberPicker(available, 32 - group.Members.Count); panel.Children.Add(choices);
        var history = new CheckBox { Name = "IncludeGroupHistory", Content = "Cho người mới đọc tin nhắn cũ", IsChecked = false, Margin = new Thickness(0, 18, 0, 8) };
        history.SetResourceReference(Control.ForegroundProperty, "TextBrush"); panel.Children.Add(history);
        panel.Children.Add(Paragraph("Tắt: chỉ nhận tin từ lúc tham gia. Bật: đồng bộ lịch sử còn lưu khi các máy gửi tin kết nối lại; tin đã xóa hoặc hết hạn không được khôi phục.", 12, muted: true));
        panel.Children.Add(Paragraph("Các thành viên cần cập nhật Lantern và ghép đôi với nhau để chat, nhận lịch sử và tải tệp.", 12, muted: true));
        if (!ShowDialog("Thêm người vào nhóm", panel, "Thêm thành viên", () => choices.SelectedIds.Length > 0, choices.Search, width: 570)) return;
        try
        {
            await node.AddGroupMembersAsync(group.Id, choices.SelectedIds, history.IsChecked == true);
            Refresh(); ShowToast("Đã thêm thành viên và lưu quyền xem lịch sử.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ThemeClick(object sender, RoutedEventArgs e)
    {
        App.Settings.Theme = App.Settings.Theme == "dark" ? "light" : "dark"; App.ApplyTheme(App.Settings.Theme); App.SaveSettings(); renderKey = ""; Refresh();
    }
    private async void SettingsClick(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Hồ sơ của bạn", 25, true));
        var avatar = store.Avatar; panel.Children.Add(AvatarSettings(() => avatar, value => avatar = value));
        panel.Children.Add(Paragraph("Tên hiển thị", bold: true)); var name = new TextBox { Text = identity.Name, MaxLength = 80 }; panel.Children.Add(name);
        panel.Children.Add(Paragraph("Tên mới được cập nhật tới các máy đã ghép đôi. Nếu người khác đã đặt tên riêng cho bạn, họ vẫn thấy tên riêng đó.", 12, muted: true));
        var toasts = new CheckBox { Content = "Hiện thông báo Windows khi có tin nhắn mới", IsChecked = App.Settings.MessageToasts, Margin = new Thickness(0, 0, 0, 16) }; toasts.SetResourceReference(Control.ForegroundProperty, "TextBrush"); panel.Children.Add(toasts);
        var toastMode = new ComboBox { ItemsSource = new[] { "Hiện người gửi và nội dung", "Chỉ báo có tin nhắn mới" }, SelectedIndex = App.Settings.MessageToastMode == "private" ? 1 : 0, IsEnabled = App.Settings.MessageToasts, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 18) }; panel.Children.Add(toastMode);
        toasts.Click += (_, _) => toastMode.IsEnabled = toasts.IsChecked == true;
        var closeToTray = new CheckBox { Content = "Đóng cửa sổ xuống khay hệ thống", IsChecked = App.Settings.CloseToTray, Margin = new Thickness(0, 0, 0, 6) }; closeToTray.SetResourceReference(Control.ForegroundProperty, "TextBrush"); panel.Children.Add(closeToTray);
        panel.Children.Add(Paragraph("Khi bật, nút X chỉ ẩn cửa sổ; Lantern vẫn nhận tin nhắn. Nhấp đúp icon tray để mở lại. Chọn Thoát Lantern ở tray để thoát hẳn.", 12, muted: true));
        var startup = new CheckBox { Content = "Khởi động cùng Windows", IsChecked = App.Settings.StartWithWindows, Margin = new Thickness(0, 0, 0, 18) }; startup.SetResourceReference(Control.ForegroundProperty, "TextBrush"); panel.Children.Add(startup);
        panel.Children.Add(AppearancePicker());
        var workshop = new Button { Content = "Xưởng chế tác sticker", Margin = new Thickness(0, 0, 0, 12) }; workshop.Click += (_, _) => OpenStickerWorkshop(); panel.Children.Add(workshop);
        panel.Children.Add(Paragraph("Danh tính cố định", bold: true)); panel.Children.Add(new TextBox { Text = identity.Fingerprint, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 0, 0, 18) });
        var actions = new WrapPanel(); AddButton(actions, "Sao lưu danh tính", ExportIdentity); AddButton(actions, "Khôi phục danh tính", ImportIdentity); panel.Children.Add(actions);
        var backup = new Button { Content = "Xuất toàn bộ dữ liệu để chuyển máy", Margin = new Thickness(0, 8, 0, 12) }; backup.Click += ExportWorkspaceClick; panel.Children.Add(backup);
        panel.Children.Add(Paragraph("Xuất một file có mật khẩu gồm danh tính, lịch sử, danh bạ, nhóm, sticker và tệp đính kèm hiện có. Trên máy mới, chọn Khôi phục dữ liệu ở màn tạo workspace. Điện thoại cần ghép đôi lại.", 12, muted: true));
        panel.Children.Add(Paragraph("Nội dung lịch sử được mã hóa bằng khóa của mật khẩu ứng dụng. Tệp ảnh/video đã lưu chưa mã hóa riêng trên ổ đĩa. Mật khẩu hội thoại là lớp khóa xem cục bộ, không đặt mật khẩu ở máy người khác.", 12, muted: true));
        var changePassword = new Button { Content = "Đổi mật khẩu ứng dụng", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) }; changePassword.Click += (_, _) => ChangeAppPassword(); panel.Children.Add(changePassword);
        var folder = new Button { Content = "Mở thư mục dữ liệu", HorizontalAlignment = HorizontalAlignment.Left }; folder.Click += (_, _) => Process.Start(new ProcessStartInfo(App.DataDirectory) { UseShellExecute = true }); panel.Children.Add(folder);
        panel.Children.Add(Paragraph("Lantern " + UpdateController.CurrentVersion.ToString(3) + " · LAN IPv4 · TCP/TLS\nTối đa 32 thành viên/nhóm · Tệp tối đa 10 GB", 11, muted: true));
        var updateSettings = new Button { Content = "Kiểm tra và cài đặt cập nhật", Margin = new Thickness(0, 0, 0, 16) }; updateSettings.Click += UpdatesClick; panel.Children.Add(updateSettings);
        var mobileSettings = new Button { Content = "Điện thoại · Ghép đôi QR và quản lý thiết bị", Margin = new Thickness(0, 0, 0, 16) }; mobileSettings.Click += (_, _) => MobileSettings(); panel.Children.Add(mobileSettings);
        foreach (var blocked in node.Peers.Where(p => p.Blocked))
        {
            var unblock = new Button { Content = "Bỏ chặn: " + blocked.DisplayName, Margin = new Thickness(0, 3, 0, 3) };
            unblock.Click += (_, _) => { node.Unblock(blocked.Id); unblock.IsEnabled = false; ShowToast("Đã bỏ chặn; cần xác minh lại máy này."); }; panel.Children.Add(unblock);
        }
        if (ShowDialog("Hồ sơ và cài đặt", panel, "Lưu thay đổi", () => !string.IsNullOrWhiteSpace(name.Text), width: 720))
        {
            try
            {
                App.Settings.Name = PeerNode.ValidateDisplayName(name.Text); App.Settings.MessageToasts = toasts.IsChecked == true; App.Settings.MessageToastMode = toastMode.SelectedIndex == 1 ? "private" : "full"; App.Settings.CloseToTray = closeToTray.IsChecked == true; App.SaveSettings();
                WindowsStartup.Apply(startup.IsChecked == true); App.Settings.StartWithWindows = startup.IsChecked == true; App.SaveSettings();
                await node.UpdateDisplayNameAsync(App.Settings.Name);
                if (avatar != store.Avatar) await node.UpdateAvatarAsync(avatar);
                App.ApplyTheme(App.Settings.Theme); MyInitials.Text = Initials(identity.Name); Title = "Lantern · " + identity.Name; renderKey = ""; Refresh(); ShowToast("Đã lưu hồ sơ và cập nhật tên hiển thị.");
            }
            catch (Exception ex) { ShowError(ex); }
        }
    }
    private void RenamePeerClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking) return;
        var peer = node.FindPeer(selectedId); if (peer is null) return;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Tên riêng của đối tác", 24, true));
        panel.Children.Add(Paragraph("Chỉ bạn thấy tên này. Tên gốc của đối tác: " + peer.Name, 13, muted: true));
        panel.Children.Add(Paragraph("Tên hiển thị trên máy bạn", bold: true));
        var alias = new TextBox { Text = peer.LocalAlias, MaxLength = 80 }; panel.Children.Add(alias);
        panel.Children.Add(Paragraph("Để trống để dùng lại tên do đối tác đặt. Tên riêng sẽ được giữ cả khi đối tác đổi tên hoặc đổi IP.", 12, muted: true));
        if (!ShowDialog("Đặt tên riêng", panel, "Lưu tên")) return;
        try { node.SetPeerAlias(peer.Id, alias.Text); renderKey = ""; Refresh(); ShowToast("Đã lưu tên riêng trên máy này."); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void ShareContactClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking) return;
        var contact = node.FindPeer(selectedId); if (contact is null) return;
        var choices = node.Peers.Where(p => p.Trusted && !p.Blocked && p.Id != contact.Id).Select(p => new ContactRow(p.Id, p.DisplayName, p.Initials, "Đã xác minh", false)).ToList();
        if (choices.Count == 0) { ShowToast("Cần ghép đôi với người nhận trước khi chia sẻ liên hệ."); return; }
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Chia sẻ liên hệ", 24, true)); panel.Children.Add(Paragraph("Gửi tên công khai, IP và danh tính của " + contact.Name + ". Người nhận vẫn phải xác minh ghép đôi. IP có thể đổi khi đối tác chuyển mạng.", 13, muted: true));
        var target = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = 0, Padding = new Thickness(12) }; panel.Children.Add(target);
        if (!ShowDialog("Chia sẻ liên hệ", panel, "Gửi liên hệ")) return;
        try { await node.ShareContactAsync(((ContactRow)target.SelectedItem).Id, contact.Id); ShowToast("Đã gửi liên hệ."); } catch (Exception ex) { ShowError(ex); }
    }
    private static string Duration(int seconds) => seconds < 60 ? seconds + " giây" : seconds < 3600 ? $"{seconds / 60}:{seconds % 60:00}" : seconds < 86400 ? seconds / 3600 + " giờ" : seconds / 86400 + " ngày";
    private async void ExpirySettingsClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking) return;
        if (selectedId.Length == 0 || !TryUnlockConversation()) return;
        var id = selectedId;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Tin nhắn tự hủy", 24, true));
        panel.Children.Add(Paragraph("Áp dụng cho tin mới ở cả hai chiều. Bộ đếm bắt đầu khi người nhận đọc; trong nhóm là lượt đọc đầu tiên. Hết giờ, tin nhắn và tệp do Lantern lưu được xóa trên các máy. Bản tệp đã lưu ra ngoài không bị xóa.", 13, muted: true));
        var options = new[] { 0, 10, 30, 60, 300, 3600, 86400, 604800 };
        var current = store.Policies().Find(p => p.Id == id)?.DisappearSeconds ?? 0;
        var choice = new ComboBox { ItemsSource = options.Select(seconds => seconds == 0 ? "Tắt" : Duration(seconds)), SelectedIndex = Math.Max(0, Array.IndexOf(options, current)), Padding = new Thickness(12) }; panel.Children.Add(choice);
        if (!ShowDialog("Cài đặt tự hủy", panel, "Lưu thay đổi")) return;
        try { await node.SetDisappearTimerAsync(id, options[choice.SelectedIndex]); ShowToast("Đã cập nhật chế độ tự hủy cho tin mới."); } catch (Exception ex) { ShowError(ex); }
    }
    private async Task UpdatePresence()
    {
        if (closing) return;
        var presence = PresenceMonitor.Current(App.Settings.DoNotDisturb);
        await node.SetPresenceAsync(presence);
        var row = new ContactRow("", "", "", "", false, Presence: presence);
        SelfPresenceDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(row.PresenceColor)); SelfPresenceButton.ToolTip = row.PresenceLabel + " · Bấm để đổi trạng thái";
    }
    private void PresenceClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var (label, dnd) in new[] { ("Đang hoạt động · tự chuyển vắng mặt", false), ("Không làm phiền", true) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = App.Settings.DoNotDisturb == dnd };
            item.Click += async (_, _) => { App.Settings.DoNotDisturb = dnd; App.SaveSettings(); await UpdatePresence(); }; menu.Items.Add(item);
        }
        menu.PlacementTarget = SelfPresenceButton; menu.IsOpen = true;
    }
    private void FindPeopleClick(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        var window = new Window { Owner = this, Title = "Tìm người theo tên", Width = 560, Height = 570, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        panel.Children.Add(Paragraph("Tìm người trong mạng", 24, true));
        panel.Children.Add(Paragraph("Tìm theo tên công khai trong danh bạ của các máy đang kết nối. Tên riêng chỉ có trên máy bạn. Nếu chưa có máy nào biết người cần tìm, hãy dùng IP hoặc nhờ chia sẻ liên hệ.", 12, muted: true));
        var name = new TextBox { Text = ContactSearch.Text, MaxLength = 80, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(name);
        var search = new Button { Content = "Tìm qua các máy đã ghép đôi", Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(search);
        var results = new StackPanel(); panel.Children.Add(new ScrollViewer { Content = results, Height = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(uiLifetime.Token); window.Closed += (_, _) => cancel.Cancel();
        search.Click += async (_, _) =>
        {
            search.IsEnabled = false; results.Children.Clear(); results.Children.Add(Paragraph("Đang hỏi các máy trong mạng…", 12, muted: true));
            try
            {
                var contacts = await node.FindContactsAsync(name.Text, cancel.Token); results.Children.Clear();
                if (contacts.Count == 0) results.Children.Add(Paragraph("Chưa tìm thấy. Thử tên khác, IP hoặc liên hệ được chia sẻ.", 13, muted: true));
                foreach (var card in contacts)
                {
                    var button = new Button { Content = new TextBlock { Text = $"{card.Name}\nDanh tính · {card.Id[..12]}…", TextWrapping = TextWrapping.Wrap }, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 9) };
                    button.Click += async (_, _) => { button.IsEnabled = false; try { await node.ConnectAsync(card.Address, card.Port, card.Id); window.Close(); ContactSearch.Clear(); Refresh(); ContactsList.SelectedItem = ContactsList.Items.OfType<ContactRow>().FirstOrDefault(row => row.Id == card.Id); ShowToast("Đối chiếu mã để xác minh người dùng vừa tìm thấy."); } catch (Exception ex) { button.IsEnabled = true; ShowError(ex); } }; results.Children.Add(button);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { results.Children.Clear(); results.Children.Add(Paragraph(ex.Message, 13, muted: true)); }
            finally { search.IsEnabled = true; }
        };
        name.KeyDown += (_, key) => { if (key.Key == Key.Enter && search.IsEnabled) { key.Handled = true; search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } };
        window.ShowDialog();
    }
    private void UpdatesClick(object sender, RoutedEventArgs e)
    {
        if (appLocking) return;
        var panel = new StackPanel { Margin = new Thickness(25) };
        var window = new Window { Owner = this, Title = "Cập nhật Lantern", Width = 500, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        panel.Children.Add(Paragraph("Lantern " + UpdateController.CurrentVersion.ToString(3), 24, true));
        var status = Paragraph(updates.Status, 13, muted: true); panel.Children.Add(status);
        var auto = new CheckBox { Content = "Tự kiểm tra và tải bản mới (mỗi 6 giờ)", IsChecked = App.Settings.AutoUpdates, Margin = new Thickness(0, 0, 0, 18) }; auto.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        auto.Click += (_, _) => { App.Settings.AutoUpdates = auto.IsChecked == true; App.SaveSettings(); }; panel.Children.Add(auto);
        panel.Children.Add(Paragraph("Gói tải từ GitHub zeusato/Lantern được kiểm tra SHA-256. Cài đặt sẽ đóng và mở lại app; workspace và lịch sử được giữ nguyên.", 12, muted: true));
        var check = new Button { Content = "Kiểm tra và tải cập nhật", Margin = new Thickness(0, 0, 0, 10) }; panel.Children.Add(check);
        var install = new Button { Content = "Cài đặt và khởi động lại", IsEnabled = updates.DownloadedArchive is not null }; panel.Children.Add(install);
        void Changed() { status.Text = updates.Status; install.IsEnabled = updates.DownloadedArchive is not null; }
        updates.Changed += Changed; window.Closed += (_, _) => updates.Changed -= Changed;
        check.Click += async (_, _) => { check.IsEnabled = false; await updates.CheckAsync(uiLifetime.Token); check.IsEnabled = true; };
        install.Click += async (_, _) => { install.IsEnabled = false; try { await updates.StartInstallAsync(uiLifetime.Token); window.Close(); ExitApplication(); } catch (Exception ex) { install.IsEnabled = true; MessageBox.Show(window, ex.Message, "Cập nhật Lantern"); } };
        window.ShowDialog();
    }
    private void ExportIdentity()
    {
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Bảo vệ bản sao lưu", 22, true)); panel.Children.Add(Paragraph("Đặt mật khẩu ít nhất 12 ký tự. Ai có tệp và mật khẩu này có thể dùng danh tính của bạn.", muted: true)); var password = new PasswordBox { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(password);
        if (!ShowDialog("Sao lưu danh tính", panel, "Chọn nơi lưu", () => password.Password.Length >= 12)) return;
        var dialog = new SaveFileDialog { Filter = "Danh tính Lantern|*.lanternkey", FileName = "Lantern-identity.lanternkey" };
        if (dialog.ShowDialog(this) == true) { try { identity.Export(dialog.FileName, password.Password); ShowToast("Đã lưu bản sao danh tính có mật khẩu."); } catch (Exception ex) { ShowError(ex); } }
    }
    private void ImportIdentity()
    {
        var file = new OpenFileDialog { Filter = "Danh tính Lantern|*.lanternkey" }; if (file.ShowDialog(this) != true) return;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Khôi phục danh tính", 22, true)); panel.Children.Add(Paragraph("Thao tác thay danh tính đang dùng. App sẽ đóng để nạp lại khóa khi mở lần sau. Hãy sao lưu danh tính hiện tại trước.", muted: true)); var password = new PasswordBox { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(password);
        if (!ShowDialog("Khôi phục", panel, "Khôi phục và đóng app")) return;
        try { Identity.Import(file.FileName, password.Password, App.DataDirectory, App.Vault); ExitApplication(); } catch (Exception ex) { ShowError(ex); }
    }
    private bool ShowDialog(string title, FrameworkElement content, string accept, Func<bool>? validate = null, UIElement? initialFocus = null, double width = 510)
    {
        return InAppDialog.Show(this, title, content, accept, validate, initialFocus, width);
    }
    private void BlockClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking) return;
        var peer = node.FindPeer(selectedId); if (peer is null) return;
        if (MessageBox.Show(this, $"Chặn kết nối từ {peer.DisplayName}? Lịch sử vẫn giữ trên máy.", "Chặn máy", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { node.Block(selectedId); selectedId = ""; renderKey = ""; Refresh(); }
    }
    private void DetailsClick(object sender, RoutedEventArgs e) { if (ActualWidth < 1150 || GameHost.Visibility == Visibility.Visible) CompactDetails(); else { detailsVisible = !detailsVisible; UpdateLayoutWidths(); } }
    private void CompactDetails()
    {
        var content = DetailsPanel.Child; if (content is null) return;
        DetailsPanel.Child = null;
        var window = new Window { Owner = this, Title = "Thông tin hội thoại", Width = 420, Height = Math.Min(760, SystemParameters.WorkArea.Height - 80), WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        ProtectOwnedWindow(window, selectedId);
        try { window.ShowDialog(); } finally { window.Content = null; DetailsPanel.Child = content; }
    }
    private void WindowSizeChanged(object sender, SizeChangedEventArgs e) { if (IsInitialized) { UpdateLayoutWidths(); UpdateComposerHeight(); } }
    private void UpdateLayoutWidths()
    {
        DetailsColumn.Width = new GridLength(detailsVisible && ActualWidth >= 1150 ? 300 : 0); DetailsPanel.Visibility = DetailsColumn.Width.Value > 0 ? Visibility.Visible : Visibility.Collapsed;
        ContactsColumn.Width = new GridLength(ActualWidth < 950 ? 240 : 290);
    }
    private void CloseMediaViews() { foreach (var window in mediaWindows.ToArray()) window.Close(); }
    private void LockConversationClick(object sender, RoutedEventArgs e)
    {
        mobile.LockConversation(selectedId);
        if (emojiPopup is not null) emojiPopup.IsOpen = false;
        unlockedConversations.Remove(selectedId); Composer.Clear(); drafts.Remove(selectedId); replyDrafts.Remove(selectedId); CloseMediaViews(); renderKey = ""; RefreshConversation();
    }
    private bool TryUnlockConversation()
    {
        if (!ConversationProtected) return true;
        var id = selectedId;
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Mở khóa hội thoại", 23, true)); panel.Children.Add(Paragraph("Mật khẩu này chỉ mở cuộc trò chuyện trên máy đang dùng.", 12, muted: true));
        var password = new PasswordBox { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(password);
        var forgot = new Button { Content = "Quên mật khẩu? Xóa lịch sử hội thoại", Margin = new Thickness(0, 0, 0, 12) }; forgot.Click += async (_, _) => { if (await ResetConversation(id)) { forgot.Content = "Đã xóa lịch sử. Bấm Mở khóa để bắt đầu lại."; forgot.IsEnabled = false; } }; panel.Children.Add(forgot);
        if (!ShowDialog("Hội thoại được bảo vệ", panel, "Mở khóa", () => !App.Vault.IsConversationLocked(id) || App.Vault.VerifyConversation(id, password.Password), password)) return false;
        password.Clear(); unlockedConversations.Add(id); RestoreComposerDraft(id); renderKey = ""; RefreshConversation(); return true;
    }
    private void ConversationPasswordClick(object sender, RoutedEventArgs e)
    {
        if (ConversationProtected || appLocking) return;
        if (selectedId.Length == 0 || !TryUnlockConversation()) return;
        var id = selectedId; var panel = new StackPanel(); panel.Children.Add(Paragraph("Mật khẩu hội thoại", 23, true)); panel.Children.Add(Paragraph("Mở khóa một lần để dùng trong cả phiên. Bấm biểu tượng khóa ở đầu hội thoại để khóa lại; khóa hoặc thoát ứng dụng cũng khóa mọi hội thoại. Máy người bên kia không bị thay đổi.", 12, muted: true));
        var first = new PasswordBox { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12) }; var second = new PasswordBox { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(Paragraph("Mật khẩu mới · ít nhất 8 ký tự", 12, true)); panel.Children.Add(first); panel.Children.Add(Paragraph("Nhập lại mật khẩu", 12, true)); panel.Children.Add(second);
        bool removed = false;
        if (App.Vault.IsConversationLocked(id))
        {
            var remove = new Button { Content = "Bỏ mật khẩu của hội thoại này" }; remove.Click += (_, _) => { App.Vault.RemoveConversationPassword(id); removed = true; remove.IsEnabled = false; ShowToast("Đã bỏ mật khẩu hội thoại."); }; panel.Children.Add(remove);
        }
        if (ShowDialog("Bảo vệ hội thoại", panel, "Lưu mật khẩu", () => removed || first.Password.Length >= 8 && first.Password == second.Password) && !removed)
        { App.Vault.SetConversationPassword(id, first.Password); mobile.LockConversation(id); unlockedConversations.Add(id); ShowToast("Đã đặt mật khẩu riêng cho hội thoại."); }
        first.Clear(); second.Clear(); renderKey = ""; RefreshConversation();
    }
    private async Task<bool> ResetConversation(string id)
    {
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Xóa lịch sử để bắt đầu lại?", 23, true)); panel.Children.Add(Paragraph("Tin nhắn và tệp của hội thoại này trên máy sẽ bị xóa. Mật khẩu riêng được gỡ bỏ. Danh bạ và bản chat ở máy khác được giữ nguyên.", 13));
        var expected = "XOA " + id[..8].ToUpperInvariant(); panel.Children.Add(Paragraph("Nhập đúng: " + expected, 15, true)); var phrase = new TextBox(); panel.Children.Add(phrase);
        if (!ShowDialog("Xác nhận xóa hội thoại", panel, "Xác thực chủ máy", () => phrase.Text.Trim() == expected)) return false;
        if (!await WindowsOwnerVerifier.VerifyAsync(this, App.Vault.Header, "Lantern: xác nhận xóa lịch sử và tệp của hội thoại " + id[..8])) return false;
        if (closing) return false;
        try { CloseMediaViews(); foreach (var player in players) player.Close(); players.Clear(); await node.DeleteConversationHistoryAsync(id); App.Vault.RemoveConversationPassword(id); drafts.Remove(id); if (id == selectedId) Composer.Clear(); unlockedConversations.Remove(id); renderKey = ""; RefreshConversation(); ShowToast("Đã xóa lịch sử hội thoại trên máy này."); return true; }
        catch (Exception ex) { ShowError(ex); return false; }
    }
    private void ChangeAppPassword()
    {
        var panel = new StackPanel(); panel.Children.Add(Paragraph("Đổi mật khẩu ứng dụng", 23, true));
        var current = new PasswordBox { Padding = new Thickness(12) }; var next = new PasswordBox { Padding = new Thickness(12) }; var repeat = new PasswordBox { Padding = new Thickness(12) };
        panel.Children.Add(Paragraph("Mật khẩu hiện tại", 12, true)); panel.Children.Add(current); panel.Children.Add(Paragraph("Mật khẩu mới", 12, true)); panel.Children.Add(next); panel.Children.Add(Paragraph("Nhập lại mật khẩu mới", 12, true)); panel.Children.Add(repeat);
        if (!ShowDialog("Đổi mật khẩu", panel, "Đổi mật khẩu", () => next.Password.Length >= 8 && next.Password == repeat.Password)) return;
        try { App.Vault.ChangePassword(current.Password, next.Password); ShowToast("Đã đổi mật khẩu ứng dụng."); } catch (Exception ex) { ShowError(ex); }
        current.Clear(); next.Clear(); repeat.Clear();
    }
    private void LockAppClick(object sender, RoutedEventArgs e) => LockApp();
    private void LockApp()
    {
        if (appLocking || closing) return;
        InAppDialog.CancelAll(this);
        if (emojiPopup is not null) emojiPopup.IsOpen = false;
        appLocking = true; HideGamePanel(); CloseMediaViews(); foreach (var player in players) player.Close(); players.Clear();
        foreach (var window in OwnedWindows.Cast<Window>().ToArray()) window.Close();
        DetailsContent.IsEnabled = false; DetailsContent.Visibility = Visibility.Collapsed; InfoTabs.IsEnabled = false; DetailsLocked.Visibility = Visibility.Visible;
        unlockedConversations.Clear(); renderKey = ""; RefreshConversation();
        ShowLogin(initial: false);
    }
    private void ShowLogin(bool initial)
    {
        Shell.Visibility = Visibility.Collapsed; StatusBar.Visibility = Visibility.Collapsed; Title = "Lantern · Đã khóa";
        loginView = new LoginView(App.DataDirectory, allowReset: initial);
        loginView.Unlocked += async vault =>
        {
            if (closing) { vault.Dispose(); return; }
            if (initial) { App.SetUnlockedVault(vault); InitializeSession(); }
            else vault.Dispose();
            appLocking = false; LoginHost.Child = null; LoginHost.Visibility = Visibility.Collapsed; loginView = null;
            Shell.Visibility = Visibility.Visible; StatusBar.Visibility = Visibility.Visible; Title = "Lantern · " + identity.Name;
            if (initial) await StartSession();
            else { renderKey = ""; Refresh(); }
        };
        LoginHost.Child = loginView; LoginHost.Visibility = Visibility.Visible;
    }
    private void ShowToast(string text) { if (!closing) ToastText.Text = text; }
    private void ShowError(Exception ex)
    {
        if (ex is OperationCanceledException && closing) return;
        ShowToast(ex.Message); if (!closing) MessageBox.Show(this, ex.Message, "Lantern", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (exportingWorkspace || loginView?.IsRestoring == true) { e.Cancel = true; ShowToast("Đang sao lưu/khôi phục dữ liệu. Vui lòng chờ hoàn tất trước khi đóng ứng dụng."); return; }
        if (node is null) { closing = true; InAppDialog.CancelAll(this); updates.Dispose(); linkPreviews.Dispose(); uiLifetime.Dispose(); return; }
        if (closing) { e.Cancel = !closeReady; return; }
        if (!exitRequested && App.Settings.CloseToTray && tray is not null) { e.Cancel = true; HideToTray(); return; }
        e.Cancel = true; closing = true; InAppDialog.CancelAll(this); refreshTimer.Stop(); uiLifetime.Cancel(); Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
        sessionTimer.Stop(); updateTimer.Stop(); updates.Dispose(); linkPreviews.Dispose();
        tray?.Dispose();
        if (emojiPopup is not null) emojiPopup.IsOpen = false;
        foreach (var player in players) player.Close();
        await mobile.DisposeAsync(); await node.DisposeAsync(); store.Dispose(); identity.Dispose(); uiLifetime.Dispose(); closeReady = true; Close();
    }
}

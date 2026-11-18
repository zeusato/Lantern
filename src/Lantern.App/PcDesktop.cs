using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Automation;
using Lantern.Core;

namespace Lantern.App;

/// <summary>Local, opt-in Windows adapter. No screenshots, remote shell or arbitrary executable arguments.</summary>
internal sealed class PcDesktop : IPcDesktop
{
    private readonly SemaphoreSlim worker = new(1, 1);
    public Task<List<PcTarget>> InspectAsync(CancellationToken ct)
    {
        var inspection = new PcInspection();
        return inspection.RunAsync(token => Run(() =>
        {
            var targets = Discover();
            inspection.Discover(targets.Select(t => new PcTarget(t.Id, t.App, "", "unknown", "", "")));
            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    Restore(target, token);
                    var first = Read(target, token); Pause(token, 180); var second = Read(target, token, includeAnswer: true);
                    inspection.Report(first.Public.Version == second.Public.Version && first.Public.State == second.Public.State
                        ? second.Public : second.Public with { State = "unknown", Version = "", Detail = "Giao diện AI vừa thay đổi. Kiểm tra lại trạng thái." });
                }
                catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
                { inspection.Report(new(target.Id, target.App, "", "unknown", "", ex.Message)); }
            }
            return true;
        }, token), ct);
    }
    public Task<PcTarget> ReadAsync(PcPacket request, CancellationToken ct) => Run(() =>
    {
        var target = Discover().SingleOrDefault(t => t.Id == request.Target) ?? throw new InvalidOperationException("Cửa sổ AI đã đóng.");
        EnsureDesktop();
        var snapshot = Read(target, ct, includeAnswer: true);
        if (snapshot.Public.Version != request.Version || Read(target, ct).Public.Version != request.Version) throw new InvalidOperationException("Hội thoại đã thay đổi. Kiểm tra AI lại trước khi xem câu trả lời.");
        return snapshot.Public;
    }, ct);
    public Task<string> ExecuteAsync(PcPacket request, Action authorize, CancellationToken ct) => Run(() =>
    {
        authorize();
        if (request.Command == "power.lock")
        {
            if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error());
            return "Windows đã nhận yêu cầu khóa máy.";
        }
        if (request.Command == "power.shutdown")
        {
            authorize();
            using var process = Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "shutdown.exe")) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "/s", "/t", "0" } }) ?? throw new InvalidOperationException("Không gửi được lệnh tắt máy.");
            if (!process.WaitForExit(2000)) throw new TimeoutException();
            if (process.ExitCode != 0) throw new InvalidOperationException("Windows từ chối tắt máy. Kiểm tra quyền tài khoản trên PC.");
            return "Windows đã nhận yêu cầu tắt máy. Ứng dụng có dữ liệu chưa lưu có thể chặn quá trình này.";
        }
        if (request.Command != "ai.send") throw new ArgumentException("Lệnh không được hỗ trợ.");
        var target = Discover().SingleOrDefault(t => t.Id == request.Target) ?? throw new InvalidOperationException("Cửa sổ AI đã đóng hoặc thay đổi. Kiểm tra lại.");
        return Send(target, request, authorize, ct);
    }, ct);

    private static string Send(Target target, PcPacket request, Action authorize, CancellationToken ct)
    {
        Restore(target, ct);
        var first = Read(target, ct); Pause(ct, 200); var current = Read(target, ct);
        if (first.Public.Version != current.Public.Version || current.Public.Version != request.Version || first.Public.State != "idle" || current.Public.State != "idle")
            throw new InvalidOperationException("AI hoặc cuộc trò chuyện đã thay đổi, đang bận, có bản nháp hoặc chưa xác định trạng thái. Kiểm tra lại.");
        authorize();
        if (!IsWindowVisible(target.Window) || IsIconic(target.Window)) throw new InvalidOperationException("Cửa sổ AI chưa sẵn sàng.");
        var editor = current.Editor!;
        if (!editor.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject)) throw new InvalidOperationException("Ứng dụng không hỗ trợ nhập prompt bằng UI Automation.");
        if (!string.IsNullOrWhiteSpace(EditorValue(editor, (ValuePattern)valueObject))) throw new InvalidOperationException("Ô nhập vừa có bản nháp. Kiểm tra lại trên PC.");
        authorize();
        ((ValuePattern)valueObject).SetValue(request.Prompt);
        Pause(ct, 250);
        var filled = Read(target, ct);
        if (filled.Public.Version != request.Version || filled.Busy || filled.Awaiting || filled.Value.TrimEnd('\r', '\n') != request.Prompt)
            throw new InvalidOperationException("Chưa gửi: giao diện thay đổi trong lúc điền. Prompt có thể đang nằm trong ô nhập trên PC.");
        authorize();
        var send = filled.Send ?? throw new InvalidOperationException("Chưa gửi: không tìm thấy nút Gửi. Prompt vẫn nằm trong ô nhập.");
        if (!send.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject)) throw new InvalidOperationException("Ứng dụng không hỗ trợ kích hoạt nút Gửi.");
        if (!IsWindowVisible(target.Window) || IsIconic(target.Window) || !IsSend(target, send.Current.Name) || !send.Current.IsEnabled || ((ValuePattern)valueObject).Current.Value.TrimEnd('\r', '\n') != request.Prompt)
            throw new InvalidOperationException("Chưa gửi: cửa sổ hoặc ô nhập đã đổi.");
        // Once invocation starts, an unconfirmed outcome must never be reported as safe to retry.
        try
        {
            authorize();
            ((InvokePattern)invokeObject).Invoke();
            for (int i = 0; i < 20; i++)
            {
                Pause(ct, 150);
                var after = Read(target, ct, request.Prompt);
                // The first send can give a new conversation its title/URL. The document must
                // still be the one whose composer we checked before invoking Send.
                if (after.Public.Version != request.Version && !(current.NewConversation && after.DocumentId == current.DocumentId && current.DocumentId.Length > 0)) throw new TimeoutException();
                if (string.IsNullOrWhiteSpace(after.Value) && after.Nodes.Any(e => e.Cached.ControlType == ControlType.Text && e.Cached.Name == request.Prompt))
                    return "Đã gửi prompt vào " + current.Public.Conversation + ". AI tự xử lý trong phiên hiện tại.";
            }
            throw new TimeoutException();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new TimeoutException("Chưa xác nhận được việc gửi. Kiểm tra chat trên PC trước khi gửi lại.", ex); }
    }

    // A hung third-party UIA provider cannot create an unbounded queue or execute late commands.
    // Keep the one worker occupied until its actual thread exits; all mutation checkpoints recheck cancellation.
    private async Task<T> Run<T>(Func<T> action, CancellationToken ct)
    {
        if (!await worker.WaitAsync(0, ct)) throw new InvalidOperationException("PC đang kiểm tra một ứng dụng. Thử lại sau.");
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { ct.ThrowIfCancellationRequested(); result.TrySetResult(action()); } catch (Exception ex) { result.TrySetException(ex); } finally { worker.Release(); } }) { IsBackground = true, Name = "Lantern PC commands" };
        thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        return await result.Task.WaitAsync(ct);
    }
    private static void Pause(CancellationToken ct, int milliseconds) { if (ct.WaitHandle.WaitOne(milliseconds)) ct.ThrowIfCancellationRequested(); }
    private sealed record Target(string Id, string App, int ProcessId, IntPtr Window);
    private static bool IsSend(Target target, string name) => name == "Send" || target.App == "Claude" && name == "Send message";
    private sealed record Snapshot(PcTarget Public, AutomationElement? Editor, AutomationElement? Send, string Value, bool Busy, bool Awaiting, List<AutomationElement> Nodes, string DocumentId, bool NewConversation);
    private static List<Target> Discover()
    {
        var processes = new Dictionary<int, (string App, long Started)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (!name.Equals("claude", StringComparison.OrdinalIgnoreCase) && !name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) && !name.Equals("Codex", StringComparison.OrdinalIgnoreCase)) continue;
                    // App identity is selected locally; the phone never supplies an exe path.
                    processes[process.Id] = (name.Equals("claude", StringComparison.OrdinalIgnoreCase) ? "Claude" : "ChatGPT / Codex", process.StartTime.ToUniversalTime().Ticks);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            }
        }
        var targets = new List<Target>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (!processes.TryGetValue((int)pid, out var info) || GetWindow(window, 4) != IntPtr.Zero) return true;
            if (!GetWindowRect(window, out var rectangle) || (!IsIconic(window) && (rectangle.Right - rectangle.Left < 300 || rectangle.Bottom - rectangle.Top < 200)) || (GetWindowLongPtr(window, -20).ToInt64() & 0x80) != 0) return true;
            var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
            if (title.Length == 0) return true;
            targets.Add(new($"{pid}:{info.Started}:{window.ToInt64()}", info.App, (int)pid, window)); return true;
        }, IntPtr.Zero);
        // Background/tray processes may have no usable window. Report this, never pretend they're offline or idle.
        foreach (var app in processes.Values.Select(p => p.App).Distinct())
            if (!targets.Any(t => t.App == app)) targets.Add(new("hidden:" + app, app, 0, IntPtr.Zero));
        return targets.Take(8).ToList();
    }
    private static void Restore(Target target, CancellationToken ct)
    {
        if (target.Window == IntPtr.Zero) throw new InvalidOperationException("App đang chạy ở nền nhưng chưa có cửa sổ. Mở app từ tray trên PC rồi kiểm tra lại.");
        EnsureDesktop();
        ct.ThrowIfCancellationRequested();
        ShowWindowAsync(target.Window, 9); SetForegroundWindow(target.Window);
        for (int i = 0; i < 10; i++)
        { if (IsWindowVisible(target.Window) && !IsIconic(target.Window)) { Pause(ct, 200); return; } Pause(ct, 100); }
        throw new InvalidOperationException("Windows chưa hiện được cửa sổ. Mở ứng dụng từ tray rồi kiểm tra lại.");
    }
    private static void EnsureDesktop()
    {
        using var input = new DesktopHandle(OpenInputDesktop(0, false, 1));
        if (input.IsInvalid) throw new InvalidOperationException("Windows đang khóa hoặc không có desktop tương tác.");
        var name = new StringBuilder(256);
        if (!GetUserObjectInformation(input.DangerousGetHandle(), 2, name, name.Capacity * 2, out _) || name.ToString() != "Default") throw new InvalidOperationException("Cần mở khóa Windows để gửi prompt.");
    }
    private static Snapshot Read(Target target, CancellationToken ct, string? sentPrompt = null, bool includeAnswer = false)
    {
        ct.ThrowIfCancellationRequested();
        // Fetch relevant controls and their properties in batches. Per-node TreeWalker/Current calls
        // cross the process boundary thousands of times on a long conversation.
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        foreach (var property in new[] { AutomationElement.NameProperty, AutomationElement.ControlTypeProperty,
            AutomationElement.IsOffscreenProperty, AutomationElement.BoundingRectangleProperty, AutomationElement.IsEnabledProperty }) cache.Add(property);
        using var cached = cache.Activate();
        var root = AutomationElement.FromHandle(target.Window) ?? throw new InvalidOperationException("Không đọc được cửa sổ AI.");
        var kinds = new List<Condition> { new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar) };
        if (sentPrompt is not null) kinds.Add(new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text), new PropertyCondition(AutomationElement.NameProperty, sentPrompt)));
        var editorNodes = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)).Cast<AutomationElement>().ToList();
        ct.ThrowIfCancellationRequested();
        bool Visible(AutomationElement e) => !e.Cached.IsOffscreen && !e.Cached.BoundingRectangle.IsEmpty;
        var editors = editorNodes.Where(e => Visible(e) && e.Cached.IsEnabled &&
            (target.App == "Claude" ? e.Cached.Name == "Prompt" : e.Cached.Name is "Work with Codex" or "Ask anything" or "Do anything" or "Message ChatGPT" or "Ask ChatGPT")).ToArray();
        if (editors.Length != 1) throw new InvalidOperationException("Chưa nhận diện chắc chắn ô nhập của cuộc trò chuyện.");
        var editor = editors[0]; var bounds = editor.Cached.BoundingRectangle;
        // Electron exposes an outer shell document as well as the active chat. Only the
        // document containing this composer may supply its title, buttons and session URL.
        AutomationElement? document = null;
        for (var parent = TreeWalker.ControlViewWalker.GetParent(editor, cache); parent is not null; parent = TreeWalker.ControlViewWalker.GetParent(parent, cache))
        {
            ct.ThrowIfCancellationRequested();
            if (parent.Cached.ControlType == ControlType.Document) { document = parent; break; }
            if (parent == root) break;
        }
        var nodes = (document ?? root).FindAll(TreeScope.Descendants, new OrCondition(kinds.ToArray())).Cast<AutomationElement>().ToList();
        ct.ThrowIfCancellationRequested();
        var buttons = nodes.Where(e => e.Cached.ControlType == ControlType.Button && Visible(e)).ToArray();
        bool Near(AutomationElement e) { var b = e.Cached.BoundingRectangle; return b.Top >= bounds.Top - 80 && b.Bottom <= bounds.Bottom + 140 && b.Right >= bounds.Left && b.Left <= bounds.Right + 100; }
        var controls = buttons.Where(Near).ToArray();
        bool busy = controls.Any(e => e.Cached.IsEnabled && e.Cached.Name is "Stop" or "Stop generating" or "Steer" or "Interrupt");
        bool awaiting = buttons.Any(e => e.Cached.IsEnabled && e.Cached.Name is "Allow once" or "Allow this conversation" or "Always allow" or "Deny" or "Approve" or "Reject");
        if (!editor.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject) || ((ValuePattern)valueObject).Current.IsReadOnly) throw new InvalidOperationException("Ô nhập không hỗ trợ đọc/đặt giá trị.");
        var value = EditorValue(editor, (ValuePattern)valueObject);
        var title = document?.Cached.Name ?? "";
        var route = document?.TryGetCurrentPattern(ValuePattern.Pattern, out var documentValue) == true ? ((ValuePattern)documentValue).Current.Value : "";
        var documentId = document is null ? "" : string.Join('.', document.GetRuntimeId());
        AutomationElement? heading = null;
        if (target.App == "Claude") heading = buttons.SingleOrDefault(e => e.Cached.Name.EndsWith(", rename session", StringComparison.Ordinal));
        else
        {
            // Scope to the actual chat toolbar, never to a screen coordinate heuristic:
            // sidebars, scrolled message Copy buttons and multi-monitor DPI can share the same row.
            var toolbar = nodes.SingleOrDefault(e => e.Cached.ControlType == ControlType.ToolBar && e.Cached.Name == "Chat toolbar" && Visible(e));
            if (toolbar is not null)
            {
                var titles = toolbar.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().Where(Visible).ToArray();
                heading = titles.FirstOrDefault(e => title.Length > 0 && title != "ChatGPT" && e.Cached.Name == title) ?? titles.FirstOrDefault(e => e.Cached.Name.Length > 0 && e.Cached.Name is not ("Share" or "Show tabs" or "Toggle summary" or "Toggle pinned summary"));
                if (heading is not null && !PcAiState.IsUntitled(title) && heading.Cached.Name != title) throw new InvalidOperationException("Tiêu đề cửa sổ và cuộc trò chuyện chưa đồng bộ. Kiểm tra lại.");
            }
            else if (document is null) throw new InvalidOperationException("Chưa nhận diện được cuộc trò chuyện đang mở.");
        }
        if (heading is not null) title = heading.Cached.Name.Replace(", rename session", "", StringComparison.Ordinal);
        if (title.Length > 250) throw new InvalidOperationException("Chưa đọc được tên cuộc trò chuyện.");
        bool newConversation = PcAiState.IsUntitled(title);
        // Pin to the actual document route when available, so named/unnamed tabs cannot
        // collide and an auto-generated title does not invalidate the same conversation.
        var identity = target.Id + "|" + documentId + "|" + (route.Length > 0 && target.App == "Claude" ? route : title + "|" + (heading is null ? "" : string.Join('.', heading.GetRuntimeId()))) + "|" + string.Join('.', editor.GetRuntimeId());
        string version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        ct.ThrowIfCancellationRequested();
        bool recognized = busy || controls.Any(e => IsSend(target, e.Cached.Name) || PcAiState.IsEmptyComposerControl(target.App, e.Cached.Name));
        string answer = includeAnswer ? ReadAnswer(document, cache, ct) : "";
        string state = PcAiState.Classify(busy, awaiting, !string.IsNullOrWhiteSpace(value), recognized);
        return new(new(target.Id, target.App, newConversation ? "Hội thoại mới" : title, state, version, state switch { "busy" => "AI đang xử lý.", "attention" => "AI đang chờ phản hồi/xác nhận trên PC.", "draft" => "Ô nhập có bản nháp; Lantern sẽ không ghi đè.", "idle" => "Sẵn sàng nhận yêu cầu.", _ => "Chưa nhận diện được nút gửi/dừng của AI." }, answer), editor,
            controls.SingleOrDefault(e => IsSend(target, e.Cached.Name) && e.Cached.IsEnabled), value, busy, awaiting, nodes, documentId, newConversation);
    }
    private static string EditorValue(AutomationElement editor, ValuePattern value)
    {
        // Chromium may expose the placeholder through ValuePattern on an empty contenteditable.
        // TextPattern distinguishes it from a real draft containing that same text.
        if (editor.TryGetCurrentPattern(TextPattern.Pattern, out var text))
            return ((TextPattern)text).DocumentRange.GetText(-1).TrimEnd('\r', '\n');
        return value.Current.Value;
    }
    private static string ReadAnswer(AutomationElement? document, CacheRequest cache, CancellationToken ct)
    {
        if (document is null) return "";
        var containers = document.FindAll(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group),
            new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "Conversation"),
                new PropertyCondition(AutomationElement.NameProperty, "Chat messages")))).Cast<AutomationElement>().ToArray();
        if (containers.Length != 1) return "";
        var texts = containers[0].FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>().ToArray();
        var marker = texts.LastOrDefault(e => e.Cached.Name == "ChatGPT said:" || e.Cached.Name.StartsWith("Claude responded:", StringComparison.Ordinal));
        if (marker is null) return "";
        var block = TreeWalker.ControlViewWalker.GetParent(marker, cache);
        if (block is null || block == containers[0]) return "";
        ct.ThrowIfCancellationRequested();
        var parts = block.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>()
            .Where(e => !e.Equals(marker)).Select(e => e.Cached.Name).Where(t => !string.IsNullOrWhiteSpace(t));
        return string.Join(" ", parts).Trim() is var answer && answer.Length > 16000 ? answer[..16000] + "…" : answer;
    }
    private sealed class DesktopHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    { public DesktopHandle(IntPtr value) : base(true) => SetHandle(value); protected override bool ReleaseHandle() => CloseDesktop(handle); }
    private delegate bool EnumCallback(IntPtr window, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rectangle rectangle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool LockWorkStation();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder info, int length, out int needed);
}

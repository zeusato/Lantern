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
    public Task<List<PcTarget>> InspectAsync(CancellationToken ct) => Run(() =>
    {
        var targets = Discover(); var result = new List<PcTarget>();
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                Restore(target, ct);
                var first = Read(target); Pause(ct, 180); var second = Read(target);
                result.Add(second.Public with { State = first.Public.Version == second.Public.Version && first.Public.State == second.Public.State ? second.Public.State : "unknown" });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
            { result.Add(new(target.Id, target.App, "", "unknown", "", ex.Message)); }
        }
        return result;
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
        Restore(target, ct);
        var first = Read(target); Pause(ct, 200); var current = Read(target);
        if (first.Public.Version != current.Public.Version || current.Public.Version != request.Version || first.Public.State != "idle" || current.Public.State != "idle")
            throw new InvalidOperationException("AI hoặc cuộc trò chuyện đã thay đổi, đang bận, có bản nháp hoặc chưa xác định trạng thái. Kiểm tra lại.");
        authorize();
        if (!IsWindowVisible(target.Window) || IsIconic(target.Window)) throw new InvalidOperationException("Cửa sổ AI chưa sẵn sàng.");
        var editor = current.Editor!;
        if (!editor.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject)) throw new InvalidOperationException("Ứng dụng không hỗ trợ nhập prompt bằng UI Automation.");
        if (!string.IsNullOrWhiteSpace(((ValuePattern)valueObject).Current.Value)) throw new InvalidOperationException("Ô nhập vừa có bản nháp. Kiểm tra lại trên PC.");
        authorize();
        ((ValuePattern)valueObject).SetValue(request.Prompt);
        Pause(ct, 250);
        var filled = Read(target);
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
                var after = Read(target);
                if (after.Public.Version != request.Version) throw new TimeoutException();
                if (string.IsNullOrWhiteSpace(after.Value) && after.Nodes.Any(e => e.Current.ControlType == ControlType.Text && e.Current.Name == request.Prompt))
                    return "Đã gửi prompt vào " + current.Public.Conversation + ". AI tự xử lý trong phiên hiện tại.";
            }
            throw new TimeoutException();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new TimeoutException("Chưa xác nhận được việc gửi. Kiểm tra chat trên PC trước khi gửi lại.", ex); }
    }, ct);

    // A hung third-party UIA provider cannot create an unbounded queue or execute late commands.
    // Keep the one worker occupied until its actual thread exits; all mutation checkpoints recheck cancellation.
    private async Task<T> Run<T>(Func<T> action, CancellationToken ct)
    {
        if (!await worker.WaitAsync(0, ct)) throw new InvalidOperationException("PC đang kiểm tra một ứng dụng. Thử lại sau.");
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { ct.ThrowIfCancellationRequested(); result.TrySetResult(action()); } catch (Exception ex) { result.TrySetException(ex); } finally { worker.Release(); } }) { IsBackground = true, Name = "Lantern PC commands" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return await result.Task.WaitAsync(ct);
    }
    private static void Pause(CancellationToken ct, int milliseconds) { if (ct.WaitHandle.WaitOne(milliseconds)) ct.ThrowIfCancellationRequested(); }
    private sealed record Target(string Id, string App, int ProcessId, IntPtr Window);
    private static bool IsSend(Target target, string name) => name == "Send" || target.App == "Claude" && name == "Send message";
    private sealed record Snapshot(PcTarget Public, AutomationElement? Editor, AutomationElement? Send, string Value, bool Busy, bool Awaiting, List<AutomationElement> Nodes);
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
        using var input = new DesktopHandle(OpenInputDesktop(0, false, 1));
        if (input.IsInvalid) throw new InvalidOperationException("Windows đang khóa hoặc không có desktop tương tác.");
        var name = new StringBuilder(256);
        if (!GetUserObjectInformation(input.DangerousGetHandle(), 2, name, name.Capacity * 2, out _) || name.ToString() != "Default") throw new InvalidOperationException("Cần mở khóa Windows để gửi prompt.");
        ct.ThrowIfCancellationRequested();
        ShowWindowAsync(target.Window, 9); SetForegroundWindow(target.Window);
        for (int i = 0; i < 10; i++)
        { if (IsWindowVisible(target.Window) && !IsIconic(target.Window)) { Pause(ct, 200); return; } Pause(ct, 100); }
        throw new InvalidOperationException("Windows chưa hiện được cửa sổ. Mở ứng dụng từ tray rồi kiểm tra lại.");
    }
    private static Snapshot Read(Target target)
    {
        var root = AutomationElement.FromHandle(target.Window) ?? throw new InvalidOperationException("Không đọc được cửa sổ AI.");
        var nodes = new List<AutomationElement>(); var queue = new Queue<AutomationElement>(); queue.Enqueue(root);
        var walker = TreeWalker.ControlViewWalker;
        while (queue.Count > 0 && nodes.Count < 6000)
        {
            var node = queue.Dequeue(); nodes.Add(node);
            for (var child = walker.GetFirstChild(node); child is not null && queue.Count + nodes.Count < 6000; child = walker.GetNextSibling(child)) queue.Enqueue(child);
        }
        if (queue.Count > 0 || nodes.Count >= 6000) throw new InvalidOperationException("Giao diện quá lớn để xác định chắc chắn trạng thái. Thu gọn các bảng phụ rồi kiểm tra lại.");
        bool Visible(AutomationElement e) => !e.Current.IsOffscreen && !e.Current.BoundingRectangle.IsEmpty;
        var editors = nodes.Where(e => e.Current.ControlType == ControlType.Edit && Visible(e) && e.Current.IsEnabled &&
            (target.App == "Claude" ? e.Current.Name == "Prompt" : e.Current.Name is "Work with Codex" or "Ask anything" or "Do anything" or "Message ChatGPT" or "Ask ChatGPT")).ToArray();
        if (editors.Length != 1) throw new InvalidOperationException("Chưa nhận diện chắc chắn ô nhập của cuộc trò chuyện.");
        var editor = editors[0]; var bounds = editor.Current.BoundingRectangle;
        var buttons = nodes.Where(e => e.Current.ControlType == ControlType.Button && Visible(e)).ToArray();
        bool Near(AutomationElement e) { var b = e.Current.BoundingRectangle; return b.Top >= bounds.Top - 80 && b.Bottom <= bounds.Bottom + 140 && b.Right >= bounds.Left && b.Left <= bounds.Right + 100; }
        var controls = buttons.Where(Near).ToArray();
        bool busy = controls.Any(e => e.Current.Name is "Stop" or "Stop generating" or "Steer" or "Interrupt");
        bool awaiting = buttons.Any(e => e.Current.Name is "Allow once" or "Allow this conversation" or "Always allow" or "Deny" or "Approve" or "Reject");
        if (!editor.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject) || ((ValuePattern)valueObject).Current.IsReadOnly) throw new InvalidOperationException("Ô nhập không hỗ trợ đọc/đặt giá trị.");
        var value = ((ValuePattern)valueObject).Current.Value;
        var document = nodes.FirstOrDefault(e => e.Current.ControlType == ControlType.Document && Visible(e));
        var title = document?.Current.Name ?? "";
        AutomationElement? heading = null;
        if (target.App == "Claude") heading = buttons.SingleOrDefault(e => e.Current.Name.EndsWith(", rename session", StringComparison.Ordinal));
        else
        {
            // Scope to the actual chat toolbar, never to a screen coordinate heuristic:
            // sidebars, scrolled message Copy buttons and multi-monitor DPI can share the same row.
            var toolbar = nodes.SingleOrDefault(e => e.Current.ControlType == ControlType.ToolBar && e.Current.Name == "Chat toolbar" && Visible(e));
            if (toolbar is null) throw new InvalidOperationException("Chưa nhận diện được thanh tiêu đề cuộc trò chuyện ChatGPT.");
            var titles = toolbar.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().Where(Visible).ToArray();
            heading = titles.FirstOrDefault(e => e.Current.Name == title && title != "ChatGPT") ?? titles.FirstOrDefault(e => e.Current.Name.Length > 0 && e.Current.Name is not ("Share" or "Show tabs" or "Toggle summary" or "Toggle pinned summary"));
            if (heading is not null && title is not ("" or "ChatGPT") && heading.Current.Name != title) throw new InvalidOperationException("Tiêu đề cửa sổ và cuộc trò chuyện chưa đồng bộ. Kiểm tra lại.");
        }
        if (heading is not null) title = heading.Current.Name.Replace(", rename session", "", StringComparison.Ordinal);
        if (title.Length == 0 || title is "Claude" or "Claude Code" or "ChatGPT" or "New chat" || title.Length > 250) throw new InvalidOperationException("Mở sẵn một cuộc trò chuyện để gửi yêu cầu.");
        var identity = target.Id + "|" + title + "|" + string.Join('.', editor.GetRuntimeId()) + "|" + (heading is null ? "" : string.Join('.', heading.GetRuntimeId()));
        string version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        bool recognized = busy || controls.Any(e => IsSend(target, e.Current.Name) || target.App == "Claude" && e.Current.Name == "Press and hold to record");
        string state = PcAiState.Classify(busy, awaiting, !string.IsNullOrWhiteSpace(value), recognized);
        return new(new(target.Id, target.App, title, state, version, state switch { "busy" => "AI đang xử lý.", "attention" => "AI đang chờ phản hồi/xác nhận trên PC.", "draft" => "Ô nhập có bản nháp; Lantern sẽ không ghi đè.", "idle" => "Sẵn sàng nhận yêu cầu.", _ => "Chưa nhận diện được nút gửi/dừng của AI." }), editor,
            controls.SingleOrDefault(e => IsSend(target, e.Current.Name) && e.Current.IsEnabled), value, busy, awaiting, nodes);
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

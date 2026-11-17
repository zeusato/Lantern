using System.Security.Cryptography;
using System.Text.Json;

namespace Lantern.Core;

public sealed class PcAccess
{
    public bool Power { get; set; }
    public bool Ai { get; set; }
    public List<PcRoot> Roots { get; set; } = [];
    public void Validate()
    {
        if (Roots is null || Roots.Count > 16 || Roots.Select(r => r.Id).Distinct().Count() != Roots.Count)
            throw new ArgumentException("Tối đa 16 thư mục, không trùng mã.");
        foreach (var root in Roots)
        {
            if (!Guid.TryParseExact(root.Id, "N", out _) || string.IsNullOrWhiteSpace(root.Name) || root.Name.Length > 80)
                throw new ArgumentException("Tên thư mục không hợp lệ.");
            PcFiles.ValidateRoot(root.Path);
        }
    }
}
public sealed record PcRoot(string Id, string Name, string Path);
public sealed record PcEntry(string Name, bool Directory, long Size, DateTimeOffset Modified);
public sealed record PcTarget(string Id, string App, string Conversation, string State, string Version, string Detail);
public sealed class PcPacket
{
    public string Action { get; set; } = "";
    public string Command { get; set; } = "";
    public string Ticket { get; set; } = "";
    public string Root { get; set; } = "";
    public string Path { get; set; } = "";
    public string Target { get; set; } = "";
    public string Version { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string State { get; set; } = "";
    public string Message { get; set; } = "";
    public string Data { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public long Offset { get; set; }
    public long Expires { get; set; }
    public int Page { get; set; }
    public bool More { get; set; }
    public bool Power { get; set; }
    public bool Ai { get; set; }
    public List<PcRoot> Roots { get; set; } = [];
    public List<PcEntry> Entries { get; set; } = [];
    public List<PcTarget> Targets { get; set; } = [];
    public void Validate()
    {
        if (Action is null || Command is null || Ticket is null || Root is null || Path is null || Target is null || Version is null || Prompt is null || Data is null ||
            Action.Length > 40 || Command.Length > 40 || Ticket.Length > 64 || Root.Length > 32 || Path.Length > 4096 || Target.Length > 160 || Version.Length > 128 || Prompt.Length > 16000 || Data.Length > 400000 || Page is < 0 or > 10000)
            throw new InvalidDataException("Yêu cầu PC không hợp lệ.");
    }
}
public interface IPcDesktop
{
    Task<List<PcTarget>> InspectAsync(CancellationToken ct);
    Task<string> ExecuteAsync(PcPacket request, Action authorize, CancellationToken ct);
}

/// <summary>Separate from chat/outbox. A short-lived, server-issued ticket is consumed BEFORE any side effect.</summary>
public sealed class PcControl : IDisposable
{
    private readonly MobileRegistry registry;
    private readonly IPcDesktop desktop;
    private readonly Func<bool> available;
    private readonly TimeProvider clock;
    private readonly PcFiles files;
    private readonly object gate = new();
    private readonly Dictionary<string, Ticket> tickets = [];
    private sealed class Ticket(string device, PcPacket request, DateTimeOffset expires)
    {
        public string Device { get; } = device;
        public PcPacket Request { get; } = request;
        public DateTimeOffset Expires { get; } = expires;
        public TaskCompletionSource<PcPacket>? Completion { get; set; }
    }
    public PcControl(MobileRegistry registry, IPcDesktop desktop, Func<bool> available, TimeProvider? clock = null)
    { this.registry = registry; this.desktop = desktop; this.available = available; this.clock = clock ?? TimeProvider.System; files = new PcFiles(this.clock); }
    private PcAccess Access(string device)
    {
        if (!available()) throw new UnauthorizedAccessException("Lantern trên PC đang khóa hoặc đóng.");
        return registry.Snapshot().Devices.Find(d => d.Id == device)?.Access ?? throw new UnauthorizedAccessException("Thiết bị đã bị thu hồi.");
    }
    private void Authorize(string device, PcPacket command)
    {
        var access = Access(device);
        if (command.Command is "power.lock" or "power.shutdown")
        { if (!access.Power) throw new UnauthorizedAccessException("PC chưa cấp quyền khóa/tắt máy."); }
        else if (command.Command == "ai.send")
        {
            if (!access.Ai) throw new UnauthorizedAccessException("PC chưa cấp quyền gửi yêu cầu AI.");
            if (string.IsNullOrWhiteSpace(command.Prompt) || command.Target.Length == 0 || command.Version.Length == 0)
                throw new ArgumentException("Kiểm tra trạng thái AI rồi nhập yêu cầu.");
        }
        else throw new ArgumentException("Lệnh PC chưa được hỗ trợ.");
    }
    public async Task<PcPacket> HandleAsync(string device, PcPacket request, CancellationToken ct)
    {
        request.Validate(); ct.ThrowIfCancellationRequested();
        var access = Access(device);
        switch (request.Action)
        {
            case "status": return new() { Power = access.Power, Ai = access.Ai, Roots = access.Roots.Select(r => r with { Path = "" }).ToList() };
            case "ai.inspect":
                if (!access.Ai) throw new UnauthorizedAccessException("PC chưa cấp quyền gửi yêu cầu AI.");
                return new() { Targets = await desktop.InspectAsync(ct) };
            case "files.list": case "files.open": case "files.read": case "files.close":
                return await files.HandleAsync(device, request, () => Access(device), ct);
            case "prepare":
                Authorize(device, request);
                var copy = JsonSerializer.Deserialize<PcPacket>(JsonSerializer.Serialize(request, MobileProtocol.Json), MobileProtocol.Json)!;
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                var expiry = clock.GetUtcNow().AddSeconds(45);
                lock (gate)
                {
                    foreach (var key in tickets.Where(p => p.Value.Expires.AddMinutes(10) < clock.GetUtcNow() && p.Value.Completion?.Task.IsCompleted != false).Select(p => p.Key).ToArray()) tickets.Remove(key);
                    if (tickets.Count >= 512 || tickets.Values.Count(t => t.Device == device && t.Expires > clock.GetUtcNow()) >= 16) throw new InvalidOperationException("Quá nhiều lệnh đang chờ. Thử lại sau.");
                    tickets.Add(token, new(device, copy, expiry));
                }
                return new() { Ticket = token, Expires = expiry.ToUnixTimeMilliseconds(), State = "prepared" };
            case "execute": return await Execute(device, request.Ticket, ct);
            case "result":
                lock (gate)
                {
                    if (!tickets.TryGetValue(request.Ticket, out var known) || known.Device != device) throw new ArgumentException("Không tìm thấy lệnh; không tự gửi lại.");
                    return known.Completion?.Task.IsCompletedSuccessfully == true ? known.Completion.Task.Result : new() { State = known.Completion is null ? "prepared" : "running", Message = "Chưa có kết quả cuối cùng." };
                }
            default: throw new ArgumentException("Chức năng PC chưa được hỗ trợ.");
        }
    }
    private async Task<PcPacket> Execute(string device, string token, CancellationToken ct)
    {
        Ticket entry; bool owner;
        lock (gate)
        {
            if (!tickets.TryGetValue(token, out entry!) || entry.Device != device) throw new ArgumentException("Lệnh không tồn tại hoặc PC đã khởi động lại. Không tự gửi lại.");
            owner = entry.Completion is null;
            if (owner)
            {
                if (entry.Expires <= clock.GetUtcNow()) throw new ArgumentException("Lệnh đã hết hạn. Kiểm tra lại trước khi gửi.");
                Authorize(device, entry.Request);
                entry.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        if (!owner) return await entry.Completion!.Task.WaitAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            void Check() { timeout.Token.ThrowIfCancellationRequested(); if (entry.Expires <= clock.GetUtcNow()) throw new InvalidOperationException("Lệnh hết hạn."); Authorize(device, entry.Request); }
            Check();
            var message = await desktop.ExecuteAsync(entry.Request, Check, timeout.Token);
            entry.Completion!.TrySetResult(new() { State = "done", Message = message });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Even an uncertain outcome is terminal for this ticket; never execute it twice.
            entry.Completion!.TrySetResult(new() { State = ex is OperationCanceledException or TimeoutException ? "unknown" : "failed", Message = ex is OperationCanceledException or TimeoutException ? "Chưa xác định kết quả. Kiểm tra PC trước khi gửi lệnh mới." : ex.Message });
        }
        return await entry.Completion!.Task;
    }
    public void Dispose() => files.Dispose();
}

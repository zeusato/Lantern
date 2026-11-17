using System.Diagnostics;
using System.Security.Cryptography;
using Lantern.Core;

internal static class PcControlTests
{
    internal sealed class Desktop : IPcDesktop
    {
        public int Calls;
        public Func<Action, CancellationToken, Task<string>>? Work;
        public Task<List<PcTarget>> InspectAsync(CancellationToken ct) => Task.FromResult(new List<PcTarget> { new("test", "Fake AI", "Chat", "idle", "version", "Ready") });
        public async Task<string> ExecuteAsync(PcPacket request, Action authorize, CancellationToken ct)
        { if (Work is not null) return await Work(authorize, ct); authorize(); Interlocked.Increment(ref Calls); return "accepted"; }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        private readonly List<ManualTimer> timers = [];
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new ManualTimer(callback, state); timers.Add(timer); return timer; }
        public void Tick() { foreach (var timer in timers.ToArray()) timer.Fire(); }
        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            public void Fire() { if (!disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    public static async Task Run(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "pc-commands"); Directory.CreateDirectory(folder);
        using var vault = WorkspaceVault.Create(Path.Combine(folder, "vault"), "PC-command-test-password!");
        var registry = new MobileRegistry(folder, vault); registry.Add(new() { Id = "phone", Name = "Phone" }); registry.Add(new() { Id = "other", Name = "Other" });
        var desktop = new Desktop(); var clock = new Clock(); bool available = true;
        using var pc = new PcControl(registry, desktop, () => available, clock);
        Task<PcPacket> Call(PcPacket p, string device = "phone") => pc.HandleAsync(device, p, CancellationToken.None);
        async Task Denied(Func<Task> action, string name)
        { bool denied = false; try { await action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException) { denied = true; } check(denied, name); }
        var initial = await Call(new() { Action = "status" });
        check(!initial.Power && !initial.Ai && initial.Roots.Count == 0, "Existing pairing grants no PC authority by default");
        await Denied(() => Call(new() { Action = "prepare", Command = "power.shutdown" }), "Pairing alone cannot shut down the PC");
        await Denied(() => Call(new() { Action = "ai.inspect" }), "AI inspection requires separate permission");
        registry.SetAccess("phone", new() { Power = true, Ai = true });
        var first = await Call(new() { Action = "prepare", Command = "power.lock" });
        await Denied(() => Call(new() { Action = "execute", Ticket = first.Ticket }, "other"), "Command ticket is bound to the paired device");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(new() { Action = "execute", Ticket = first.Ticket })));
        check(desktop.Calls == 1 && results.All(r => r.State == "done"), "Concurrent and lost-response retries execute a command only once");
        check((await Call(new() { Action = "result", Ticket = first.Ticket })).State == "done", "Phone can query the prior command result without executing again");
        var expired = await Call(new() { Action = "prepare", Command = "power.shutdown" }); clock.Now = clock.Now.AddSeconds(46);
        await Denied(() => Call(new() { Action = "execute", Ticket = expired.Ticket }), "Late/offline commands expire on the PC clock");
        var revoked = await Call(new() { Action = "prepare", Command = "power.lock" }); registry.SetAccess("phone", new());
        await Denied(() => Call(new() { Action = "execute", Ticket = revoked.Ticket }), "Permission revoked after preparation prevents execution");
        registry.SetAccess("phone", new() { Power = true });
        var restart = await Call(new() { Action = "prepare", Command = "power.lock" });
        using (var restarted = new PcControl(registry, desktop, () => true, clock)) await Denied(() => restarted.HandleAsync("phone", new() { Action = "execute", Ticket = restart.Ticket }, CancellationToken.None), "PC restart invalidates every outstanding ticket");
        var uncertain = await Call(new() { Action = "prepare", Command = "power.lock" });
        desktop.Work = (_, _) => { desktop.Calls++; throw new TimeoutException(); };
        check((await Call(new() { Action = "execute", Ticket = uncertain.Ticket })).State == "unknown", "Ambiguous desktop outcome is reported as unknown");
        int attempts = desktop.Calls; await Call(new() { Action = "execute", Ticket = uncertain.Ticket });
        check(desktop.Calls == attempts, "An uncertain action is never automatically executed again"); desktop.Work = null;
        available = false;
        await Denied(() => Call(new() { Action = "status" }), "Locking Lantern gates the independent PC channel"); available = true;
        await Denied(() => Call(new() { Action = "prepare", Command = "cmd.exe" }), "Remote arbitrary shell/executable commands are rejected");

        var shared = Path.Combine(folder, "shared"); Directory.CreateDirectory(shared);
        var outside = Path.Combine(folder, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "private.txt"), "not shared");
        var data = RandomNumberGenerator.GetBytes(280000); File.WriteAllBytes(Path.Combine(shared, "tài liệu.bin"), data);
        File.WriteAllBytes(Path.Combine(shared, "empty.txt"), []);
        var sharedRoot = new PcRoot(Guid.NewGuid().ToString("N"), "Shared", shared); registry.SetAccess("phone", new() { Roots = [sharedRoot] });
        check((await Call(new() { Action = "status" })).Roots.Single().Path == "", "Root metadata never discloses the physical PC path");
        var list = await Call(new() { Action = "files.list", Root = sharedRoot.Id });
        check(list.Entries.Any(e => e.Name == "tài liệu.bin" && e.Size == data.Length), "Shared tree lists Unicode names and file metadata");
        foreach (var path in new[] { "../outside/private.txt", outside, "tài liệu.bin:secret", "\\\\server\\share", "sub\\..\\outside", "file. ", "/absolute" })
            await Denied(() => Call(new() { Action = "files.open", Root = sharedRoot.Id, Path = path }), "Reject traversal/absolute/ADS path: " + path);
        await Denied(() => Call(new() { Action = "files.list", Root = sharedRoot.Id }, "other"), "Other paired devices cannot browse an ungranted root");
        var opened = await Call(new() { Action = "files.open", Root = sharedRoot.Id, Path = "tài liệu.bin" });
        check(opened.Hash == Convert.ToHexString(SHA256.HashData(data)), "Download opens a stable file with an end-to-end checksum");
        var chunk = await Call(new() { Action = "files.read", Ticket = opened.Ticket });
        var next = await Call(new() { Action = "files.read", Ticket = opened.Ticket, Offset = 262144 });
        check(Convert.FromBase64String(chunk.Data).Concat(Convert.FromBase64String(next.Data)).SequenceEqual(data), "256 KB chunks reconstruct the file exactly");
        await Denied(() => Call(new() { Action = "files.read", Ticket = opened.Ticket }, "other"), "Download handle cannot be used by another phone");
        await Denied(() => Call(new() { Action = "files.read", Ticket = opened.Ticket, Offset = -1 }), "Negative file offsets are rejected");
        registry.SetAccess("phone", new());
        await Denied(() => Call(new() { Action = "files.read", Ticket = opened.Ticket }), "Revocation stops an already-open download");
        registry.SetAccess("phone", new() { Roots = [sharedRoot] });
        var empty = await Call(new() { Action = "files.open", Root = sharedRoot.Id, Path = "empty.txt" });
        check(empty.Size == 0 && empty.Hash == Convert.ToHexString(SHA256.HashData([])), "Empty files have a valid checksum and zero-byte transfer");
        await Call(new() { Action = "files.close", Ticket = empty.Ticket });
        var timed = await Call(new() { Action = "files.open", Root = sharedRoot.Id, Path = "empty.txt" }); clock.Now = clock.Now.AddMinutes(3);
        await Denied(() => Call(new() { Action = "files.read", Ticket = timed.Ticket }), "Abandoned file sessions expire and release their handles");
        await Call(new() { Action = "files.open", Root = sharedRoot.Id, Path = "empty.txt" });
        clock.Now = clock.Now.AddMinutes(3); clock.Tick();
        using (var exclusive = new FileStream(Path.Combine(shared, "empty.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(exclusive.Length == 0, "Idle cleanup releases abandoned file handles without another phone request");
        var junction = Path.Combine(shared, "link");
        using (var cmd = Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "/c", "mklink", "/J", junction, outside } }))
        {
            await cmd!.WaitForExitAsync();
            check(cmd.ExitCode == 0, "Create isolated junction fixture");
        }
        await Denied(() => Call(new() { Action = "files.open", Root = sharedRoot.Id, Path = "link/private.txt" }), "Directory junction cannot escape the granted tree");
        check(!(await Call(new() { Action = "files.list", Root = sharedRoot.Id })).Entries.Any(e => e.Name == "link"), "Tree omits reparse points");
        for (int i = 0; i < 103; i++) File.WriteAllText(Path.Combine(shared, "page-" + i + ".txt"), "");
        var paged = await Call(new() { Action = "files.list", Root = sharedRoot.Id });
        check(paged.Entries.Count == 100 && paged.More && (await Call(new() { Action = "files.list", Root = sharedRoot.Id, Page = 1 })).Entries.Count == 5, "Large folders are paginated without recursively scanning the tree");
        check(PcAiState.Classify(true, false, false, true) == "busy" && PcAiState.Classify(false, true, false, true) == "attention", "AI busy and waiting-for-approval are separate states");
        check(PcAiState.Classify(false, false, true, true) == "draft" && PcAiState.Classify(false, false, false, false) == "unknown", "Drafts and unrecognized chats are never treated as idle");
        check(PcAiState.Classify(false, false, false, true) == "idle", "Recognized empty idle composer can accept a prompt");
    }
}

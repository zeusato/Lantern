using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Lantern.App;

internal sealed class ProfileInstance : IDisposable
{
    private static readonly NamedWaitHandleOptions Scope = new() { CurrentSessionOnly = true, CurrentUserOnly = true };
    private readonly FileStream profileLock;
    private readonly EventWaitHandle activation;
    private readonly RegisteredWaitHandle listener;

    private ProfileInstance(FileStream profileLock, Action activate)
    {
        this.profileLock = profileLock;
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(Environment.ProcessId), Scope);
        listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => activate(), null, Timeout.Infinite, false);
        try
        {
            Span<byte> pid = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(pid, Environment.ProcessId);
            profileLock.Write(pid);
            profileLock.Flush();
        }
        catch { Dispose(); throw; }
    }

    // The profile still has a single writer. Readers can obtain its owner's PID even through a directory alias.
    public static ProfileInstance? OpenOrActivate(string directory, Action activate)
    {
        string path = Path.Combine(directory, "profile.lock");
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            FileStream owner;
            try { owner = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read); }
            catch (IOException ex) when ((ex.HResult & 0xffff) == 32)
            {
                if (TryActivate(path)) return null;
                // A concurrent launch can see the lock before the owner has published its event.
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(3))
                    throw new IOException("Hồ sơ đang được sử dụng. Nếu đang chạy bản Lantern cũ, hãy mở từ khay hệ thống.", ex);
                Thread.Sleep(50);
                continue;
            }
            try { return new ProfileInstance(owner, activate); }
            catch { owner.Dispose(); throw; }
        }
    }

    private static bool TryActivate(string path)
    {
        try
        {
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            reader.ReadExactly(bytes);
            int pid = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            if (pid <= 0 || !EventWaitHandle.TryOpenExisting(EventName(pid), Scope, out var signal)) return false;
            using (signal)
            {
                AllowSetForegroundWindow(pid);
                return signal.Set();
            }
        }
        catch (IOException) { return false; }
    }

    private static string EventName(int pid) => $"Lantern.Activate.{pid}";

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    public void Dispose()
    {
        listener.Unregister(null);
        activation.Dispose();
        profileLock.Dispose();
    }
}

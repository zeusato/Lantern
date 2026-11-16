using System.Runtime.InteropServices;

namespace Lantern.App;

internal static class PresenceMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size; public uint Tick; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInput info);
    internal static string Current(bool doNotDisturb)
    {
        if (doNotDisturb) return "dnd";
        var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref input) && unchecked((uint)Environment.TickCount - input.Tick) >= 15 * 60 * 1000 ? "idle" : "online";
    }
}

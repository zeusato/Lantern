using Microsoft.Win32;
using System.IO;

namespace Lantern.App;
internal static class WindowsStartup
{
    internal const string ValueName = "Lantern";
    internal static string Command(string executable, string? directory = null) => "\"" + Path.GetFullPath(executable) + "\"" + (directory is null ? "" : " --data \"" + Path.GetFullPath(directory) + "\"");
    internal static void Apply(bool enabled)
    {
        if (App.TestMode) return;
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new IOException("Không xác định được đường dẫn Lantern.");
            // Only the installed/published app registers itself, never dotnet or the test runner.
            if (!string.Equals(Path.GetFileName(executable), "Lantern.exe", StringComparison.OrdinalIgnoreCase)) return;
            key.SetValue(ValueName, Command(executable, App.DataDirectory), RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, false);
    }
}

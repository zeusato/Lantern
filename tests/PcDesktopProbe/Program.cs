// Read-only manual probe of the production adapter. It restores existing AI windows,
// reports status, and NEVER calls ExecuteAsync or captures a screenshot.
using System.Text.Json;
using Lantern.Core;
var type = typeof(Lantern.App.MainWindow).Assembly.GetType("Lantern.App.PcDesktop", true)!;
var desktop = (IPcDesktop)Activator.CreateInstance(type, true)!;
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var targets = await desktop.InspectAsync(timeout.Token);
Console.WriteLine(JsonSerializer.Serialize(targets, new JsonSerializerOptions { WriteIndented = true }));
if (args.Contains("--minimized"))
{
    var windows = targets.Where(t => !t.Id.StartsWith("hidden:", StringComparison.Ordinal)).Select(t => new IntPtr(long.Parse(t.Id.Split(':')[2]))).ToArray();
    if (windows.Length == 0) throw new InvalidOperationException("Open an AI app before testing window restoration.");
    try
    {
        foreach (var window in windows) Native.ShowWindowAsync(window, 6);
        await Task.Delay(500);
        if (!windows.All(Native.IsIconic)) throw new InvalidOperationException("Windows did not minimize the test windows.");
        using var restoredTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var restored = await desktop.InspectAsync(restoredTimeout.Token);
        if (windows.Any(Native.IsIconic) || restored.Count(t => !t.Id.StartsWith("hidden:", StringComparison.Ordinal)) < windows.Length)
            throw new InvalidOperationException("The adapter failed to discover and restore a minimized AI window.");
        Console.WriteLine("PASS: minimized AI windows were discovered and restored; no prompts sent.");
    }
    finally { foreach (var window in windows) Native.ShowWindowAsync(window, 9); }
}
if (args.Contains("--diagnostics"))
{
    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
    foreach (var target in (System.Collections.IEnumerable)type.GetMethod("Discover", flags)!.Invoke(null, null)!)
    {
        try
        {
            var snapshot = type.GetMethod("Read", flags)!.Invoke(null, [target])!;
            var nodes = (List<System.Windows.Automation.AutomationElement>)snapshot.GetType().GetProperty("Nodes")!.GetValue(snapshot)!;
            var editor = (System.Windows.Automation.AutomationElement)snapshot.GetType().GetProperty("Editor")!.GetValue(snapshot)!;
            var bounds = editor.Current.BoundingRectangle;
            string editorValue = ((System.Windows.Automation.ValuePattern)editor.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).Current.Value;
            string? editorText = editor.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out var textPattern) ? ((System.Windows.Automation.TextPattern)textPattern).DocumentRange.GetText(100) : null;
            Console.WriteLine(JsonSerializer.Serialize(new { EditorName = editor.Current.Name, ValueLength = editorValue.Length, TextLength = editorText?.Length, WhitespaceOnly = string.IsNullOrWhiteSpace(editorValue), editor.Current.HelpText, Patterns = editor.GetSupportedPatterns().Select(p => p.ProgrammaticName) }));
            foreach (var node in nodes.Where(n => n.Current.ControlType == System.Windows.Automation.ControlType.Document || n.Current.ControlType == System.Windows.Automation.ControlType.ToolBar || n == editor || n.Current.ControlType == System.Windows.Automation.ControlType.Button && !n.Current.IsOffscreen && n.Current.BoundingRectangle.Top >= bounds.Top - 100 && n.Current.BoundingRectangle.Bottom <= bounds.Bottom + 180))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { Kind = node.Current.ControlType.ProgrammaticName, node.Current.Name, node.Current.IsOffscreen, Rect = node.Current.BoundingRectangle.ToString() }));
            }
        }
        catch (Exception e) { Console.WriteLine(e.GetBaseException().Message); }
    }
}
internal static class Native
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr window, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
}

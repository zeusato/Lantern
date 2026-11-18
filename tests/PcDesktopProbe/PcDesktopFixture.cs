using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Lantern.Core;

internal static class PcDesktopFixture
{
    public static async Task Run()
    {
        var started = new TaskCompletionSource<(Window Window, TextBox Editor, Button Send, TextBlock Message)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var layout = new DockPanel();
                var toolbar = new ToolBar(); AutomationProperties.SetName(toolbar, "Chat toolbar");
                var heading = new Button { Content = "Synthetic QA conversation" }; AutomationProperties.SetName(heading, "Synthetic QA conversation"); ToolBar.SetOverflowMode(heading, OverflowMode.Never);
                toolbar.Items.Add(heading); DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
                var composer = new StackPanel(); DockPanel.SetDock(composer, Dock.Bottom);
                var editor = new TextBox { Height = 40 }; AutomationProperties.SetName(editor, "Work with Codex"); composer.Children.Add(editor);
                var send = new Button { Content = "Send", Height = 32 }; composer.Children.Add(send); layout.Children.Add(composer);
                var history = new StackPanel();
                for (int i = 0; i < 120; i++) history.Children.Add(new TextBlock { Text = "Synthetic history " + i });
                var message = new TextBlock { Text = "Synthetic sent prompt" }; history.Children.Add(message);
                layout.Children.Add(new ScrollViewer { Content = history });
                var window = new Window { Title = "Lantern isolated UIA test", Width = 520, Height = 440, Content = layout, ShowActivated = false };
                window.Show(); window.UpdateLayout();
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => started.SetResult((window, editor, send, message)));
                Dispatcher.Run();
            }
            catch (Exception ex) { started.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        var fixture = await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var dispatcher = fixture.Window.Dispatcher;
        try
        {
            var type = typeof(Lantern.App.MainWindow).Assembly.GetType("Lantern.App.PcDesktop", true)!;
            var targetType = type.GetNestedType("Target", BindingFlags.NonPublic)!;
            var handle = await dispatcher.InvokeAsync(() => new WindowInteropHelper(fixture.Window).Handle);
            var target = Activator.CreateInstance(targetType, "fixture", "ChatGPT / Codex", Environment.ProcessId, handle)!;
            var read = type.GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)!;
            async Task<(PcTarget Status, object Snapshot)> Read(string? prompt = null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var snapshot = await Task.Run(() => read.Invoke(null, [target, timeout.Token, prompt])!, timeout.Token).WaitAsync(timeout.Token);
                return ((PcTarget)snapshot.GetType().GetProperty("Public")!.GetValue(snapshot)!, snapshot);
            }
            static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS UIA: " + name); }
            var watch = Stopwatch.StartNew(); var idle = await Read();
            Check(idle.Status.State == "idle" && idle.Status.Conversation == "Synthetic QA conversation", "Cached controls return the correct idle conversation");
            Console.WriteLine($"UIA fixture read: {watch.Elapsed.TotalMilliseconds:F0} ms");
            await dispatcher.InvokeAsync(() => fixture.Editor.Text = "Keep this draft");
            Check((await Read()).Status.State == "draft", "Cached inspection never treats a draft as idle");
            await dispatcher.InvokeAsync(() => { fixture.Editor.Text = ""; fixture.Send.Content = "Stop"; });
            Check((await Read()).Status.State == "busy", "Cached inspection recognizes busy controls");
            await dispatcher.InvokeAsync(() => fixture.Send.Content = "Send");
            var acknowledged = await Read("Synthetic sent prompt");
            var nodes = (List<AutomationElement>)acknowledged.Snapshot.GetType().GetProperty("Nodes")!.GetValue(acknowledged.Snapshot)!;
            Check(nodes.Any(e => e.Cached.ControlType == ControlType.Text && e.Cached.Name == "Synthetic sent prompt"), "Send acknowledgement includes the exact sent text");
            Check(!nodes.Any(e => e.Cached.Name.StartsWith("Synthetic history ")), "Unrelated history text is excluded from status snapshots");
            Check(acknowledged.Status.Version == idle.Status.Version, "Batch reads preserve the pinned conversation identity");
        }
        finally { await dispatcher.InvokeAsync(() => { fixture.Window.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }); }
    }
}

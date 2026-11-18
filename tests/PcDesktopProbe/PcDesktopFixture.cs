using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Lantern.Core;

internal static class PcDesktopFixture
{
    private sealed class Document : Decorator
    {
        public string Title { get; set; } = "";
        public string Route { get; set; } = "";
        protected override AutomationPeer OnCreateAutomationPeer() => new DocumentPeer(this);
    }
    private sealed class DocumentPeer(Document owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
        protected override string GetNameCore() => owner.Title;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => true;
        public string Value => owner.Route;
        public void SetValue(string value) => throw new InvalidOperationException();
    }
    private sealed record View(Window Window, Document Document, ToolBar Toolbar, Button Heading, TextBox Editor, Button Send, Button Stop, TextBlock Message);

    public static async Task Run()
    {
        var started = new TaskCompletionSource<View>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var layout = new DockPanel();
                var toolbar = new ToolBar(); AutomationProperties.SetName(toolbar, "Chat toolbar");
                var heading = new Button { Content = "Synthetic QA conversation" }; ToolBar.SetOverflowMode(heading, OverflowMode.Never);
                toolbar.Items.Add(heading); DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
                var composer = new StackPanel(); DockPanel.SetDock(composer, Dock.Bottom);
                var editor = new TextBox { Height = 40 }; AutomationProperties.SetName(editor, "Work with Codex"); composer.Children.Add(editor);
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var send = new Button { Content = "Send", Height = 32, Width = 100 };
                var stop = new Button { Content = "Stop", Height = 32, Width = 100, IsEnabled = false };
                actions.Children.Add(send); actions.Children.Add(stop); composer.Children.Add(actions); layout.Children.Add(composer);
                var history = new StackPanel();
                for (int i = 0; i < 120; i++) history.Children.Add(new TextBlock { Text = "Synthetic history " + i });
                var message = new TextBlock { Text = "Synthetic sent prompt" }; history.Children.Add(message);
                layout.Children.Add(new ScrollViewer { Content = history });
                var document = new Document { Title = "Synthetic QA conversation", Route = "https://chatgpt.com/c/fixture", Child = layout };
                var shell = new DockPanel();
                // A second document has an enabled Stop within the old geometric search region.
                var unrelated = new Document { Title = "Unrelated tool", Child = new Button { Content = "Stop", Height = 30 } };
                DockPanel.SetDock(unrelated, Dock.Bottom); shell.Children.Add(unrelated); shell.Children.Add(document);
                var outer = new Document { Route = "file:///fixture-shell.html", Child = shell };
                var window = new Window { Title = "Lantern isolated UIA test", Width = 520, Height = 440, Content = outer, ShowActivated = false };
                window.Show(); window.UpdateLayout();
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => started.SetResult(new(window, document, toolbar, heading, editor, send, stop, message)));
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
            object Target(string app) => Activator.CreateInstance(targetType, "fixture", app, Environment.ProcessId, handle)!;
            var read = type.GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)!;
            async Task<(PcTarget Status, object Snapshot)> Read(string app = "ChatGPT / Codex", string? prompt = null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var snapshot = await Task.Run(() => read.Invoke(null, [Target(app), timeout.Token, prompt])!, timeout.Token).WaitAsync(timeout.Token);
                return ((PcTarget)snapshot.GetType().GetProperty("Public")!.GetValue(snapshot)!, snapshot);
            }
            static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS UIA: " + name); }
            var watch = Stopwatch.StartNew(); var idle = await Read();
            Check(idle.Status.State == "idle" && idle.Status.Conversation == "Synthetic QA conversation", "Idle ignores disabled Stop and a busy unrelated document");
            Console.WriteLine($"UIA fixture read: {watch.Elapsed.TotalMilliseconds:F0} ms");
            await dispatcher.InvokeAsync(() => fixture.Editor.Text = "Keep this draft");
            Check((await Read()).Status.State == "draft", "Cached inspection never treats a draft as idle");
            await dispatcher.InvokeAsync(() => { fixture.Editor.Text = ""; fixture.Stop.IsEnabled = true; });
            Check((await Read()).Status.State == "busy", "An enabled Stop in the active composer is busy");
            await dispatcher.InvokeAsync(() => fixture.Stop.IsEnabled = false);
            var acknowledged = await Read(prompt: "Synthetic sent prompt");
            var nodes = (List<AutomationElement>)acknowledged.Snapshot.GetType().GetProperty("Nodes")!.GetValue(acknowledged.Snapshot)!;
            Check(nodes.Any(e => e.Cached.ControlType == ControlType.Text && e.Cached.Name == "Synthetic sent prompt"), "Send acknowledgement includes the exact sent text");
            Check(!nodes.Any(e => e.Cached.Name.StartsWith("Synthetic history ")), "Unrelated history text is excluded from status snapshots");
            Check(acknowledged.Status.Version == idle.Status.Version, "Batch reads preserve the pinned conversation identity");
            await dispatcher.InvokeAsync(() => { fixture.Document.Title = "New generated title"; fixture.Heading.Content = fixture.Document.Title; });
            Check((await Read()).Status.Version == idle.Status.Version, "Renaming the same route keeps its identity");
            await dispatcher.InvokeAsync(() => fixture.Document.Route = "https://chatgpt.com/c/another");
            Check((await Read()).Status.Version != idle.Status.Version, "Different routes with reused controls and titles never share a version");
            foreach (var app in new[] { "Claude", "ChatGPT / Codex" })
            {
                await dispatcher.InvokeAsync(() =>
                {
                    fixture.Toolbar.Visibility = Visibility.Collapsed;
                    fixture.Document.Title = app == "Claude" ? "Claude Code" : "ChatGPT";
                    fixture.Document.Route = app == "Claude" ? "https://claude.ai/new" : "https://chatgpt.com/";
                    AutomationProperties.SetName(fixture.Editor, app == "Claude" ? "Prompt" : "Work with Codex");
                    fixture.Send.Content = app == "Claude" ? "Send message" : "Send"; fixture.Send.IsEnabled = false;
                });
                var fresh = await Read(app);
                Check(fresh.Status.State == "idle" && fresh.Status.Conversation == "Hội thoại mới" && fresh.Status.Version.Length > 0, app + " new conversation is ready without a title or chat toolbar");
                await dispatcher.InvokeAsync(() => fixture.Editor.Text = "Existing draft");
                Check((await Read(app)).Status.State == "draft", app + " new conversation still protects existing drafts");
                await dispatcher.InvokeAsync(() => { fixture.Editor.Text = ""; fixture.Document.Title = "Existing conversation"; fixture.Document.Route += "existing"; });
                Check((await Read(app)).Status.Conversation == "Existing conversation", app + " reads its actual document instead of the outer shell");
            }
            // Exercise the production send path, including the first-send title/URL transition.
            int sends = 0; string sendApp = "";
            await dispatcher.InvokeAsync(() =>
            {
                fixture.Editor.TextChanged += (_, _) => fixture.Send.IsEnabled = fixture.Editor.Text.Length > 0;
                fixture.Send.Click += (_, _) => { sends++; fixture.Message.Text = fixture.Editor.Text; fixture.Editor.Text = ""; fixture.Document.Title = "Generated first title"; fixture.Document.Route = sendApp == "Claude" ? "https://claude.ai/epitaxy/local_new-session" : "https://chatgpt.com/c/new-session"; };
            });
            var sendMethod = type.GetMethod("Send", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var app in new[] { "Claude", "ChatGPT / Codex" })
            {
                int previousSends = sends;
                await dispatcher.InvokeAsync(() =>
                {
                    sendApp = app; fixture.Editor.Text = "";
                    fixture.Document.Title = app == "Claude" ? "Claude Code" : "ChatGPT";
                    fixture.Document.Route = app == "Claude" ? "https://claude.ai/new" : "https://chatgpt.com/";
                    AutomationProperties.SetName(fixture.Editor, app == "Claude" ? "Prompt" : "Work with Codex");
                    fixture.Send.Content = app == "Claude" ? "Send message" : "Send";
                });
                var beforeSend = await Read(app);
                using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var request = new PcPacket { Command = "ai.send", Target = "fixture", Version = beforeSend.Status.Version, Prompt = "First prompt fixture only for " + app };
                var sent = await Task.Run(() => (string)sendMethod.Invoke(null, [Target(app), request, (Action)(() => sendTimeout.Token.ThrowIfCancellationRequested()), sendTimeout.Token])!).WaitAsync(sendTimeout.Token);
                Check(sends == previousSends + 1 && sent.StartsWith("Đã gửi prompt"), app + " first prompt is sent once and confirmed after its title and route change");
                bool staleRejected = false;
                try { await Task.Run(() => sendMethod.Invoke(null, [Target(app), request, (Action)(() => { }), sendTimeout.Token])); }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { staleRejected = true; }
                Check(staleRejected && sends == previousSends + 1, app + " stale new-chat version cannot send into the created conversation");
            }
        }
        finally { await dispatcher.InvokeAsync(() => { fixture.Window.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }); }
    }
}

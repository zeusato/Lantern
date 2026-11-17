using System.Net;
using Lantern.Core;

internal static class RelayEnrollmentTests
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "automatic-relay");
        using var vault = WorkspaceVault.Create(path, "relay-fixture-password");
        var registry = new MobileRegistry(path, vault);
        registry.Add(new() { Id = "existing-lan-phone", Name = "LAN phone" });
        registry.EnsureAutomaticRelay(); var config = registry.Snapshot().Relay;
        registry.EnsureAutomaticRelay();
        var restored = new MobileRegistry(path, vault).Snapshot();
        check(config.Automatic && config.Enabled && config.ServiceToken.Length == 0 && restored.Relay.InstallationKey == config.InstallationKey && restored.Devices.Count == 1, "A fresh PC gets a persistent independent relay capability without losing LAN devices");
        check(!File.ReadAllText(Path.Combine(path, "mobile-devices.enc")).Contains(config.InstallationKey), "Automatic relay capability is encrypted at rest");
        using var secondVault = WorkspaceVault.Create(path + "-second", "relay-fixture-password");
        var second = new MobileRegistry(path + "-second", secondVault); second.EnsureAutomaticRelay();
        check(second.Snapshot().Relay.InstallationKey != config.InstallationKey, "Different PCs never share their installation capability");
        var handler = new RecordingHandler(); using var api = new RelayApi(config, handler);
        var route = new MobileRoute();
        await api.Register(route, CancellationToken.None);
        await api.Call("v1/routes/" + route.Id + "/state", route.HostKey, null, CancellationToken.None);
        check(handler.Requests[0] is { Path: "/v1/installations", Bearer: var enrollment } && enrollment == config.InstallationKey && handler.Requests[1].Installation == config.InstallationKey && handler.Requests[2].Installation.Length == 0 && handler.Requests.All(r => !r.ServiceHeader), "Enrollment authorizes only registration; route traffic has no service or installation credential");
        registry.Configure(new() { Url = "https://private.example/", ServiceToken = "synthetic-private-key" }); registry.EnsureAutomaticRelay();
        check(registry.Snapshot().Relay.ServiceToken == "synthetic-private-key", "Existing private relay settings remain unchanged on upgrade");
        var failure = new RecordingHandler { Fail = true }; using var failed = new RelayApi(config, failure);
        bool rejected = false; try { await failed.Register(route, CancellationToken.None); } catch (HttpRequestException) { rejected = true; }
        check(rejected && failure.Requests.Count == 1, "Enrollment failure never proceeds to create an unauthorized route");
    }
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public bool Fail { get; init; }
        public List<(string Path, string Bearer, string Installation, bool ServiceHeader)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization!.Parameter!, request.Headers.TryGetValues("X-Lantern-Installation", out var values) ? values.Single() : "", request.Headers.Contains("OAI-Sites-Authorization")));
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
        }
    }
}

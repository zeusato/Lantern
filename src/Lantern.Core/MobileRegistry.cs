using System.Security.Cryptography;
using System.Text.Json;

namespace Lantern.Core;

public sealed class RelayConfiguration
{
    public const string PublicUrl = "https://lantern-private-relay-zeusato.tranquymtcn.chatgpt.site/";
    public string Url { get; set; } = "";
    public string ServiceToken { get; set; } = "";
    public string InstallationKey { get; set; } = "";
    public bool Automatic => Url == PublicUrl && InstallationKey.Length == 43 && InstallationKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') && ServiceToken.Length == 0;
    public bool Enabled => Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.UserInfo) && uri.Query.Length == 0 && uri.Fragment.Length == 0 && (Automatic || ServiceToken.Length is > 0 and <= 6000 && !ServiceToken.Any(char.IsControl));
}
public sealed class MobileRoute
{
    public string Transport { get; set; } = "tls";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string HostKey { get; set; } = Secret();
    public string ClientKey { get; set; } = Secret();
    public static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
public sealed class MobileDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset Added { get; set; } = DateTimeOffset.UtcNow;
    public MobileRoute? Route { get; set; }
    public PcAccess Access { get; set; } = new();
    public bool RequireAppPassword { get; set; }
}
public sealed class MobileState
{
    public RelayConfiguration Relay { get; set; } = new();
    public List<MobileDevice> Devices { get; set; } = [];
}
public sealed class MobileRegistry
{
    private readonly string file;
    private readonly WorkspaceVault vault;
    private readonly object gate = new();
    private MobileState state;
    public MobileRegistry(string directory, WorkspaceVault vault)
    {
        this.vault = vault; file = Path.Combine(directory, "mobile-devices.enc");
        if (File.Exists(file))
        {
            var sealedState = File.ReadAllText(file);
            // Unlike legacy chat storage, device authorization has no plaintext migration format.
            if (!sealedState.StartsWith("enc:v1:", StringComparison.Ordinal)) throw new InvalidDataException("Danh sách thiết bị không được mã hóa hoặc đã bị thay đổi.");
            state = JsonSerializer.Deserialize<MobileState>(vault.Unseal(sealedState), MobileProtocol.Json) ?? throw new InvalidDataException("Danh sách thiết bị không hợp lệ.");
        }
        else state = new();
    }
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, MobileProtocol.Json), MobileProtocol.Json)!;
    public MobileState Snapshot() { lock (gate) return Copy(state); }
    public void EnsureAutomaticRelay()
    {
        lock (gate)
        {
            if (state.Relay.Enabled) return;
            if (state.Relay.Url.Length > 0 || state.Relay.ServiceToken.Length > 0 || state.Relay.InstallationKey.Length > 0)
                throw new InvalidOperationException("Cấu hình relay riêng không hợp lệ. Kiểm tra trong mục Nâng cao.");
            // Persist BEFORE contacting the relay: retries/restarts reuse the same installation.
            state.Relay = new() { Url = RelayConfiguration.PublicUrl, InstallationKey = MobileRoute.Secret() };
            Save();
        }
    }
    public bool Contains(string id) { lock (gate) return state.Devices.Any(d => d.Id == id); }
    public void RequireAppPassword(string id)
    {
        lock (gate)
        {
            var device = state.Devices.Find(d => d.Id == id) ?? throw new InvalidOperationException("Thiết bị đã bị gỡ.");
            if (device.RequireAppPassword) return;
            device.RequireAppPassword = true; Save();
        }
    }
    public void SetAccess(string id, PcAccess access)
    {
        access.Validate();
        lock (gate)
        {
            var device = state.Devices.Find(d => d.Id == id) ?? throw new InvalidOperationException("Thiết bị đã bị gỡ.");
            device.Access = Copy(access); Save();
        }
    }
    public void Add(MobileDevice device)
    {
        lock (gate)
        {
            var existing = state.Devices.FindIndex(d => d.Id == device.Id);
            if (existing >= 0) { state.Devices[existing] = Copy(device); Save(); return; }
            if (state.Devices.Count >= 8) throw new InvalidOperationException("Tối đa 8 điện thoại. Hãy gỡ thiết bị cũ.");
            state.Devices.Add(Copy(device)); Save();
        }
    }
    public MobileDevice? Remove(string id)
    {
        lock (gate) { var device = state.Devices.Find(d => d.Id == id); if (device is not null) { state.Devices.Remove(device); Save(); } return device; }
    }
    public void Configure(RelayConfiguration value)
    {
        if (value.Url.Length > 0 && !value.Enabled) throw new ArgumentException("Cần địa chỉ relay HTTPS hợp lệ và khóa truy cập dịch vụ.");
        lock (gate) { state.Relay = Copy(value); Save(); }
    }
    private void Save() { File.WriteAllText(file + ".tmp", vault.Seal(JsonSerializer.Serialize(state, MobileProtocol.Json))); File.Move(file + ".tmp", file, true); }
}
public sealed record MobilePairingRequest(string Id, string Name, string VerificationCode);
public sealed record MobileInvitation(string Nonce, DateTimeOffset Expires, MobileRoute? Route);

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Lantern.Core;

public sealed record SignedGroupAction(string Payload, string PublicKey, string Signature);
internal sealed class GroupActionBody
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "";
    public string ActorId { get; set; } = "";
    public ChatGroup Group { get; set; } = new();
    public string SuccessorId { get; set; } = "";
}
internal static class GroupActions
{
    private static byte[] SignedBytes(string payload) => Encoding.UTF8.GetBytes("Lantern-group-action-v1\n" + payload);
    public static SignedGroupAction Sign(Identity identity, GroupActionBody body)
    {
        body.ActorId = identity.Id;
        var payload = Codec.Encode(body);
        using var key = identity.Certificate.GetECDsaPrivateKey()!;
        return new(payload, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), Convert.ToBase64String(key.SignData(SignedBytes(payload), HashAlgorithmName.SHA256)));
    }
    public static GroupActionBody Verify(SignedGroupAction envelope)
    {
        try
        {
            if (envelope.Payload is null || envelope.PublicKey is null || envelope.Signature is null || envelope.Payload.Length > 32000 || envelope.PublicKey.Length > 1024 || envelope.Signature.Length > 256) throw new InvalidDataException("Lệnh nhóm không hợp lệ.");
            var body = Codec.Decode<GroupActionBody>(envelope.Payload);
            if (body.Id is null || body.Kind is not ("leave" or "transfer" or "delete") || body.ActorId is null || body.SuccessorId is null || body.Group is null || !Protocol.ValidId(body.Id)) throw new InvalidDataException("Lệnh nhóm không hợp lệ.");
            Protocol.Validate(new WireMessage { Group = body.Group }); GroupMembership.Validate(body.Group);
            if (body.Group.Deleted || !body.Group.Members.Contains(body.ActorId) || body.Kind != "leave" && body.ActorId != body.Group.OwnerId || body.Kind == "transfer" && (body.SuccessorId == body.ActorId || !body.Group.Members.Contains(body.SuccessorId)) || body.Kind != "transfer" && body.SuccessorId.Length != 0) throw new InvalidDataException("Không có quyền thực hiện lệnh nhóm.");
            var publicKey = Convert.FromBase64String(envelope.PublicKey);
            if (Convert.ToHexString(SHA256.HashData(publicKey)) != body.ActorId) throw new InvalidDataException("Danh tính ký lệnh nhóm không khớp.");
            using var key = ECDsa.Create(); key.ImportSubjectPublicKeyInfo(publicKey, out var consumed);
            if (consumed != publicKey.Length || !key.VerifyData(SignedBytes(envelope.Payload), Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256)) throw new InvalidDataException("Chữ ký lệnh nhóm không hợp lệ.");
            return body;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or System.Text.Json.JsonException or ArgumentException) { throw new InvalidDataException("Lệnh nhóm không hợp lệ.", ex); }
    }
}

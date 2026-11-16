import Foundation
import Security
import CryptoKit

// Minimal DER for a P-256 self-signed TLS client certificate. No private key leaves Keychain.
enum DER {
    static func item(_ tag: UInt8, _ value: Data) -> Data {
        var length = Data()
        if value.count < 128 { length.append(UInt8(value.count)) }
        else {
            var n = value.count; var bytes: [UInt8] = []
            while n > 0 { bytes.insert(UInt8(n & 255), at: 0); n >>= 8 }
            length.append(UInt8(0x80 | bytes.count)); length.append(contentsOf: bytes)
        }
        return Data([tag]) + length + value
    }
    static func seq(_ data: Data) -> Data { item(0x30, data) }
    static let algorithm = Data([0x30,0x0a,0x06,0x08,0x2a,0x86,0x48,0xce,0x3d,0x04,0x03,0x02])
    static let spkiPrefix = Data([0x30,0x59,0x30,0x13,0x06,0x07,0x2a,0x86,0x48,0xce,0x3d,0x02,0x01,0x06,0x08,0x2a,0x86,0x48,0xce,0x3d,0x03,0x01,0x07,0x03,0x42,0x00])
    static func certificate(key: SecKey) throws -> SecCertificate {
        guard let publicKey = SecKeyCopyPublicKey(key), let raw = SecKeyCopyExternalRepresentation(publicKey, nil) as Data? else { throw LanternError.message("Không đọc được khóa thiết bị.") }
        let name = seq(item(0x31, seq(Data([0x06,0x03,0x55,0x04,0x03]) + item(0x0c, Data("Lantern iOS".utf8)))))
        let validity = seq(item(0x18, Data("20200101000000Z".utf8)) + item(0x18, Data("21200101000000Z".utf8)))
        var serial = Data(count: 16)
        guard serial.withUnsafeMutableBytes({ SecRandomCopyBytes(kSecRandomDefault, 16, $0.baseAddress!) }) == errSecSuccess else { throw LanternError.message("Không tạo được số ngẫu nhiên.") }
        serial[0] = (serial[0] & 0x7f) | 1
        let tbs = seq(item(0xa0, Data([0x02,0x01,0x02])) + item(0x02, serial) + algorithm + name + validity + name + spkiPrefix + raw)
        var error: Unmanaged<CFError>?
        guard let signature = SecKeyCreateSignature(key, .ecdsaSignatureMessageX962SHA256, tbs as CFData, &error) as Data?,
              let cert = SecCertificateCreateWithData(nil, seq(tbs + algorithm + item(0x03, Data([0]) + signature)) as CFData) else { throw LanternError.message("Không tạo được chứng chỉ thiết bị.") }
        return cert
    }
}

enum DeviceKeys {
    private static let tag = Data("vn.lantern.device.v1".utf8)
    private static let label = "Lantern iOS identity v1"
    private static let service = "vn.lantern.private.v1"
    static func identity() throws -> SecIdentity {
        var found: CFTypeRef?
        let query: [String: Any] = [kSecClass as String: kSecClassIdentity, kSecAttrLabel as String: label, kSecReturnRef as String: true]
        if SecItemCopyMatching(query as CFDictionary, &found) == errSecSuccess, let found { return (found as! SecIdentity) }
        let keyQuery: [String: Any] = [kSecClass as String: kSecClassKey, kSecAttrApplicationTag as String: tag, kSecAttrKeyType as String: kSecAttrKeyTypeECSECPrimeRandom, kSecReturnRef as String: true]
        var stored: CFTypeRef?
        let key: SecKey
        if SecItemCopyMatching(keyQuery as CFDictionary, &stored) == errSecSuccess, let stored { key = (stored as! SecKey) }
        else {
            let attributes: [String: Any] = [kSecAttrKeyType as String: kSecAttrKeyTypeECSECPrimeRandom, kSecAttrKeySizeInBits as String: 256,
                kSecPrivateKeyAttrs as String: [kSecAttrIsPermanent as String: true, kSecAttrApplicationTag as String: tag, kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly]]
            guard let created = SecKeyCreateRandomKey(attributes as CFDictionary, nil) else { throw LanternError.message("Không tạo được khóa trong Keychain.") }
            key = created
        }
        let cert = try DER.certificate(key: key)
        let status = SecItemAdd([kSecClass as String: kSecClassCertificate, kSecValueRef as String: cert, kSecAttrLabel as String: label,
                                kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly] as CFDictionary, nil)
        guard status == errSecSuccess || status == errSecDuplicateItem,
              SecItemCopyMatching(query as CFDictionary, &found) == errSecSuccess, let found else { throw LanternError.message("Không ghép được khóa và chứng chỉ TLS (\(status)).") }
        return (found as! SecIdentity)
    }
    static func fingerprint(_ cert: SecCertificate) throws -> String {
        guard let key = SecCertificateCopyKey(cert), let attrs = SecKeyCopyAttributes(key) as? [String: Any],
              attrs[kSecAttrKeyType as String] as? String == kSecAttrKeyTypeECSECPrimeRandom as String,
              attrs[kSecAttrKeySizeInBits as String] as? Int == 256,
              let raw = SecKeyCopyExternalRepresentation(key, nil) as Data?, raw.count == 65 else { throw LanternError.message("PC không dùng khóa P-256 hợp lệ.") }
        return (DER.spkiPrefix + raw).sha256
    }
    static func id() throws -> String {
        var cert: SecCertificate?
        guard SecIdentityCopyCertificate(try identity(), &cert) == errSecSuccess, let cert else { throw LanternError.message("Thiếu chứng chỉ thiết bị.") }
        return try fingerprint(cert)
    }
    static func get(_ account: String) throws -> Data? {
        var found: CFTypeRef?
        let status = SecItemCopyMatching([kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account, kSecReturnData as String: true] as CFDictionary, &found)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw LanternError.message("Keychain chưa sẵn sàng (\(status)).") }
        return found as? Data
    }
    static func put(_ account: String, _ data: Data?) throws {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
        guard let data else { SecItemDelete(query as CFDictionary); return }
        let values: [String: Any] = [kSecValueData as String: data, kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly]
        var status = SecItemUpdate(query as CFDictionary, values as CFDictionary)
        if status == errSecItemNotFound { status = SecItemAdd(query.merging(values) { _, b in b } as CFDictionary, nil) }
        guard status == errSecSuccess else { throw LanternError.message("Không lưu được Keychain (\(status)).") }
    }
    static func cacheKey() throws -> SymmetricKey {
        if let value = try get("cache-key") { return SymmetricKey(data: value) }
        let key = SymmetricKey(size: .bits256)
        try put("cache-key", key.withUnsafeBytes { Data($0) }); return key
    }
}

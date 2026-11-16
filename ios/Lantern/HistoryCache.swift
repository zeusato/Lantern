import Foundation
import CryptoKit

final class HistoryCache {
    private let folder: URL
    private let key: SymmetricKey
    init(pc: String) throws {
        key = try DeviceKeys.cacheKey()
        folder = Self.root.appendingPathComponent(Data(pc.utf8).sha256, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        var directory = folder; var values = URLResourceValues(); values.isExcludedFromBackup = true
        try directory.setResourceValues(values)
    }
    static var root: URL { FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("LanternCache", isDirectory: true) }
    private func path(_ name: String) -> URL { folder.appendingPathComponent(Data(name.utf8).sha256 + ".enc") }
    func remove(_ name: String) throws { let file = path(name); if FileManager.default.fileExists(atPath: file.path) { try FileManager.default.removeItem(at: file) } }
    func read(_ name: String) throws -> [Packet] {
        let file = path(name)
        guard FileManager.default.fileExists(atPath: file.path) else { return [] }
        let sealed = try AES.GCM.SealedBox(combined: Data(contentsOf: file))
        let plain = try AES.GCM.open(sealed, using: key, authenticating: Data((folder.lastPathComponent + name).utf8))
        return try JSONSerialization.jsonObject(with: plain) as? [Packet] ?? []
    }
    func write(_ name: String, _ packets: [Packet]) throws {
        let plain = try JSONSerialization.data(withJSONObject: packets)
        let box = try AES.GCM.seal(plain, using: key, authenticating: Data((folder.lastPathComponent + name).utf8))
        guard let bytes = box.combined else { throw LanternError.message("Không mã hóa được cache.") }
        try bytes.write(to: path(name), options: [.atomic, .completeFileProtection])
        let files = try FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.fileSizeKey, .contentModificationDateKey])
        var total = files.reduce(0) { $0 + ((try? $1.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) }
        for file in files.sorted(by: { ((try? $0.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast) < ((try? $1.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast) }) where total > 100 * 1024 * 1024 && file != path("outbox") {
            total -= (try? file.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
            try FileManager.default.removeItem(at: file)
        }
    }
    static func persistable(_ packets: [Packet]) -> [Packet] { packets.filter { !Message(raw: $0).ephemeral && !Message(raw: $0).expired } }
    static func clear() throws { if FileManager.default.fileExists(atPath: root.path) { try FileManager.default.removeItem(at: root) } }
}

import Foundation
import CryptoKit

typealias Packet = [String: Any]

enum LanternError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case let .message(text) = self { return text }; return nil }
}

extension Data {
    var hex: String { map { String(format: "%02X", $0) }.joined() }
    var sha256: String { Data(SHA256.hash(data: self)).hex }
}
extension Dictionary where Key == String, Value == Any {
    func string(_ key: String) -> String { self[key] as? String ?? "" }
    func int(_ key: String) -> Int { (self[key] as? NSNumber)?.intValue ?? 0 }
    func bool(_ key: String) -> Bool { self[key] as? Bool ?? false }
    func packets(_ key: String) -> [Packet] { self[key] as? [Packet] ?? [] }
}

enum Wire {
    static let maxFrame = 2 * 1024 * 1024
    static let maxFile = 100 * 1024 * 1024
    static func id() -> String { UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased() }
    static func frame(_ packet: Packet) throws -> Data {
        let body = try JSONSerialization.data(withJSONObject: packet)
        guard body.count <= maxFrame else { throw LanternError.message("Nội dung quá lớn.") }
        var size = UInt32(body.count).bigEndian
        return withUnsafeBytes(of: &size) { Data($0) } + body
    }
    static func length(_ header: Data) throws -> Int {
        guard header.count == 4 else { throw LanternError.message("Thiếu đầu gói dữ liệu.") }
        let size = header.reduce(0) { ($0 << 8) | Int($1) }
        guard (2...maxFrame).contains(size) else { throw LanternError.message("Gói PC vượt giới hạn.") }
        return size
    }
    static func decode(_ data: Data) throws -> Packet {
        guard data.count <= maxFrame, let packet = try JSONSerialization.jsonObject(with: data) as? Packet else {
            throw LanternError.message("Gói PC không hợp lệ.")
        }
        return packet
    }
    static func matches(_ value: String, _ expression: String) -> Bool {
        value.range(of: expression, options: .regularExpression) != nil
    }
    static func privateIPv4(_ text: String) -> Bool {
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 4, parts.allSatisfy({ !$0.isEmpty && $0.allSatisfy(\.isNumber) && UInt8($0) != nil }) else { return false }
        let p = parts.map { Int($0)! }
        return p[0] == 10 || p[0] == 192 && p[1] == 168 || p[0] == 172 && (16...31).contains(p[1]) || p[0] == 169 && p[1] == 254
    }
    static func qr(_ text: String, now: TimeInterval = Date().timeIntervalSince1970) throws -> Packet {
        guard text.utf8.count <= 12000, let data = text.data(using: .utf8) else { throw LanternError.message("QR quá lớn.") }
        let p = try decode(data)
        guard p.int("v") == 1, matches(p.string("pc"), "^[A-F0-9]{64}$"),
              matches(p.string("nonce"), "^[A-Za-z0-9_-]{43}$"), Double(p.int("expires")) > now,
              (1...65535).contains(p.int("port")), let addresses = p["addresses"] as? [String], addresses.count <= 16,
              addresses.allSatisfy(privateIPv4) else { throw LanternError.message("QR không hợp lệ hoặc đã hết hạn. Tạo mã mới trên PC.") }
        if let r = p["relay"] as? Packet {
            guard let url = URLComponents(string: r.string("url")), url.scheme == "https", url.host != nil,
                  url.user == nil, url.password == nil, url.query == nil, url.fragment == nil, url.path == "/",
                  matches(r.string("id"), "^[a-f0-9]{32}$"), matches(r.string("key"), "^[A-Za-z0-9_-]{43}$"),
                  !r.string("token").isEmpty, r.string("token").utf8.count <= 6000 else { throw LanternError.message("Cấu hình relay không hợp lệ.") }
        }
        return p
    }
    static func messageID(device: String, request: String) -> String { String(Data((device + request).utf8).sha256.prefix(32)).lowercased() }
    static func verification(pc: String, device: String) -> String {
        let ids = [pc, device].sorted()
        let chars = Array(Data("Lantern-pair-v1:\(ids[0]):\(ids[1])".utf8).sha256.prefix(24))
        return stride(from: 0, to: 24, by: 4).map { String(chars[$0..<$0+4]) }.joined(separator: " ")
    }
    static func date(_ value: String) -> Date? {
        let parser = ISO8601DateFormatter(); parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = parser.date(from: value) { return date }
        parser.formatOptions = [.withInternetDateTime]; return parser.date(from: value)
    }
}

struct Conversation: Identifiable {
    let raw: Packet
    var id: String { raw.string("id") }
    var name: String { raw.string("name") }
    var locked: Bool { raw.bool("locked") }
    var protected: Bool { raw.bool("protected") }
    var unread: Int { raw.int("unread") }
}
struct Message: Identifiable {
    var raw: Packet
    var id: String { raw.string("id") }
    var text: String { raw.string("text") }
    var kind: String { raw.string("kind").isEmpty ? "text" : raw.string("kind") }
    var outgoing: Bool { raw.bool("outgoing") }
    var pending: Bool { !raw.string("localRequest").isEmpty }
    var ephemeral: Bool { raw.int("disappearAfterSeconds") > 0 || Wire.date(raw.string("expiresAt")) != nil }
    var expired: Bool { Wire.date(raw.string("expiresAt")).map { $0 <= Date() } ?? false }
    static func joins(_ previous: Message, _ next: Message) -> Bool {
        guard previous.outgoing == next.outgoing, previous.raw.string("conversationId") == next.raw.string("conversationId"),
              previous.outgoing || (!previous.raw.string("senderId").isEmpty && previous.raw.string("senderId") == next.raw.string("senderId")),
              let a = Wire.date(previous.raw.string("time")), let b = Wire.date(next.raw.string("time")) else { return false }
        return b >= a && b.timeIntervalSince(a) <= 300 && Calendar.current.isDate(a, inSameDayAs: b)
    }
}

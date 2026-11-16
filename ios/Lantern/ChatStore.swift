import Foundation
import SwiftUI
import CryptoKit

@MainActor final class ChatStore: ObservableObject {
    @Published var conversations: [Conversation] = []
    @Published var messages: [Message] = []
    @Published var selected = ""
    @Published var status = "Chưa ghép đôi PC"
    @Published var error = ""
    @Published var paired = false
    @Published var connected = false
    @Published var pairingCode = ""
    @Published var pcName = ""
    @Published var transferProgress: Double?
    @Published var preview: URL?
    @Published var replyTo: Message?
    @Published var visible = true
    private var config: Packet?
    private var connection: PCConnection?
    private var cache: HistoryCache?
    private var snapshots: [String: [Packet]] = [:]
    private var hashes: [String: String] = [:]
    private var outbox: [Packet] = []
    private var shown: Set<String> = []
    private var downloaded: Set<String> = []
    private var loop: Task<Void, Never>?
    private var transfer: Task<Void, Never>?
    private var serial = 0
    private var generation = 0
    private var deviceID = ""
    private var inFlight: Set<String> = []
    private var syncInFlight = false
    private var previewMessage: Message?

    var current: Conversation? { conversations.first { $0.id == selected } }
    var locked: Bool { current?.locked ?? true }
    init() {
        clearTemporaryFiles()
        do {
            if let saved = try DeviceKeys.get("connection") {
                let value = try Wire.decode(saved); config = value; paired = true; pcName = value.string("name")
                deviceID = try DeviceKeys.id(); cache = try HistoryCache(pc: value.string("pc"))
                conversations = try cache!.read("contacts").map { Conversation(raw: $0) }
                outbox = try cache!.read("outbox").map { var p = $0; p["status"] = "retry"; return p }
            }
        } catch { self.error = "Không mở được dữ liệu đã lưu: \(error.localizedDescription)" }
    }
    func pair(_ text: String) {
        do {
            let candidate = try Wire.qr(text)
            guard !paired else { throw LanternError.message("Quên PC hiện tại trước khi ghép PC khác.") }
            stop(); config = candidate; deviceID = try DeviceKeys.id()
            pairingCode = Wire.verification(pc: candidate.string("pc"), device: deviceID)
            pcName = candidate.string("name"); error = ""; start()
        } catch { self.error = error.localizedDescription }
    }
    func start() {
        guard visible, config != nil, loop == nil else { return }
        generation += 1; let token = generation
        loop = Task {
            while !Task.isCancelled, visible, let value = config, generation == token {
                let pc = PCConnection(); connection = pc
                status = paired ? "Đang kết nối PC…" : "Đối chiếu mã và chấp nhận trên PC…"
                pc.onPush = { [weak self, weak pc] packet in
                    guard let self, let pc, self.connection === pc else { return }; self.apply(packet)
                }
                pc.onDisconnect = { [weak self, weak pc] in
                    guard let self, let pc, self.connection === pc else { return }; self.disconnected()
                }
                do {
                    _ = try await pc.open(value)
                    guard !Task.isCancelled, generation == token else { pc.close(); break }
                    var saved = value; saved["nonce"] = ""
                    try DeviceKeys.put("connection", JSONSerialization.data(withJSONObject: saved))
                    config = saved; if cache == nil { cache = try HistoryCache(pc: saved.string("pc")) }
                    paired = true; pairingCode = ""; connected = true; serial = 0; hashes.removeAll()
                    status = "Đã kết nối · " + pc.transport
                    while !Task.isCancelled, connected, generation == token {
                        await sync()
                        for _ in 0..<10 {
                            try await Task.sleep(nanoseconds: 1_000_000_000)
                            refresh()
                            if !connected { break }
                        }
                    }
                } catch {
                    if generation == token, !Task.isCancelled { status = "Chưa kết nối · " + error.localizedDescription }
                }
                pc.close()
                if !paired { config = nil; pairingCode = ""; break }
                if !Task.isCancelled { try? await Task.sleep(nanoseconds: 2_000_000_000) }
            }
            if generation == token { loop = nil }
        }
    }
    private func disconnected() {
        connected = false; serial = 0; hashes.removeAll(); shown.removeAll(); downloaded.removeAll()
        transfer?.cancel(); transfer = nil; transferProgress = nil; preview = nil; previewMessage = nil; clearTemporaryFiles()
        conversations = conversations.map { conversation in
            var p = conversation.raw; if conversation.protected { p["locked"] = true; snapshots.removeValue(forKey: conversation.id) }; return Conversation(raw: p)
        }
        // Protected/ephemeral pending data belongs only to the live session.
        outbox.removeAll { packet in Message(raw: packet).ephemeral || conversations.first(where: { $0.id == packet.string("conversationId") })?.protected != false }
        outbox = outbox.map { p in var p = p; if !p.string("localRequest").isEmpty { p["status"] = "retry" }; return p }
        replyTo = nil; refresh()
    }
    func stop() {
        generation += 1; loop?.cancel(); loop = nil
        connection?.close(); connection = nil; disconnected()
    }
    func setVisible(_ active: Bool) { visible = active; if active { start() } else { stop(); status = "Mở app để nối lại PC" } }
    func forget() {
        stop()
        do { try DeviceKeys.put("connection", nil); try HistoryCache.clear() }
        catch { self.error = "Chưa xóa hết dữ liệu: \(error.localizedDescription)"; return }
        config = nil; cache = nil; paired = false; pairingCode = ""; pcName = ""; selected = ""; conversations = []; messages = []; snapshots = [:]; outbox = []; status = "Chưa ghép đôi PC"
    }
    func select(_ id: String) {
        selected = id; shown.removeAll(); replyTo = nil
        if snapshots[id] == nil, current?.protected == false { snapshots[id] = try? cache?.read("history-" + id) }
        refresh(); Task { await sync() }
    }
    private func saveOutbox() throws {
        let saved = outbox.filter { packet in !packet.string("localRequest").isEmpty && conversations.first(where: { $0.id == packet.string("conversationId") })?.protected == false }
        try cache?.write("outbox", HistoryCache.persistable(saved))
    }
    private func apply(_ packet: Packet) {
        let next = packet.int("serial")
        guard next == 0 || next > serial else { return }; serial = max(serial, next)
        if packet["conversations"] != nil {
            conversations = packet.packets("conversations").map { Conversation(raw: $0) }
            for c in conversations where c.locked {
                snapshots.removeValue(forKey: c.id); outbox.removeAll { $0.string("conversationId") == c.id }
                if c.id == selected { replyTo = nil; preview = nil; previewMessage = nil; transfer?.cancel(); clearTemporaryFiles() }
            }
            do {
                for c in conversations where c.protected { try cache?.remove("history-" + c.id) }
                try cache?.write("contacts", conversations.map { c in var p = c.raw; if c.protected { p["locked"] = true }; return p })
                try saveOutbox()
            } catch { self.error = "Không lưu được cache: \(error.localizedDescription)" }
        }
        let id = packet.string("conversation")
        if !id.isEmpty, !packet.bool("locked"), packet["messages"] != nil, packet.string("type") != "unchanged" {
            let batch = packet.packets("messages")
            snapshots[id] = batch; hashes[id] = packet.string("hash")
            let ids = Set(batch.map { $0.string("id") }); outbox.removeAll { ids.contains($0.string("id")) }
            do {
                if conversations.first(where: { $0.id == id })?.protected == false { try cache?.write("history-" + id, HistoryCache.persistable(batch)) }
                try saveOutbox()
            } catch { self.error = "Không lưu được lịch sử: \(error.localizedDescription)" }
        }
        refresh()
    }
    private func refresh() {
        if previewMessage?.expired == true { preview = nil; previewMessage = nil; clearTemporaryFiles() }
        guard visible, !selected.isEmpty, !locked else { messages = []; return }
        let remote = (snapshots[selected] ?? []).filter { !Message(raw: $0).expired }
        let ids = Set(remote.map { $0.string("id") })
        messages = (remote + outbox.filter { $0.string("conversationId") == selected && !ids.contains($0.string("id")) }).map { Message(raw: $0) }.filter { !$0.expired }
    }
    func displayed(_ message: Message, _ active: Bool) {
        if active { shown.insert(message.id) } else { shown.remove(message.id) }
    }
    func sync() async {
        guard connected, visible, !syncInFlight, let pc = connection else { return }
        syncInFlight = true; defer { syncInFlight = false }
        let id = selected
        let read = messages.filter { shown.contains($0.id) && !$0.pending && ($0.kind != "image" || downloaded.contains($0.id)) }.map(\.id)
        do {
            let response = try await pc.request(["type": "sync", "conversation": id, "hash": hashes[id] ?? "", "visibleIds": read])
            guard connection === pc else { return }; apply(response)
        } catch { if connection === pc { status = error.localizedDescription } }
    }
    func unlock(_ password: String) async {
        guard let pc = connection, connected else { return }; let id = selected
        do {
            let response = try await pc.request(["type": "unlock", "conversation": id, "password": password])
            guard connection === pc, selected == id else { return }
            if response.int("serial") >= serial {
                conversations = conversations.map { c in var p = c.raw; if c.id == id { p["locked"] = false }; return Conversation(raw: p) }; apply(response)
            }
            await sync()
        } catch { self.error = error.localizedDescription }
    }
    func lock() async {
        let id = selected
        conversations = conversations.map { c in var p = c.raw; if c.id == id { p["locked"] = true }; return Conversation(raw: p) }
        snapshots.removeValue(forKey: id); outbox.removeAll { $0.string("conversationId") == id }; replyTo = nil; preview = nil; previewMessage = nil; clearTemporaryFiles(); refresh()
        if transfer != nil { cancelTransfer() }
        do { if let result = try await connection?.request(["type": "lock", "conversation": id]) { serial = max(serial, result.int("serial")) }; try saveOutbox() }
        catch { self.error = error.localizedDescription }
    }
    func send(_ text: String) -> Bool {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, text.utf16.count <= 16000, !locked, outbox.count < 100 else { error = "Hãy mở khóa hội thoại; tin nhắn tối đa 16.000 ký tự và hàng đợi tối đa 100 tin."; return false }
        let request = Wire.id(), id = Wire.messageID(device: deviceID, request: request)
        let p: Packet = ["id": id, "localRequest": request, "conversationId": selected, "text": text, "kind": "text", "outgoing": true,
                         "time": ISO8601DateFormatter().string(from: Date()), "status": "sending", "replyToId": replyTo?.id ?? "", "disappearAfterSeconds": current?.raw.int("disappearSeconds") ?? 0]
        outbox.append(p)
        do { try saveOutbox() } catch { outbox.removeAll { $0.string("id") == id }; self.error = "Không lưu được tin; nội dung vẫn trong ô nhập."; return false }
        replyTo = nil; refresh(); Task { await retry(id) }; return true
    }
    func retry(_ id: String) async {
        guard let pending = outbox.first(where: { $0.string("id") == id }), !pending.string("localRequest").isEmpty, !inFlight.contains(id) else { return }
        guard connected, let pc = connection, conversations.first(where: { $0.id == pending.string("conversationId") })?.locked == false else { markFailed(id); return }
        inFlight.insert(id); defer { inFlight.remove(id) }
        do {
            let result = try await pc.request(["type": "send", "conversation": pending.string("conversationId"), "id": pending.string("localRequest"), "text": pending.string("text"), "replyTo": pending.string("replyToId")])
            guard connection === pc else { return }
            if result.string("type") == "gone" { outbox.removeAll { $0.string("id") == id } }
            else if let index = outbox.firstIndex(where: { $0.string("id") == id }) {
                if let message = result.packets("messages").first { outbox[index] = message }
                else { outbox[index].removeValue(forKey: "localRequest"); outbox[index]["status"] = "sent" }
            }
            try saveOutbox(); refresh(); await sync()
        } catch { markFailed(id); self.error = error.localizedDescription }
    }
    private func markFailed(_ id: String) { if let index = outbox.firstIndex(where: { $0.string("id") == id }) { outbox[index]["status"] = "retry" }; try? saveOutbox(); refresh() }
    func react(_ message: Message, emoji: String) async {
        guard !locked, let pc = connection else { return }
        do { _ = try await pc.request(["type": "react", "conversation": selected, "messageId": message.id, "emoji": emoji]); await sync() }
        catch { self.error = error.localizedDescription }
    }
    func upload(_ url: URL) {
        guard transfer == nil, connected, !locked, let pc = connection else { return }
        let conversation = selected
        transferProgress = 0
        transfer = Task {
            let access = url.startAccessingSecurityScopedResource(); defer { if access { url.stopAccessingSecurityScopedResource() }; transfer = nil; transferProgress = nil }
            do {
                let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
                guard size <= Wire.maxFile else { throw LanternError.message("Tệp tối đa 100 MB.") }
                let handle = try FileHandle(forReadingFrom: url); defer { try? handle.close() }
                var hash = SHA256(); var actual = 0
                while let chunk = try handle.read(upToCount: 32768), !chunk.isEmpty { try Task.checkCancellation(); actual += chunk.count; guard actual <= Wire.maxFile else { throw LanternError.message("Tệp vượt 100 MB.") }; hash.update(data: chunk) }
                let checksum = Data(hash.finalize()).hex; try handle.seek(toOffset: 0)
                _ = try await pc.request(["type": "upload-start", "id": Wire.id(), "conversation": conversation, "size": actual, "hash": checksum, "fileName": url.lastPathComponent])
                var offset = 0
                while let chunk = try handle.read(upToCount: 32768), !chunk.isEmpty {
                    try Task.checkCancellation(); guard connection === pc, conversations.first(where: { $0.id == conversation })?.locked == false else { throw CancellationError() }
                    _ = try await pc.request(["type": "upload-chunk", "conversation": conversation, "offset": offset, "data": chunk.base64EncodedString()]); offset += chunk.count; transferProgress = Double(offset) / Double(max(actual, 1))
                }
                try Task.checkCancellation()
                _ = try await pc.request(["type": "upload-finish", "conversation": conversation, "fileName": url.lastPathComponent]); await sync()
            } catch { if !Task.isCancelled { self.error = error.localizedDescription } }
        }
    }
    func download(_ message: Message) {
        guard transfer == nil, !locked, connected, let pc = connection else { return }
        let conversation = selected; transferProgress = 0
        transfer = Task {
            defer { transfer = nil; transferProgress = nil }
            var output: URL?
            do {
                let size = message.raw.int("fileSize")
                guard (0...Wire.maxFile).contains(size), Wire.matches(message.raw.string("hash"), "^[A-Fa-f0-9]{64}$") else { throw LanternError.message("Tệp không hợp lệ hoặc vượt 100 MB.") }
                _ = try await pc.request(["type": "accept", "conversation": conversation, "messageId": message.id])
                let folder = Self.tempFolder.appendingPathComponent(Wire.id(), isDirectory: true)
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let name = URL(fileURLWithPath: message.raw.string("fileName")).lastPathComponent
                let file = folder.appendingPathComponent(name.isEmpty || name == "." || name == ".." ? "attachment" : name)
                output = file; try Data().write(to: file, options: .completeFileProtection)
                let handle = try FileHandle(forWritingTo: file); defer { try? handle.close() }
                var offset = 0, hash = SHA256()
                repeat {
                    try Task.checkCancellation()
                    guard connection === pc, selected == conversation, !locked else { throw CancellationError() }
                    let response = try await pc.request(["type": "download", "conversation": conversation, "messageId": message.id, "offset": offset])
                    guard response.int("offset") == offset, response.int("size") == size,
                          response.string("hash").uppercased() == message.raw.string("hash").uppercased(), let chunk = Data(base64Encoded: response.string("data")), chunk.count <= 32768,
                          offset + chunk.count <= size, !chunk.isEmpty || size == 0 else { throw LanternError.message("Khối tệp không hợp lệ.") }
                    try handle.write(contentsOf: chunk); hash.update(data: chunk); offset += chunk.count; transferProgress = Double(offset) / Double(max(size, 1))
                } while offset < size
                guard Data(hash.finalize()).hex == message.raw.string("hash").uppercased() else { throw LanternError.message("Checksum tệp không đúng.") }
                guard visible, !locked, selected == conversation, !message.expired else { throw CancellationError() }
                try Task.checkCancellation()
                downloaded.insert(message.id); previewMessage = message; preview = file; await sync()
            } catch { if let output { try? FileManager.default.removeItem(at: output) }; if !Task.isCancelled { self.error = error.localizedDescription } }
        }
    }
    func cancelTransfer() { transfer?.cancel(); connection?.close() }
    static var tempFolder: URL { FileManager.default.temporaryDirectory.appendingPathComponent("LanternMedia", isDirectory: true) }
    private func clearTemporaryFiles() { try? FileManager.default.removeItem(at: Self.tempFolder) }
}

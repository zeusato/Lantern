import Foundation
import Network
import Security

private final class CompletionGate: @unchecked Sendable {
    private let lock = NSLock()
    private var finished = false
    func claim() -> Bool { lock.lock(); defer { lock.unlock() }; if finished { return false }; finished = true; return true }
}

// Reject redirects: relay credentials must never follow a Location header to another host.
class RelayDelegate: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, didReceive challenge: URLAuthenticationChallenge,
                    completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        completionHandler(.performDefaultHandling, nil)
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
}

@MainActor final class RelayBridge {
    private let config: Packet
    private let sessionID = Wire.id()
    private let http: URLSession
    private let delegate: RelayDelegate
    private var listener: NWListener?
    private var peer: NWConnection?
    private var jobs: [Task<Void, Never>] = []
    private var received = 0
    private var stopped = false
    var onFailure: (() -> Void)?
    init(_ config: Packet, delegate: RelayDelegate = RelayDelegate()) {
        self.config = config
        self.delegate = delegate
        let settings = URLSessionConfiguration.ephemeral
        settings.timeoutIntervalForRequest = 20; settings.timeoutIntervalForResource = 25
        settings.httpCookieStorage = nil; settings.urlCache = nil
        http = URLSession(configuration: settings, delegate: delegate, delegateQueue: nil)
    }
    private func call(_ action: String, _ body: Packet) async throws -> Packet {
        guard !stopped, let url = URL(string: config.string("url") + "v1/routes/" + config.string("id") + "/" + action) else { throw CancellationError() }
        var request = URLRequest(url: url); request.httpMethod = "POST"
        request.setValue("Bearer " + config.string("key"), forHTTPHeaderField: "Authorization")
        request.setValue("Bearer " + config.string("token"), forHTTPHeaderField: "OAI-Sites-Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        let (stream, response) = try await http.bytes(for: request, delegate: delegate)
        guard let response = response as? HTTPURLResponse else { throw LanternError.message("Relay không phản hồi.") }
        guard response.statusCode == 200 else { throw RelayError(status: response.statusCode) }
        var data = Data()
        for try await byte in stream {
            guard data.count < 275000 else { throw LanternError.message("Gói relay quá lớn.") }
            data.append(byte)
        }
        return try Wire.decode(data)
    }
    private struct RelayError: Error { let status: Int }
    func start() async throws -> UInt16 {
        _ = try await call("open", ["session": sessionID])
        let parameters = NWParameters.tcp
        parameters.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: .any)
        let server = try NWListener(using: parameters); listener = server
        server.newConnectionHandler = { [weak self] connection in
            Task { @MainActor in
                guard let self, !self.stopped, self.peer == nil else { connection.cancel(); return }
                self.peer = connection; self.listener?.cancel(); self.listener = nil
                connection.start(queue: .main)
                self.jobs.append(Task { await self.sendLoop(connection) })
                self.jobs.append(Task { await self.receiveLoop(connection) })
            }
        }
        return try await withCheckedThrowingContinuation { continuation in
            let gate = CompletionGate()
            server.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    if gate.claim() { continuation.resume(returning: server.port!.rawValue) }
                case .failed(let error): if gate.claim() { continuation.resume(throwing: error) }
                case .cancelled: if gate.claim() { continuation.resume(throwing: CancellationError()) }
                default: break
                }
            }
            server.start(queue: .main)
        }
    }
    private func exchange(_ body: Packet) async throws -> Packet {
        let start = Date()
        while true {
            do { return try await call("exchange", body.merging(["after": received]) { _, b in b }) }
            catch {
                if stopped || Task.isCancelled { throw CancellationError() }
                if let failure = error as? RelayError, failure.status != 429 { throw error }
                guard Date().timeIntervalSince(start) < 45 else { throw error }
                try await Task.sleep(nanoseconds: 300_000_000)
            }
        }
    }
    private func sendLoop(_ connection: NWConnection) async {
        do {
            var sequence = 0
            while !stopped {
                let data = try await SocketIO.receive(connection, minimum: 1, maximum: 32768)
                sequence += 1
                _ = try await exchange(["session": sessionID, "seq": sequence, "pushOnly": true, "data": data.base64EncodedString()])
            }
        } catch { fail() }
    }
    private func receiveLoop(_ connection: NWConnection) async {
        do {
            while !stopped {
                let response = try await exchange(["session": sessionID, "batch": 4])
                let packets = response.packets("packets")
                for packet in packets {
                    guard packet.int("seq") == received + 1, let data = Data(base64Encoded: packet.string("data")), !data.isEmpty, data.count <= 49152 else { throw LanternError.message("Sai thứ tự gói relay.") }
                    try await SocketIO.send(connection, data); received += 1
                }
                if packets.isEmpty { try await Task.sleep(nanoseconds: 50_000_000) }
            }
        } catch { fail() }
    }
    private func fail() { guard !stopped else { return }; let callback = onFailure; close(); callback?() }
    func close() {
        stopped = true; jobs.forEach { $0.cancel() }; jobs.removeAll()
        listener?.cancel(); listener = nil; peer?.cancel(); peer = nil; http.invalidateAndCancel()
    }
}

enum SocketIO {
    static func send(_ connection: NWConnection, _ bytes: Data) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            connection.send(content: bytes, completion: .contentProcessed { error in
                if let error { continuation.resume(throwing: error) } else { continuation.resume() }
            })
        }
    }
    static func receive(_ connection: NWConnection, minimum: Int, maximum: Int) async throws -> Data {
        try await withCheckedThrowingContinuation { continuation in
            connection.receive(minimumIncompleteLength: minimum, maximumLength: maximum) { data, _, _, error in
                if let error { continuation.resume(throwing: error) }
                else if let data, !data.isEmpty { continuation.resume(returning: data) }
                else { continuation.resume(throwing: LanternError.message("PC đã đóng kết nối.")) }
            }
        }
    }
    static func exact(_ connection: NWConnection, _ count: Int) async throws -> Data {
        var data = Data()
        while data.count < count { data.append(try await receive(connection, minimum: 1, maximum: count - data.count)) }
        return data
    }
    static func packet(_ connection: NWConnection) async throws -> Packet {
        let header = try await exact(connection, 4)
        return try Wire.decode(await exact(connection, Wire.length(header)))
    }
}

@MainActor final class PCConnection {
    private var socket: NWConnection?
    private var relay: RelayBridge?
    private var reader: Task<Void, Never>?
    private var pending: [String: CheckedContinuation<Packet, Error>] = [:]
    private var deadlines: [String: Task<Void, Never>] = [:]
    private var stopped = false
    var onPush: ((Packet) -> Void)?
    var onDisconnect: (() -> Void)?
    private(set) var transport = "Wi-Fi/LAN"
    #if DEBUG
    // Tests supply their ephemeral CA without changing the simulator's global trust store.
    var relayDelegate = RelayDelegate()
    #endif

    private func connect(host: String, port: UInt16, pin: String, seconds: UInt64) async throws -> NWConnection {
        let tls = NWProtocolTLS.Options()
        sec_protocol_options_set_min_tls_protocol_version(tls.securityProtocolOptions, .TLSv12)
        guard let identity = sec_identity_create(try DeviceKeys.identity()) else { throw LanternError.message("Không tạo được danh tính TLS.") }
        sec_protocol_options_set_local_identity(tls.securityProtocolOptions, identity)
        sec_protocol_options_set_verify_block(tls.securityProtocolOptions, { _, trust, complete in
            let value = sec_trust_copy_ref(trust).takeRetainedValue()
            guard let chain = SecTrustCopyCertificateChain(value) as? [SecCertificate], let certificate = chain.first,
                  let fingerprint = try? DeviceKeys.fingerprint(certificate) else { complete(false); return }
            complete(fingerprint == pin)
        }, .global(qos: .userInitiated))
        let tcp = NWProtocolTCP.Options(); tcp.noDelay = true
        let connection = NWConnection(host: NWEndpoint.Host(host), port: NWEndpoint.Port(rawValue: port)!, using: NWParameters(tls: tls, tcp: tcp))
        socket = connection
        let timeout = Task { try? await Task.sleep(nanoseconds: seconds * 1_000_000_000); if !Task.isCancelled { connection.cancel() } }
        defer { timeout.cancel() }
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            let gate = CompletionGate()
            connection.stateUpdateHandler = { state in
                switch state {
                case .ready: if gate.claim() { continuation.resume() }
                case .failed(let error): if gate.claim() { continuation.resume(throwing: error) }
                case .cancelled: if gate.claim() { continuation.resume(throwing: LanternError.message("Không tới được PC hoặc kết nối quá chậm.")) }
                default: break
                }
            }
            connection.start(queue: .main)
        }
        return connection
    }
    func open(_ config: Packet) async throws -> Packet {
        var active: NWConnection?
        for address in config["addresses"] as? [String] ?? [] where Wire.privateIPv4(address) {
            guard !stopped else { throw CancellationError() }
            do { active = try await connect(host: address, port: UInt16(config.int("port")), pin: config.string("pc"), seconds: 4); break }
            catch { socket?.cancel(); socket = nil }
        }
        // Used exclusively by simulator integration tests; shipping builds accept only private IPv4 from QR.
        #if DEBUG
        if config.bool("fixture") { active = try await connect(host: "127.0.0.1", port: UInt16(config.int("port")), pin: config.string("pc"), seconds: 5) }
        #endif
        if active == nil {
            guard !stopped, let configRelay = config["relay"] as? Packet else { throw LanternError.message("Chưa tới được PC trong Wi-Fi. Kiểm tra PC và quyền Mạng cục bộ.") }
            #if DEBUG
            let bridge = RelayBridge(configRelay, delegate: relayDelegate)
            #else
            let bridge = RelayBridge(configRelay)
            #endif
            relay = bridge
            bridge.onFailure = { [weak self] in self?.close() }
            let port = try await bridge.start()
            active = try await connect(host: "127.0.0.1", port: port, pin: config.string("pc"), seconds: 45); transport = "Internet"
        }
        guard !stopped, let active else { throw CancellationError() }
        socket = active
        let timeout = Task { try? await Task.sleep(nanoseconds: 110_000_000_000); if !Task.isCancelled { active.cancel() } }
        defer { timeout.cancel() }
        do {
            try await SocketIO.send(active, Wire.frame(["type": "hello", "nonce": config.string("nonce"), "name": "Lantern iPhone", "capabilities": ["push-v1"]]))
            let welcome = try await SocketIO.packet(active)
            guard welcome.string("type") == "welcome", welcome.string("id") == config.string("pc"),
                  (welcome["capabilities"] as? [String] ?? []).contains("push-v1") else { throw LanternError.message("PC chưa chấp nhận hoặc cần cập nhật Lantern trên PC.") }
            reader = Task { [weak self] in
                do {
                    while !Task.isCancelled {
                        let packet = try await SocketIO.packet(active)
                        guard let self, !self.stopped else { return }
                        let id = packet.string("id")
                        if id.isEmpty { self.onPush?(packet) }
                        else if let continuation = self.pending.removeValue(forKey: id) {
                            self.deadlines.removeValue(forKey: id)?.cancel(); continuation.resume(returning: packet)
                        }
                    }
                } catch { self?.close() }
            }
            return welcome
        } catch { close(); throw error }
    }
    func request(_ packet: Packet) async throws -> Packet {
        guard !stopped, let socket else { throw LanternError.message("Đang mất kết nối PC.") }
        var packet = packet
        if packet.string("id").isEmpty { packet["id"] = Wire.id() }
        let id = packet.string("id"), bytes = try Wire.frame(packet)
        guard pending[id] == nil, pending.count < 32 else { throw LanternError.message("Đang xử lý quá nhiều lệnh.") }
        let response: Packet = try await withCheckedThrowingContinuation { continuation in
            pending[id] = continuation
            deadlines[id] = Task { [weak self] in
                try? await Task.sleep(nanoseconds: 35_000_000_000)
                guard !Task.isCancelled, let self else { return }
                self.close()
            }
            socket.send(content: bytes, completion: .contentProcessed { [weak self] error in
                if error != nil { Task { @MainActor in self?.close() } }
            })
        }
        if response.string("type") == "error" { throw LanternError.message(response.string("text")) }
        return response
    }
    func close() {
        guard !stopped else { return }; stopped = true
        socket?.cancel(); socket = nil; relay?.close(); relay = nil; reader?.cancel(); reader = nil
        deadlines.values.forEach { $0.cancel() }; deadlines.removeAll()
        let waiting = pending; pending.removeAll()
        waiting.values.forEach { $0.resume(throwing: LanternError.message("Kết nối gián đoạn. Có thể thử lại với cùng mã gửi.")) }
        onDisconnect?(); onDisconnect = nil; onPush = nil
    }
}

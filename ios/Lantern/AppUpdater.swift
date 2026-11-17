import Foundation
import CryptoKit
import SwiftUI
import UIKit

struct UpdateFailure: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}

struct AppRelease: Codable {
    static let asset = "Lantern-iOS-unsigned.ipa"
    static let sums = "IOS-SHA256SUMS.txt"
    static let api = URL(string: "https://api.github.com/repos/zeusato/Lantern/releases/latest")!
    let version: String
    let url: URL
    let sumsURL: URL
    let size: Int64
    let digest: String

    static func versionCode(_ version: String) throws -> Int {
        guard version.range(of: "^[0-9]{1,3}\\.[0-9]{1,2}\\.[0-9]{1,2}$", options: .regularExpression) != nil else { throw UpdateFailure("Phiên bản cập nhật không hợp lệ.") }
        let parts = version.split(separator: ".").compactMap { Int($0) }
        return parts[0] * 10000 + parts[1] * 100 + parts[2]
    }
    static func parse(_ data: Data) throws -> AppRelease {
        guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              json["draft"] as? Bool == false, json["prerelease"] as? Bool == false,
              let tag = json["tag_name"] as? String, tag.hasPrefix("v"),
              let assets = json["assets"] as? [[String: Any]] else { throw UpdateFailure("Chỉ nhận bản phát hành ổn định.") }
        let version = String(tag.dropFirst()); _ = try versionCode(version)
        let apps = assets.filter { $0["name"] as? String == asset }, checksums = assets.filter { $0["name"] as? String == sums }
        guard apps.count == 1, checksums.count == 1 else { throw UpdateFailure("Bản phát hành chưa có đủ IPA và checksum.") }
        let base = "https://github.com/zeusato/Lantern/releases/download/\(tag)/"
        guard let address = apps[0]["browser_download_url"] as? String, address == base + asset,
              let sumsAddress = checksums[0]["browser_download_url"] as? String, sumsAddress == base + sums,
              let size = apps[0]["size"] as? Int64, size > 0, size <= 100 * 1024 * 1024,
              let url = URL(string: address), let sumsURL = URL(string: sumsAddress) else { throw UpdateFailure("Nguồn hoặc kích thước cập nhật không hợp lệ.") }
        let digest = (apps[0]["digest"] as? String ?? "").lowercased()
        guard digest.isEmpty || digest.range(of: "^sha256:[a-f0-9]{64}$", options: .regularExpression) != nil else { throw UpdateFailure("Checksum GitHub không hợp lệ.") }
        return AppRelease(version: version, url: url, sumsURL: sumsURL, size: size, digest: digest)
    }
    static func checksum(_ data: Data) throws -> String {
        let entries = String(decoding: data, as: UTF8.self).split(whereSeparator: \.isNewline).map { $0.split(whereSeparator: \.isWhitespace).map(String.init) }.filter { $0.count == 2 && $0[1].replacingOccurrences(of: "*", with: "") == asset }
        guard entries.count == 1, let sha = entries.first?.first?.lowercased(), sha.range(of: "^[a-f0-9]{64}$", options: .regularExpression) != nil else { throw UpdateFailure("Thiếu hoặc sai checksum cho IPA.") }
        return sha
    }
    static func allowed(_ url: URL) -> Bool {
        url.scheme == "https" && url.user == nil && url.password == nil && (url.port == nil || url.port == 443)
            && ["api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com", "github-releases.githubusercontent.com"].contains(url.host?.lowercased() ?? "")
    }
    func verify(_ file: URL, sha: String) throws {
        guard sha.range(of: "^[a-f0-9]{64}$", options: .regularExpression) != nil,
              digest.isEmpty || digest == "sha256:" + sha,
              (try FileManager.default.attributesOfItem(atPath: file.path)[.size] as? NSNumber)?.int64Value == size else { throw UpdateFailure("Kích thước hoặc checksum IPA không khớp.") }
        let handle = try FileHandle(forReadingFrom: file); defer { try? handle.close() }
        var hasher = SHA256()
        while let data = try handle.read(upToCount: 65536), !data.isEmpty { hasher.update(data: data) }
        guard hasher.finalize().map({ String(format: "%02x", $0) }).joined() == sha else { throw UpdateFailure("IPA tải về bị lỗi. Hãy tải lại.") }
    }
}

/// Dedicated bounded HTTPS session, with no cookies, credentials or chat requests.
private final class UpdateDownload: NSObject, URLSessionDownloadDelegate, @unchecked Sendable {
    private let limit: Int64
    private let progress: (Int) -> Void
    private var continuation: CheckedContinuation<URL, Error>?
    private var output: URL?
    private var failure: Error?
    private var previous = -1
    private init(limit: Int64, progress: @escaping (Int) -> Void) { self.limit = limit; self.progress = progress }
    static func fetch(_ url: URL, limit: Int64, progress: @escaping (Int) -> Void = { _ in }) async throws -> URL {
        guard AppRelease.allowed(url) else { throw UpdateFailure("Đường tải không được phép.") }
        let delegate = UpdateDownload(limit: limit, progress: progress)
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 20; config.timeoutIntervalForResource = 300
        config.httpAdditionalHeaders = ["User-Agent": "Lantern-iOS-Updater", "Accept-Encoding": "identity"]
        let session = URLSession(configuration: config, delegate: delegate, delegateQueue: nil)
        return try await withCheckedThrowingContinuation { continuation in
            delegate.continuation = continuation; session.downloadTask(with: url).resume()
        }
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        guard let url = request.url, AppRelease.allowed(url) else { failure = UpdateFailure("Chuyển hướng tải không được phép."); completionHandler(nil); task.cancel(); return }
        completionHandler(request)
    }
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64, totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        guard totalBytesWritten <= limit, totalBytesExpectedToWrite <= limit else { failure = UpdateFailure("Dữ liệu tải vượt giới hạn."); downloadTask.cancel(); return }
        let p = totalBytesExpectedToWrite > 0 ? Int(totalBytesWritten * 100 / totalBytesExpectedToWrite) : 0
        if p != previous { previous = p; progress(p) }
    }
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        do {
            guard let response = downloadTask.response as? HTTPURLResponse, response.statusCode == 200 else { throw UpdateFailure("Không tải được bản cập nhật. Hãy kiểm tra Internet hoặc thử lại sau.") }
            let size = (try FileManager.default.attributesOfItem(atPath: location.path)[.size] as? NSNumber)?.int64Value ?? Int64.max
            guard size <= limit else { throw UpdateFailure("Dữ liệu tải vượt giới hạn.") }
            let target = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
            try FileManager.default.moveItem(at: location, to: target); output = target
        } catch { failure = error }
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        defer { continuation = nil; session.finishTasksAndInvalidate() }
        if let error = failure ?? error { if let output { try? FileManager.default.removeItem(at: output) }; continuation?.resume(throwing: error) }
        else if let output { continuation?.resume(returning: output) }
        else { continuation?.resume(throwing: UpdateFailure("Không nhận được tệp cập nhật.")) }
    }
}

@MainActor final class AppUpdater: ObservableObject {
    @Published var status = "Chưa kiểm tra cập nhật."
    @Published var busy = false
    @Published var release: AppRelease?
    @Published var file: URL?
    @Published var shareFile: SharedIPA?
    @Published var autoCheck: Bool { didSet { defaults.set(autoCheck, forKey: "updates.autoCheck") } }
    @Published var autoDownload: Bool { didSet { defaults.set(autoDownload, forKey: "updates.autoDownload") } }
    let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.0.0"
    private let defaults = UserDefaults.standard
    private var sha = ""
    private var restored = false
    private let directory = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0].appendingPathComponent("Updates", isDirectory: true)
    init() {
        autoCheck = UserDefaults.standard.object(forKey: "updates.autoCheck") as? Bool ?? true
        autoDownload = UserDefaults.standard.object(forKey: "updates.autoDownload") as? Bool ?? true
    }
    func automatic() {
        #if DEBUG
        if ProcessInfo.processInfo.environment["LANTERN_UI_FIXTURE_PIN"] != nil { return }
        #endif
        if autoCheck { Task { await check(force: false) } }
    }
    private func metadata(_ url: URL, limit: Int64) async throws -> Data {
        let downloaded = try await UpdateDownload.fetch(url, limit: limit)
        defer { try? FileManager.default.removeItem(at: downloaded) }
        return try Data(contentsOf: downloaded)
    }
    func check(force: Bool) async {
        guard !busy else { return }
        let due = force || Date().timeIntervalSince1970 - defaults.double(forKey: "updates.lastAttempt") > 6 * 3600
        guard due || !restored else { return }
        busy = true; status = "Đang kiểm tra bản mới…"
        do {
            if !restored {
                restored = true
                if let json = defaults.data(forKey: "updates.release"), let cached = try? AppRelease.parse(json),
                   try AppRelease.versionCode(cached.version) > AppRelease.versionCode(version) {
                    let hash = defaults.string(forKey: "updates.sha") ?? "", local = directory.appendingPathComponent(AppRelease.asset)
                    if (try? await Task.detached { try cached.verify(local, sha: hash) }.value) != nil { release = cached; sha = hash; file = local }
                } else { try? FileManager.default.removeItem(at: directory.appendingPathComponent(AppRelease.asset)) }
            }
            if due {
                defaults.set(Date().timeIntervalSince1970, forKey: "updates.lastAttempt")
                let json = try await metadata(AppRelease.api, limit: 2 * 1024 * 1024), next = try AppRelease.parse(json)
                if try AppRelease.versionCode(next.version) > AppRelease.versionCode(version) {
                    let hash = try AppRelease.checksum(await metadata(next.sumsURL, limit: 16384))
                    guard next.digest.isEmpty || next.digest == "sha256:" + hash else { throw UpdateFailure("Hai checksum của bản phát hành không khớp.") }
                    if next.version != release?.version || hash != sha { file = nil }
                    release = next; sha = hash; defaults.set(json, forKey: "updates.release"); defaults.set(hash, forKey: "updates.sha")
                } else {
                    release = nil; file = nil; defaults.removeObject(forKey: "updates.release"); defaults.removeObject(forKey: "updates.sha")
                    try? FileManager.default.removeItem(at: directory.appendingPathComponent(AppRelease.asset))
                }
            }
            status = release.map { "Có phiên bản \($0.version)." } ?? "Đang dùng phiên bản mới nhất."
            if file != nil { status = "Đã tải IPA · Sẵn sàng chuyển sang Scarlet." }
        } catch { status = error.localizedDescription }
        busy = false
        if release != nil && file == nil && autoDownload { await download() }
    }
    func download() async {
        guard !busy, let release else { return }
        busy = true; file = nil; status = "Đang tải IPA \(release.version)…"
        do {
            let downloaded = try await UpdateDownload.fetch(release.url, limit: release.size) { [weak self] p in
                Task { @MainActor in self?.status = "Đang tải IPA \(release.version) · \(p)%" }
            }
            defer { try? FileManager.default.removeItem(at: downloaded) }
            let hash = sha
            try await Task.detached { try release.verify(downloaded, sha: hash) }.value
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let target = directory.appendingPathComponent(AppRelease.asset)
            if FileManager.default.fileExists(atPath: target.path) { try FileManager.default.removeItem(at: target) }
            try FileManager.default.moveItem(at: downloaded, to: target)
            file = target; status = "Đã tải IPA · Sẵn sàng chuyển sang Scarlet."
        } catch { status = error.localizedDescription }
        busy = false
    }
    func share() async {
        guard !busy, let release, let file else { return }
        busy = true; status = "Đang xác minh IPA…"
        do {
            let hash = sha; try await Task.detached { try release.verify(file, sha: hash) }.value
            shareFile = SharedIPA(url: file); status = "Chọn Scarlet hoặc Lưu vào Tệp để nhập trong Scarlet."
        } catch { self.file = nil; status = error.localizedDescription }
        busy = false
    }
}

struct SharedIPA: Identifiable { let id = UUID(); let url: URL }
struct ShareIPAView: UIViewControllerRepresentable {
    let url: URL
    func makeUIViewController(context: Context) -> UIActivityViewController { UIActivityViewController(activityItems: [url], applicationActivities: nil) }
    func updateUIViewController(_ controller: UIActivityViewController, context: Context) {}
}

struct UpdatesSection: View {
    @EnvironmentObject private var updater: AppUpdater
    var body: some View {
        Section("Cập nhật ứng dụng") {
            LabeledContent("Đang dùng", value: updater.version)
            Toggle("Tự kiểm tra bản mới", isOn: $updater.autoCheck).onChange(of: updater.autoCheck) { if $0 { updater.automatic() } }
            Toggle("Tự tải bản cập nhật", isOn: $updater.autoDownload).onChange(of: updater.autoDownload) { if $0 && updater.file == nil { Task { await updater.download() } } }
            Text("Kiểm tra mỗi 6 giờ khi mở app. Tải bằng kết nối Internet hiện tại, kể cả dữ liệu di động.").font(.footnote).foregroundStyle(.secondary)
            if updater.busy { ProgressView(updater.status) } else { Text(updater.status).font(.callout) }
            Button("Kiểm tra ngay") { Task { await updater.check(force: true) } }.disabled(updater.busy)
            if let release = updater.release {
                if updater.file == nil { Button("Tải IPA \(release.version)") { Task { await updater.download() } }.disabled(updater.busy) }
                else { Button("Chia sẻ IPA / Mở bằng Scarlet") { Task { await updater.share() } }.disabled(updater.busy) }
            }
            Text("Trong bảng chia sẻ, chọn Scarlet nếu có; nếu không, Lưu vào Tệp rồi mở Scarlet → nhập IPA để ký/cài. Cần chứng chỉ còn hiệu lực. Giữ nguyên mã ứng dụng và danh tính ký để giữ dữ liệu ghép đôi.").font(.footnote).foregroundStyle(.secondary)
            Link("Xem bản phát hành", destination: URL(string: "https://github.com/zeusato/Lantern/releases/latest")!)
        }
    }
}

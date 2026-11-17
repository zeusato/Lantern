import XCTest
import CryptoKit
@testable import Lantern

final class UpdateTests: XCTestCase {
    private func metadata() -> [String: Any] {
        let base = "https://github.com/zeusato/Lantern/releases/download/v9.9.9/"
        return ["draft": false, "prerelease": false, "tag_name": "v9.9.9", "assets": [
            ["name": AppRelease.asset, "browser_download_url": base + AppRelease.asset, "size": 3],
            ["name": AppRelease.sums, "browser_download_url": base + AppRelease.sums]
        ]]
    }
    private func parse(_ json: [String: Any]) throws -> AppRelease { try AppRelease.parse(JSONSerialization.data(withJSONObject: json)) }
    func testStableReleaseAndVersionOrdering() throws {
        XCTAssertGreaterThan(try AppRelease.versionCode("1.10.0"), try AppRelease.versionCode("1.9.99"))
        XCTAssertThrowsError(try AppRelease.versionCode("1.2.3-beta"))
        XCTAssertThrowsError(try AppRelease.versionCode("1.100.0"))
        XCTAssertEqual(try parse(metadata()).version, "9.9.9")
        var json = metadata(); json["draft"] = true; XCTAssertThrowsError(try parse(json))
        json = metadata(); json["prerelease"] = true; XCTAssertThrowsError(try parse(json))
        json = metadata(); json["assets"] = []; XCTAssertThrowsError(try parse(json))
    }
    func testRejectsForeignSourcesAndUnsafeRedirects() throws {
        var json = metadata(), assets = json["assets"] as! [[String: Any]]
        assets[0]["browser_download_url"] = "https://github.com/other/Lantern/releases/download/v9.9.9/" + AppRelease.asset
        json["assets"] = assets; XCTAssertThrowsError(try parse(json))
        XCTAssertTrue(AppRelease.allowed(URL(string: "https://release-assets.githubusercontent.com/asset")!))
        for url in ["http://github.com/a", "https://github.com.evil.test/a", "https://u:p@github.com/a", "https://github.com:444/a"] { XCTAssertFalse(AppRelease.allowed(URL(string: url)!)) }
    }
    func testChecksumTamperTruncationAndDuplicateManifest() throws {
        let release = try parse(metadata()), data = Data([1, 2, 3])
        let hash = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
        let line = hash + "  " + AppRelease.asset
        XCTAssertEqual(try AppRelease.checksum(Data(line.utf8)), hash)
        XCTAssertThrowsError(try AppRelease.checksum(Data((line + "\n" + line).utf8)))
        XCTAssertThrowsError(try AppRelease.checksum(Data((hash + " other.ipa").utf8)))
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: file) }
        try data.write(to: file); try release.verify(file, sha: hash)
        try Data([1, 9, 3]).write(to: file); XCTAssertThrowsError(try release.verify(file, sha: hash))
        try Data([1, 2]).write(to: file); XCTAssertThrowsError(try release.verify(file, sha: hash))
        try data.write(to: file)
        let conflicting = AppRelease(version: release.version, url: release.url, sumsURL: release.sumsURL, size: release.size, digest: "sha256:" + String(repeating: "0", count: 64))
        XCTAssertThrowsError(try conflicting.verify(file, sha: hash))
    }
}

import XCTest

final class ChatFlowTests: XCTestCase {
    func testUpdateSettingsAndPreferences() throws {
        let app = XCUIApplication()
        app.launchEnvironment["LANTERN_UI_FIXTURE_PIN"] = try XCTUnwrap(ProcessInfo.processInfo.environment["LANTERN_FIXTURE_PIN"])
        app.launch()
        app.buttons["Cài đặt"].tap()
        XCTAssertTrue(app.staticTexts["Cập nhật ứng dụng"].waitForExistence(timeout: 5))
        let auto = app.switches["Tự tải bản cập nhật"]
        XCTAssertTrue(auto.exists)
        let before = auto.value as? String
        auto.tap()
        XCTAssertNotEqual(auto.value as? String, before)
        app.buttons["Xong"].tap(); app.buttons["Cài đặt"].tap()
        XCTAssertNotEqual(auto.value as? String, before)
        let screenshot = XCTAttachment(screenshot: app.screenshot()); screenshot.name = "Lantern-iOS-updates"; screenshot.lifetime = .keepAlways; add(screenshot)
        app.terminate()
    }
    func testUnlockComposePushAndRelock() throws {
        continueAfterFailure = false
        let pin = try XCTUnwrap(ProcessInfo.processInfo.environment["LANTERN_FIXTURE_PIN"])
        let app = XCUIApplication()
        app.launchEnvironment["LANTERN_UI_FIXTURE_PIN"] = pin
        app.launch()
        let contact = app.staticTexts["QA contact"]
        XCTAssertTrue(contact.waitForExistence(timeout: 20)); contact.tap()
        let password = app.secureTextFields["Mật khẩu hội thoại"]
        XCTAssertTrue(password.waitForExistence(timeout: 10)); password.tap(); password.typeText("qa-password")
        app.buttons["Mở khóa"].tap()
        let composer = app.textViews["composer"]
        XCTAssertTrue(composer.waitForExistence(timeout: 15)); composer.tap()
        XCTAssertTrue(app.keyboards.firstMatch.waitForExistence(timeout: 15), "Tapping the composer must show the software keyboard")
        composer.typeText("Hello from iPhone UI")
        app.buttons["send"].tap()
        XCTAssertTrue(app.staticTexts["Hello from iPhone UI"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.staticTexts["Incoming immediately after send"].waitForExistence(timeout: 10))
        XCTAssertTrue(app.keyboards.firstMatch.exists, "Composer should keep the keyboard after ACK/push")
        let screenshot = XCTAttachment(screenshot: app.screenshot()); screenshot.name = "Lantern-iOS-chat"; screenshot.lifetime = .keepAlways; add(screenshot)
        app.buttons["Khóa hội thoại"].tap()
        XCTAssertTrue(password.waitForExistence(timeout: 5))
        XCTAssertFalse(app.staticTexts["Hello from iPhone UI"].exists)
        let locked = XCTAttachment(screenshot: app.screenshot()); locked.name = "Lantern-iOS-locked"; locked.lifetime = .keepAlways; add(locked)
        app.terminate()
    }
}

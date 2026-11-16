# Phương án Lantern iOS

Ngày đối chiếu tài liệu: 16/11/2026. Đây là phương án triển khai; bản 1.2.3 chưa có ứng dụng hoặc IPA iOS.

## Đề xuất

Làm companion native bằng Swift/SwiftUI, dự kiến iOS 16 trở lên. iPhone dùng danh tính PC sau khi quét QR và được PC chấp nhận, giống Android. PC giữ lịch sử chính, phải chạy Lantern và không sleep. Giữ đường LAN trực tiếp và relay riêng hiện có; không cần VPN toàn máy hoặc mở cổng router.

Với vài iPhone dùng riêng, chọn **Ad Hoc** nếu mục tiêu là cài IPA ngoài App Store. Nếu muốn mời người thử thuận tiện hơn, chọn **TestFlight**. Có thể làm bản thử miễn phí qua **AltStore Classic trên Windows**, chấp nhận ký lại thường xuyên và chưa có thông báo APNs với tài khoản miễn phí. Chưa mua tài khoản, tạo chứng chỉ iOS hoặc thay đổi relay trong lần lên phương án này.

## Có cài ngoài App Store được không?

| Cách cài | Điều kiện | Phù hợp với Lantern |
| --- | --- | --- |
| Ad Hoc | Apple Developer Program; đăng ký UDID từng máy; ký IPA bằng provisioning profile chứa các máy đó | Cài trực tiếp ngoài App Store cho nhóm thiết bị đã biết. Apple giới hạn 100 thiết bị mỗi dòng sản phẩm trong một năm thành viên; cần quản lý và gia hạn chữ ký/profile. |
| TestFlight | Apple Developer Program, upload qua App Store Connect; beta bên ngoài cần qua quy trình xét duyệt | Không cần công khai app trên App Store; người dùng cài qua TestFlight. Mỗi build dùng tối đa 90 ngày, phải phát hành build mới trước khi hết hạn. Đây vẫn là kênh beta của Apple. |
| AltStore Classic + AltServer Windows | Apple Account cá nhân, AltServer trên PC, Developer Mode trên iOS 16+ | Thử IPA cá nhân không cần mua gói Developer. Với tài khoản miễn phí, app hết hạn sau 7 ngày nếu không refresh; giới hạn 3 app sideload đang hoạt động. Có thêm bước cài/refresh nên không chọn làm kênh triển khai mặc định cho cả công ty. |
| Marketplace/web distribution của Apple | Chỉ các vùng và điều kiện Apple hỗ trợ | Trang Apple hiện liệt kê Brazil, Nhật và EU, với chức năng khác nhau từng vùng. Việt Nam chưa nằm trong danh sách; không thể xem một link IPA là cách cài phổ thông giống APK. |

Apple Developer Program hiện **99 USD/năm** hoặc giá nội tệ nếu có; đây là phí tài khoản phát triển, không phải phí mỗi iPhone. Nguồn: [phí thành viên](https://developer.apple.com/programs/whats-included/), [Ad Hoc và giới hạn thiết bị](https://developer.apple.com/help/account/devices/devices-overview/), [TestFlight và thời hạn beta](https://developer.apple.com/help/app-store-connect/test-a-beta-version/testflight-overview/).

AltStore có hướng dẫn [cài bằng Windows](https://faq.altstore.io/altstore-classic/how-to-install-altstore-windows) và [hạn dùng/refresh app](https://faq.altstore.io/altstore-classic/your-altstore). Điều kiện marketplace theo [Apple Support](https://support.apple.com/it-it/118110). Việc chọn vùng khác trong tài khoản không tự đáp ứng yêu cầu có mặt tại vùng đó.

## Thiết kế ứng dụng

| Thành phần | Cách thực hiện dự kiến |
| --- | --- |
| Giao diện | SwiftUI: danh sách hội thoại, bong bóng theo nội dung, gửi ngay tại chỗ, trạng thái gửi ở từng tin; giữ focus bàn phím qua ACK/push; hỗ trợ Dynamic Type, vùng an toàn và chế độ sáng/tối. |
| QR và danh tính | Camera quét QR v1 hiện có; kiểm tra hạn nonce, fingerprint PC, xác nhận trên PC. Khóa thiết bị và cấu hình ghép đôi bảo vệ bằng Keychain; thu hồi trên PC đóng phiên. |
| LAN | Network.framework/NWConnection cho TCP/TLS, chứng chỉ thiết bị và pin public key PC. Xử lý quyền Local Network và lỗi người dùng từ chối. |
| Internet | URLSession chạy hai chiều HTTPS tới relay; vẫn giữ TLS đầu cuối iPhone–PC bên trong. Thử nghiệm cầu nối stream/loopback chỉ nội bộ app để tái dùng TLS native; phải chứng minh mTLS, pinning, đóng phiên và chống kết nối giả trước khi chốt cách này. Không hạ xuống gửi JSON rõ cho relay. |
| Đồng bộ | Port giao thức hiện có: request ID, serial, sync-v1, push-v1, hash lịch sử, ACK tin đã hiển thị, tải ảnh/tệp tách khỏi lệnh chat. Dùng fixture .NET hiện có để kiểm tra tương thích. |
| Cache và gửi tin | Cache mã hóa theo PC, tối đa 100 MB; hàng đợi có ID ổn định để thử lại không tạo bản sao. Ghi tin cục bộ và trả ô nhập ngay, đồng bộ ở nền. Hội thoại khóa không lộ cache; tin tự hủy không ghi lịch sử bền vững. |
| Media | PhotosPicker, chọn tệp hệ thống, thumbnail và xem ảnh/video; truyền khối, kiểm tra SHA-256; hủy/tạm dừng rõ ràng khi app bị suspend. |

Đây là thiết kế đề xuất dựa trên giao thức trong repo; chưa phải xác nhận đã chạy trên iPhone. [NWConnection](https://developer.apple.com/documentation/network/nwconnection) cung cấp kết nối mạng, nhưng phần ghép TLS vào relay là hạng mục thử nghiệm đầu tiên. UI Java Android phải viết lại; có thể dùng chung đặc tả giao thức, dữ liệu kiểm thử và tài nguyên hình ảnh.

## Khi iPhone khóa màn hình hoặc app chạy nền

iOS không cho ứng dụng chat thông thường giữ mã chạy/socket vô hạn ở nền. Vì thế không thể chuyển nguyên foreground service Android sang iOS. Khi trở lại app, phải nối lại, tải phần thay đổi và đối soát hàng đợi. Nguồn: [iOS Background Execution Limits — Apple DTS](https://developer.apple.com/forums/thread/685525).

Thông báo nền dùng **APNs**, mặc định chỉ ghi “Có tin nhắn mới”, không chứa tên đối tác hoặc nội dung. Sau khi mở app, dữ liệu chat đi qua kênh TLS riêng. Silent push chỉ là tín hiệu gợi ý đồng bộ, có thể bị trì hoãn hoặc không được giao; không dùng nó làm nơi lưu tin hoặc cơ sở cam kết dưới 2 giây. Nguồn: [background notifications](https://developer.apple.com/documentation/usernotifications/pushing-background-updates-to-your-app).

Đối với bản dùng riêng ký bằng tài khoản Developer của chủ PC, có thể bổ sung module **PC gửi APNs trực tiếp qua HTTPS/HTTP2**, vì PC vốn phải bật. Khóa APNs .p8 của chủ tài khoản lưu riêng, mã hóa DPAPI trên PC; không nhúng vào IPA, source hoặc ZIP phát hành. Cách này tránh phải vận hành hosting mới. Cần thử truy cập APNs từ mạng công ty và xử lý token đổi/hết hiệu lực. Nguồn giao thức: [gửi yêu cầu tới APNs](https://developer.apple.com/documentation/usernotifications/sending-notification-requests-to-apns).

Nếu phân phối một app cho nhiều chủ PC dưới cùng tài khoản Developer, không được phát khóa APNs chung xuống các máy. Khi đó cần dịch vụ gửi push giữ khóa phía dịch vụ; đánh giá bổ sung vào hạ tầng relay sau, gồm khả năng HTTP/2, secret storage, xác thực tuyến và giới hạn tần suất. Relay hiện tại chưa có module này. Bản AltStore ký miễn phí chỉ đặt mục tiêu chat khi mở app; không hứa có entitlement APNs.

## Build, phát hành và kiểm thử

Build iOS cần Xcode trên macOS. Có thể dùng runner macOS của GitHub Actions để build, chạy simulator và ký IPA; không bắt buộc mua Mac chỉ để có máy build. Kiểm thử camera QR, chuyển Wi-Fi/4G, bàn phím tiếng Việt, khóa màn hình và thông báo vẫn cần iPhone thật. Nguồn quy trình CI: [GitHub — ký app Xcode trên macOS runners](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications).

1. **Chứng minh kết nối trước:** tạo project iOS và fixture tests; QR → mTLS LAN → relay Internet → xác nhận/thu hồi PC. Chốt giải pháp TLS qua relay khi test chạy trên iPhone thật.
2. **Chat dùng được:** danh bạ, mở khóa, lịch sử/cache, gửi lạc quan, ACK/push, retry không trùng, giữ focus. Kiểm thử tin đến lúc đang gõ, gửi liên tục, chuyển app và mất mạng.
3. **Media và bảo vệ:** ảnh/video/tệp, quote/react, read receipt và tự hủy; xác nhận log, crash report, notification và cache không làm lộ hội thoại khóa.
4. **Phân phối thử:** Ad Hoc cho thiết bị đã đăng ký hoặc TestFlight; CI lưu chứng chỉ/profile vào GitHub Secrets và tạo SHA-256 cho IPA. Giữ bundle ID/team để cập nhật không mất ghép đôi. Bản AltStore thử riêng cần kiểm chứng refresh không đổi danh tính thiết bị.
5. **APNs và nghiệm thu:** module gửi thông báo từ PC cho bản riêng; thử khóa màn hình, bật/tắt thông báo, token đổi, PC offline và trở lại app. Sau đó mới cân nhắc phát hành rộng.

Mục tiêu nghiệm thu khi app đang mở và đường truyền ổn định: phản hồi gửi tại UI dưới 100 ms, ACK PC và tin PC đẩy xuống dưới 2 giây trong bộ đo; công bố p50/p95/max và tỷ lệ lỗi riêng cho LAN và 4G. Chạy ít nhất 100 tin mỗi đường, gồm tải ảnh đồng thời. Mở lại 30 tin đã cache phải hiện trước khi đợi mạng. Không coi số đo Android/relay hiện tại là kết quả iOS, và không cam kết 2 giây khi iOS suspend, mạng gián đoạn hoặc PC ngủ.

Điều kiện để bắt đầu phát hành lên iPhone thật: chọn cách cài, có iPhone để thử; nếu chọn Ad Hoc/TestFlight/APNs thì cần Apple Developer Program và chủ tài khoản thiết lập quyền ký. Phương án này không yêu cầu cung cấp mật khẩu Apple Account trong chat.

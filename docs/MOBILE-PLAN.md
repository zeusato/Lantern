# Lantern Android — thiết bị liên kết PC

Phương án companion cho iPhone và các cách cài ngoài App Store: [IOS-PLAN.md](IOS-PLAN.md).

Lantern 1.2 bổ sung Android companion. Điện thoại dùng danh tính PC; PC giữ lịch sử chính và tiếp tục chat P2P với các máy LAN. Kênh riêng trong Lantern không cài VPN, không đổi định tuyến toàn máy và không cần chuyển tiếp cổng router.

## Đường kết nối

```text
Android ─ HTTPS 443 ─ relay Sites riêng ─ HTTPS 443 ─ Lantern PC ─ P2P LAN ─ đối tác
            ╰──────── TLS hai chiều từ Android tới PC ────────╯
```

Trong LAN, điện thoại thử cổng companion 45854 trước (PC có thể chọn cổng trống, QR ghi cổng thực tế). Qua Internet, cả hai đầu chủ động kết nối HTTPS ra relay. Vẫn cần dịch vụ Sites và mạng công ty cho phép truy cập; không bảo đảm vượt mọi firewall. Bản đầu chưa có STUN/ICE.

Relay chỉ chuyển bản ghi TLS mã hóa, không có khóa riêng hoặc chat dạng rõ. Hàng đợi giới hạn 32 gói mỗi chiều; gói quá 120 giây được dọn khi có truy cập. Tuyến hết hạn sau 7 ngày và được PC gia hạn.

PC cần chạy Lantern, đăng nhập sau khởi động và không sleep. Chạy dưới tray vẫn được. Khóa app trên PC không thu hồi điện thoại đã ghép đôi; dùng **Gỡ thiết bị** để thu hồi. Khóa thủ công hội thoại trên PC cũng khóa hội thoại đó ở điện thoại. Khi PC mất kết nối, điện thoại tự thử nối lại và khóa các hội thoại được bảo vệ. Hội thoại không có mật khẩu vẫn đọc được lịch sử đã lưu. Cache lịch sử và ảnh xem trước dùng AES-GCM trong Android Keystore, giới hạn 100 MB, tách theo danh tính PC; tin tự hủy không được ghi vào cache bền vững.

## Ghép đôi và bảo vệ

1. Mở khóa PC → Cài đặt → **Điện thoại · Ghép đôi QR và quản lý thiết bị**.
2. Tạo QR, quét bằng Lantern Android. QR chứa fingerprint PC, địa chỉ, nonce dùng một lần hết hạn sau 2 phút và quyền truy cập relay; không chứa mật khẩu hoặc khóa riêng PC. Không chia sẻ ảnh QR.
3. Android tạo khóa ECDSA riêng trong Android Keystore, kiểm tra public key PC và kết nối TLS hai chiều. Đối chiếu sáu nhóm mã rồi chấp nhận trên PC.
4. PC mã hóa danh sách thiết bị trong `mobile-devices.enc`. Android mã hóa cấu hình bằng khóa AES trong Keystore và tắt backup dữ liệu ghép đôi.
5. Gỡ thiết bị trên PC đóng phiên và từ chối reconnect. Quên PC trên điện thoại xóa cấu hình, lịch sử cache và hàng đợi cục bộ; khi mất điện thoại cần gỡ trên PC. Tối đa 8 thiết bị.

Credential relay cá nhân không nằm trong Git, ZIP PC hoặc APK. Cấu hình riêng được DPAPI bảo vệ tại `%LOCALAPPDATA%\LanternRelay\relay-bootstrap.bin`, nhập vào vault sau đăng nhập. Từ 1.2.7, PC cài mới tự đăng ký relay công khai bằng khóa riêng lưu trong vault; không cần cấu hình thủ công. Cấu hình relay riêng và thiết bị từ bản cũ được giữ nguyên. Khóa đăng ký của PC không được đưa vào QR.

## Phạm vi Android bản đầu

- Android 8 trở lên; giao diện native tiếng Việt, màu tối, bong bóng theo nội dung; tin của mình căn phải.
- Danh bạ/nhóm đã có trên PC, presence và số chưa đọc; 50 tin gần nhất của hội thoại.
- Gửi/nhận chữ, quote, react dưới danh tính PC. Tin vừa gửi hiển thị ngay; lưu hàng đợi mã hóa rồi chuyển về PC ở nền. Thử lại giữ nguyên ID qua các lần mở app; không tự gửi lại tin chưa xác nhận khi khởi động. Tin đã tự hủy có tombstone để không bị tạo lại. Hàng đợi tin tự hủy chỉ giữ trong bộ nhớ.
- Ảnh, video, tệp tối đa 100 MB, truyền khối và kiểm tra SHA-256. Ảnh đến tối đa 10 MB tự tải xem trước; ảnh lớn hơn, video và tệp cần thao tác chủ động. Video mở bằng ứng dụng xem của Android.
- PC chủ động đẩy cập nhật qua TLS khi có tin hoặc trạng thái mới. Android đọc phản hồi trên một luồng riêng, ghép phản hồi bằng request ID; lệnh chat có thể chạy đồng thời với tải ảnh. Gộp danh bạ/lịch sử/ACK trong một yêu cầu và dùng hash khi lịch sử không đổi. Giữ tương thích với PC cũ bằng polling.
- Mật khẩu hội thoại được PC xác minh; mở trong phiên kết nối, reconnect cần mở lại. API lịch sử, gửi, react, đọc và tệp đều kiểm tra khóa.
- Chỉ ACK các tin được hiển thị, ảnh ACK sau khi điện thoại tải xong. Tự hủy do PC quản lý; Android ẩn tin và bỏ ảnh cache khi tới hạn.
- Foreground service `remoteMessaging` có thông báo thường trực. Android/nhà sản xuất vẫn có thể hạn chế mạng nền; chưa có FCM hoặc cam kết nhận tức thì khi Doze, chưa có toast tin mới riêng trên Android.
- Chưa có resume tệp mobile, tìm lịch sử cũ, quản lý nhóm/đối tác, đổi cài đặt tự hủy hoặc khóa app Android riêng. Các thiết lập hội thoại thực hiện trên PC.

## Build và kiểm thử

PC dùng .NET 10/WPF và `scripts/Package-Release.ps1`. Android dùng Java 17, SDK 35, AGP 8.9.2, Gradle 8.11.1; wrapper có checksum cố định. `android/gradlew assembleDebug lintDebug` tạo APK thử nghiệm. APK release cần khóa ký riêng ngoài Git và phải dùng cùng khóa qua các lần cập nhật.

Workflow release lấy phiên bản Android từ tag `vMAJOR.MINOR.PATCH`, tăng `versionCode` theo phiên bản và ký bằng hai GitHub Actions Secrets `ANDROID_KEYSTORE_BASE64` / `ANDROID_STORE_PASSWORD`. Minor và patch giới hạn dưới 100. Release chỉ công bố sau khi có đủ ZIP PC, APK đã xác thực chữ ký và hai tệp SHA-256. Bản sao khóa ký được giữ riêng trên PC; không đưa keystore vào source hoặc gói phát hành.

Kiểm thử gồm peer thật trên loopback, TLS xuyên relay Sites riêng, khóa/thu hồi thiết bị và checksum tệp; Android Keystore xác thực hai chiều với .NET bằng TLS 1.2/1.3, từ chối sai fingerprint, cache mã hóa và gửi/nhận khi đang tải ảnh nền. CI chạy emulator Android 14 và 15. Bộ đo trong `MobileLatencyTests` ghi riêng thời gian tới PC, ACK và PC đẩy tin tới companion; mục tiêu mỗi chiều dưới 2 giây ở kết nối ổn định. Phép đo hai client trên cùng máy qua relay không thay thế kiểm thử điện thoại thật bằng 4G/firewall công ty; PC sleep, Doze hoặc mất mạng không có cam kết thời gian.

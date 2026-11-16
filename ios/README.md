# Lantern iOS — IPA cho Scarlet

Companion native SwiftUI cho iOS 16+, dùng PC và relay của Lantern hiện có. Không cần Apple Developer trả phí để tạo gói này. Đầu ra là **IPA arm64 chưa ký**, cần Scarlet ký trước khi cài. Đây không phải IPA đã được Apple cấp phép chạy trực tiếp.

## Cài và ghép đôi

1. Tải artifact `Lantern-iOS-unsigned` từ workflow **iOS IPA**, giải nén lấy `Lantern-iOS-unsigned.ipa` và `IOS-SHA256SUMS.txt`.
2. Chuyển IPA sang iPhone, nhập vào Scarlet và thực hiện ký/cài theo chứng chỉ đang sử dụng. Việc cài thành công phụ thuộc chứng chỉ/profile của Scarlet; file IPA không chứa chứng chỉ Apple, tài khoản hay credential relay.
3. Mở Lantern. Cho phép Camera và Mạng cục bộ khi iOS hỏi.
4. Trên PC: mở khóa Lantern → Cài đặt → Điện thoại → Ghép đôi QR. Quét mã bằng iPhone, đối chiếu đủ sáu nhóm mã rồi chấp nhận trên PC.
5. PC phải chạy và không ngủ. Khi đi Internet, PC cần cấu hình relay riêng trước khi tạo QR.

Giữ bundle ID `vn.lantern.companion` và cùng danh tính ký khi cập nhật. Thay team ký hoặc xóa app có thể làm mất quyền truy cập Keychain/cache; khi đó cần ghép đôi lại. Không đưa khóa ký Apple hoặc mật khẩu Apple Account vào repo.

## Phạm vi bản đầu

- QR v1, danh tính ECDSA P-256 trong Keychain, chứng chỉ client tự ký; pin SHA-256 của SPKI PC giống Android/.NET.
- TLS hai chiều qua LAN hoặc cầu nối loopback → HTTPS relay hiện có. Relay chỉ mang byte TLS, không giải mã nội dung. Không chuyển tiếp HTTP redirect chứa credential.
- Danh bạ, nhóm, 50 tin gần nhất, mở/khóa hội thoại; gửi lạc quan, ID ổn định khi gửi lại, trạng thái tin, quote và reaction.
- Chọn ảnh/video bằng PhotosPicker, chọn tệp bằng Files; truyền từng khối 32 KB, giới hạn 100 MB, kiểm tra SHA-256; xem bằng Quick Look sau khi tải xong. Ảnh hiện dưới dạng tệp có nút mở trong bản đầu, chưa tự tải thumbnail.
- Cache AES-GCM theo PC, khóa trong Keychain `WhenUnlockedThisDeviceOnly`, file protection và loại khỏi backup; tối đa 100 MB. Không lưu lịch sử hội thoại bảo vệ hoặc tin tự hủy bền vững. Tin chờ thuộc hội thoại bảo vệ/tự hủy chỉ giữ trong phiên.
- ACK chỉ dành cho tin đang hiển thị; ảnh chỉ ACK sau khi tải và kiểm tra checksum. Không tự gửi lại các tin còn chờ khi khởi động; người dùng chạm để thử lại.
- Khi app mất trạng thái active, đóng kết nối, che màn hình, khóa hội thoại bảo vệ và dọn media tạm. Trở lại app tự nối lại và đồng bộ.

**Không có APNs ở bản miễn phí này.** Không hứa nhận thông báo khi app chạy nền/khóa màn hình. Chưa có resume tệp; truyền bị ngắt cần chọn lại. Không có tìm lịch sử cũ, quản trị nhóm, đổi chính sách tự hủy hoặc khóa app riêng. Các tệp người dùng chủ động xuất qua hệ thống nằm ngoài phạm vi dọn dữ liệu của Lantern.

## Build trên macOS

Cần Xcode có SDK iOS, XcodeGen và .NET 10 SDK (cho kiểm thử).

```sh
brew install xcodegen
bash scripts/build-ios.sh
bash scripts/test-ios.sh
```

Workflow dùng runner GitHub-hosted `macos-15-intel` tiêu chuẩn trên repo public. Không dùng runner lớn, Xcode Cloud hoặc dịch vụ ký trả phí. `build-ios.sh` biên dịch Release cho **iphoneos arm64**, tắt code signing, đóng đúng `Payload/Lantern.app`, kiểm tra zip và tạo SHA-256. Không đóng binary Simulator vào IPA.

## Kiểm chứng

`LanternTests` kiểm tra frame/giới hạn, QR giả/hết hạn, P-256 identity ổn định, cache mã hóa/chống sửa/loại tin tự hủy; fixture .NET thật kiểm tra mTLS, sai pin, mở khóa, gửi, ID chống trùng và push không cần polling. HTTPS relay giả lập chỉ dùng credential QA, chuyển byte TLS thật tới fixture .NET và kiểm tra lệnh đồng thời qua cầu nối. CA HTTPS QA được truyền vào test bundle của từng lượt chạy; test vẫn xác minh chuỗi chứng chỉ, hostname và hạn dùng. Delegate CA riêng chỉ có trong test bundle, không nằm trong IPA Release; app dùng kiểm tra HTTPS mặc định của iOS và không sửa trust store. `LanternUITests` thao tác mở khóa, gửi, nhận push, giữ bàn phím và khóa lại; lưu ảnh chụp giao diện. Script truyền fingerprint fixture qua scheme chỉ dùng cho kiểm thử, không nhúng vào Release. CI chỉ xuất artifact IPA khi bước test thành công.

Đây là bộ test tự động; camera thật, quyền Local Network, cài/ký Scarlet, relay trên Wi-Fi/4G và chuyển nền trên iPhone cần nghiệm thu thiết bị thật. Chưa được gọi là đã kiểm chứng nếu chưa có kết quả runner hoặc thiết bị tương ứng.

# Cập nhật mobile

## Android

Mở **⋯ → Cập nhật ứng dụng**. App kiểm tra GitHub Releases khi mở/quay lại, tối đa một lần mỗi 6 giờ; **Kiểm tra ngay** bỏ qua thời gian chờ. Tự kiểm tra và tự tải mặc định bật, có thể tắt riêng. Tải dùng mạng hiện tại (kể cả 4G/5G). Cập nhật hoạt động độc lập với kết nối PC.

Bản mới hiện thành banner trong app. Sau khi tải, bấm **Cài đặt bản đã tải**. Lần đầu Android yêu cầu cho phép Lantern cài ứng dụng; quay lại sẽ tiếp tục với cùng APK. Xác nhận trong trình cài đặt Android. Hủy thì APK vẫn còn để thử lại. Không gỡ app; cài đè bằng cùng khóa ký giữ dữ liệu.

Chỉ nhận release ổn định ở `zeusato/Lantern`, đúng tên asset, HTTPS và các host tải của GitHub. Giới hạn APK 100 MB, xác minh kích thước/SHA-256 (và digest GitHub khi có), package/versionCode/versionName/minSDK, khóa ký đang cài. Kiểm tra lại trước khi trao APK qua FileProvider cho trình cài đặt. Android tiếp tục kiểm tra chữ ký mật mã khi cài. Bản debug không cập nhật đè bằng khóa release.

Không có cài âm thầm: tải nền và báo sẵn sàng, người dùng quyết định thời điểm cài để không ngắt chat. App không sử dụng Play Core vì phân phối APK qua GitHub.

## iOS / Scarlet

Mở **Cài đặt → Cập nhật ứng dụng**. Cơ chế kiểm tra và tùy chọn tải giống Android, chỉ chạy khi ứng dụng hoạt động và còn được iOS cấp thời gian. Tệp tải xong được giữ trong cache riêng; khi iOS dọn cache có thể tải lại. SHA-256/kích thước/nguồn được kiểm tra trước khi chia sẻ.

1. Bấm **Chia sẻ IPA / Mở bằng Scarlet**.
2. Chọn Scarlet nếu có trong bảng chia sẻ. Nếu không, **Lưu vào Tệp**, mở Scarlet và chọn nhập IPA vừa lưu.
3. Dùng chứng chỉ còn hiệu lực trong Scarlet để ký và xác nhận cài đè. Giữ bundle ID `vn.lantern.companion` và danh tính ký. Thay team/chứng chỉ hoặc gỡ app có thể làm mất quyền truy cập Keychain, khi đó cần ghép đôi lại.

Lantern không chứa chứng chỉ Apple, không yêu cầu mật khẩu Apple ID và không tự thay thế app. Bảng chia sẻ là API của iOS; khả năng nhận trực tiếp phụ thuộc bản Scarlet. Đường Lưu vào Tệp → nhập IPA là phương án dự phòng.

## Phát hành

Tag `vX.Y.Z` kích hoạt `.github/workflows/release.yml`: Windows tạo draft, Android ký APK, macOS build IPA chưa ký và chạy kiểm thử. Job cuối tải đủ ba gói và kiểm tra cả ba checksum trước khi đưa release thành latest. Cập nhật `ios/project.yml` cùng các phiên bản mặc định trước khi tag; workflow từ chối IPA có version khác tag.

Kiểm thử tự động bao gồm metadata/URL/checksum giả mạo, tải bị cắt/hỏng, APK mới cùng khóa và gói không phù hợp, lưu tùy chọn giao diện, chat/TLS/relay/focus cũ. Việc cài thực tế bằng chứng chỉ Scarlet trên iPhone không được mô phỏng bởi CI.

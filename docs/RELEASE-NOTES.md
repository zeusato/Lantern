# Lantern 1.2.0

Bản PC Windows x64 và Android companion liên kết bằng QR. Giải nén **Lantern-Windows-x64.zip**, chạy **Lantern.exe**; cài **Lantern-Android.apk** trên Android 8 trở lên.

- PC có mục **Điện thoại** trong Cài đặt: QR dùng một lần, đối chiếu mã, duyệt thiết bị và thu hồi quyền.
- Điện thoại dùng cùng danh tính PC để gửi/nhận tin, quote, react, xem danh bạ/nhóm và số chưa đọc; 50 tin gần nhất.
- Kết nối trực tiếp khi cùng LAN, hoặc qua relay riêng trên Internet. Không có VPN toàn máy, không cần mở cổng router. PC phải chạy Lantern, đã đăng nhập và không sleep.
- TLS hai chiều từ điện thoại tới PC; khóa Android nằm trong Keystore. Hội thoại có mật khẩu được kiểm tra ở PC cho tất cả thao tác; khóa thủ công PC áp dụng cả với điện thoại.
- Android gửi/nhận ảnh, video, tệp tối đa 100 MB, kiểm tra SHA-256. Ảnh đến tối đa 10 MB tự tải xem trước; video/tệp mở chủ động bằng ứng dụng Android.
- Tin đã hiển thị mới được ACK; ảnh cần tải xong. Thu hồi thiết bị ngắt kết nối đang chạy và từ chối dùng lại QR.
- Các chức năng PC 1.1.3 được giữ nguyên. Cập nhật không xóa workspace hay mật khẩu.

Credential relay cá nhân không nằm trong bản công khai. PC khác cần cấu hình relay riêng. Android bản đầu chưa có lịch sử offline, resume tệp, push FCM, khóa app Android riêng hoặc đầy đủ màn cài đặt như PC. Hạn chế mạng nền của Android vẫn áp dụng; xem **docs/MOBILE-PLAN.md**.

Đã kiểm thử 131 trường hợp core, 72 trường hợp UI WPF, build/lint Android và TLS qua relay Sites công khai. Cần kiểm chứng thêm trên điện thoại thật dùng 4G/5G với mạng công ty; không khẳng định đã thử mọi firewall/Doze. EXE PC chưa ký Authenticode; APK dùng khóa phát hành riêng.

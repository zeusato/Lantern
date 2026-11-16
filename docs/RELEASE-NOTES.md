# Lantern 1.2.2

- Bấm gửi trên Android: tin hiện ngay trong hội thoại, ô nhập trống ngay; truyền về PC chạy nền. Phân biệt Đang gửi, PC đã nhận, Đã nhận/Đã đọc và Chạm để thử lại.
- Tin chưa xác nhận giữ nguyên mã gửi khi thử lại, kể cả sau khi mở lại app. Hàng đợi được mã hóa trên điện thoại; không tự phát lại sau khi khởi động. Tin đã tự hủy không bị tạo lại bởi lần thử gửi cũ.
- PC chủ động đẩy tin và trạng thái mới xuống điện thoại. Gửi tin, mở khóa và đồng bộ không phải xếp sau toàn bộ ảnh/tệp đang tải. PC xác nhận sau khi lưu tin, tiếp tục chuyển tới đối tác LAN ở nền.
- Gộp danh bạ, lịch sử và xác nhận đã đọc trong một lượt đồng bộ; lịch sử không đổi chỉ trả hash. Kênh relay gửi/nhận độc lập, gộp các gói TLS và trao đổi dữ liệu bằng một giao dịch có xác thực.
- Lịch sử và ảnh xem trước đã tải được lưu mã hóa trên thiết bị, mở lại hiện ngay rồi đồng bộ nền. Cache tối đa 100 MB, tách theo danh tính PC. Hội thoại có mật khẩu khóa lại khi mất kết nối; tin tự hủy không lưu bền vững. Quên PC xóa cache và hàng đợi.
- Hộp mật khẩu giữ mở trong lúc xác thực, hiện tiến trình và lỗi rõ ràng. Mật khẩu đúng không còn bị trì hoãn cố định 1,5 giây. Hiển thị đường Wi-Fi/LAN hoặc Internet và tiến trình kết nối.
- Cài đặt PC rộng 720 px, thanh cuộn có rãnh riêng và nút Lưu cố định ở dưới.

Cập nhật cả PC và APK để dùng kênh push. APK dùng cùng khóa ký; cài đè để giữ ghép đôi. Mục tiêu gửi/nhận dưới 2 giây áp dụng khi kết nối ổn định; không phải cam kết thời gian với mạng gián đoạn, PC sleep hoặc điện thoại bị hạn chế chạy nền.

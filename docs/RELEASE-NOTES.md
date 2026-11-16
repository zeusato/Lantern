# Lantern 1.1.2

Chat P2P trong LAN cho Windows 10/11 x64. Giải nén **Lantern-Windows-x64.zip**, chạy **Lantern.exe**; bản portable đã kèm runtime. Bản 1.1.0/1.1.1 có thể tải qua mục Cập nhật trong app.

- Thêm tùy chọn **Đóng cửa sổ xuống khay hệ thống**, mặc định tắt. Khi bật, nút X ẩn cửa sổ và app vẫn nhận tin; nhấp đúp tray để mở lại. **Thoát Lantern** ở tray mới thoát hẳn. Đóng màn hình mở khóa có thể ẩn xuống tray và mở lại vẫn cần mật khẩu. Cập nhật/khôi phục danh tính vẫn đóng app thật.
- Toast có hai chế độ: hiện tên người gửi và nội dung, hoặc chỉ báo có tin nhắn mới. Chọn trong Hồ sơ và cài đặt; mặc định vẫn tắt. App/hội thoại khóa luôn ẩn tên và nội dung; Không làm phiền vẫn chặn toast.
- Sửa ảnh dán từ clipboard bị trong suốt do kênh alpha bằng 0. Ưu tiên PNG chuẩn để giữ transparency hợp lệ; ảnh clipboard lỗi từ bản cũ được phục hồi khi xem/lưu ra ngoài, không sửa tệp lưu trữ gốc.
- Emoji màu Microsoft Fluent 3D trong picker, log chat và reaction; sao chép vẫn giữ Unicode.
- Nút trả lời và react hiện khi rê chuột lên tin, không chiếm hàng riêng phía dưới.
- Media trong bảng thông tin hiển thị thumbnail vuông cho ảnh/video; link có ảnh Open Graph và tiêu đề khi trang hỗ trợ.

Đã qua 103 kiểm thử core, 45 kiểm thử WPF trên desktop và kiểm thử helper cập nhật Windows PowerShell. Kết nối được kiểm thử bằng các peer thật trên loopback; chưa kiểm chứng hai máy vật lý hoặc mọi chính sách firewall/VLAN.

Cập nhật giữ mật khẩu, workspace và lịch sử. Bản exe chưa ký Authenticode.

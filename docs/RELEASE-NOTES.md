# Lantern 1.1.3

Chat P2P trong LAN cho Windows 10/11 x64. Giải nén **Lantern-Windows-x64.zip**, chạy **Lantern.exe**; bản portable đã kèm runtime. Bản cũ có thể tải qua mục **Cập nhật** trong app.

- Màn đăng nhập, khóa ứng dụng và mở khóa hội thoại nằm ngay trong app. Đăng nhập thành công vẫn dùng cùng cửa sổ, không xuất hiện thêm một mục mở khóa trên taskbar. Ô mật khẩu tự focus; lời chào hiển thị tên tài khoản.
- Hội thoại đang khóa chặn toàn bộ bảng thông tin: Media/Tệp/Link, tìm kiếm và cài đặt, kể cả chế độ cửa sổ nhỏ. Mở khóa vẫn có hiệu lực suốt phiên cho đến khi chủ động khóa lại.
- Bong bóng tin nhắn rộng theo nội dung, tin của mình căn phải và của đối tác căn trái; tin dài tự xuống dòng. Emoji và số lượt react nằm gọn ở mép dưới bong bóng, cạnh nút thả biểu cảm; nút trả lời xuất hiện khi hover.
- Hiển thị số tin chưa đọc trên icon taskbar, bên cạnh badge tray và badge từng hội thoại đã có. Đọc hết tin sẽ xóa badge; `99+` khi vượt 99.
- Ảnh từ đối tác đã ghép đôi tự tải, kiểm tra SHA-256 và hiện trong log. Tối đa 3 lượt nhận đồng thời, tự tiếp tục khi kết nối lại. Tệp và video vẫn cần chủ động nhận. Ảnh đang chờ tải không bị đánh dấu đã đọc hoặc bắt đầu tự hủy sớm.

Đã qua 109 kiểm thử core, 70 kiểm thử WPF trên desktop và kiểm thử helper cập nhật Windows PowerShell. Kết nối được kiểm thử bằng các peer thật trên loopback; chưa kiểm chứng hai máy vật lý hoặc mọi chính sách firewall/VLAN.

Cập nhật giữ mật khẩu, workspace và lịch sử. Bản exe chưa ký Authenticode. Phương án Android/QR/Internet được ghi trong `docs/MOBILE-PLAN.md`; bản này chưa bao gồm app mobile hay giao thức liên kết thiết bị.

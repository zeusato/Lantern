# Lantern 1.1.0

Chat P2P trong LAN cho Windows 10/11 x64. Giải nén **Lantern-Windows-x64.zip**, chạy **Lantern.exe**. Bản portable đã kèm runtime.

- Đổi tên công khai, đặt tên riêng cho đối tác chỉ trên máy mình.
- Badge chưa đọc trên tray và từng hội thoại; toast tin mới mặc định tắt, bật trong Cài đặt.
- Emoji picker và tự đổi ký hiệu; react, quote và dán ảnh clipboard.
- Chấm online/offline/idle sau 15 phút/Không làm phiền; DND không hiện toast tin nhắn.
- Hội thoại mở khóa một lần cho cả phiên, có nút khóa ngay trên tiêu đề.
- Bảng thông tin chia Chia sẻ, Tìm kiếm, Cài đặt; nội dung chia Media/Tệp/Link.
- Tin tự hủy theo thời gian sau khi đọc, đồng bộ giữa các máy; nhóm tính từ lượt đọc đầu tiên. Tính năng này yêu cầu các thành viên dùng bản 1.1 trở lên.
- Chia sẻ liên hệ và tìm tên qua các máy đã ghép đôi, vẫn xác minh danh tính khi kết nối mới.
- Tự kiểm tra/tải bản mới từ GitHub, xác minh SHA-256, thông báo sẵn sàng và cài khi người dùng chọn khởi động lại.

Đã qua 94 kiểm thử core, 16 kiểm thử WPF trên desktop và kiểm thử helper cập nhật Windows PowerShell. Kết nối LAN được kiểm thử bằng các peer thật trên loopback; cần kiểm tra thêm trên các máy/vùng mạng thực tế của người dùng. Quét tự động phụ thuộc UDP trong LAN; có thể dùng IP, thẻ liên hệ hoặc tìm qua peer khi UDP bị chặn.

Mật khẩu workspace và lịch sử hiện có được giữ khi thay bản portable. Bản exe chưa ký Authenticode. Tin tự hủy không xóa các bản sao người dùng đã lưu bên ngoài app.

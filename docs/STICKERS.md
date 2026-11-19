# Sticker trên PC

Mở nút **Sticker** cạnh emoji trong thanh soạn thảo. **Xưởng chế tác sticker** cũng có trong Hồ sơ và cài đặt, kể cả khi chưa chọn hội thoại.

- **Import ảnh**: chọn nhiều PNG/GIF, chọn bộ hiện có hoặc nhập tên bộ mới, đặt tên từng sticker rồi lưu. Tên có tối đa 100 ký tự. Tìm theo tên bộ hoặc sticker, hỗ trợ gõ tiếng Việt không dấu.
- **Tạo sticker**: chọn PNG/JPEG/BMP; kéo vùng chữ nhật hoặc vẽ tự do kín quanh chủ thể rồi bấm **Cắt vùng chọn**. **Xóa nền đơn sắc** loại vùng màu gần các góc ảnh; dùng dung sai để điều chỉnh. Nền phức tạp cần vùng chọn thủ công. Thanh viền trắng từ 0–16 px; khôi phục ảnh để làm lại. Xem trước rồi đặt tên, chọn bộ để xuất bản PNG trong suốt.
- **Sửa bộ**: thêm sticker, đổi tên bộ, đổi tên/xóa/đổi vị trí từng sticker, xóa bộ. Thay đổi lưu ngay trên máy. Xóa bộ khỏi thư viện không làm mất sticker đã gửi trong chat.
- **Dùng trong chat**: chọn sticker để gửi ngay. GIF tự phát trong log; ảnh xem trước trong picker phát khi trỏ chuột. Gần đây lưu tối đa 40 sticker. Có thể lưu sticker nhận được vào thư viện khi hội thoại cho phép chuyển tiếp.
- **Chia sẻ bộ**: xuất `.lanternstickers` hoặc bấm **Gửi bộ vào chat**. Máy nhận tải tệp rồi bấm **Thêm bộ sticker**. Mỗi lần nhập tạo bản sao độc lập để tránh ghi đè sửa đổi của người nhận. Sửa bộ trên một máy không tự sửa bản trên máy khác; gửi lại bộ khi cần cập nhật. Các PC dùng cùng bản app có cùng thư viện đóng gói ban đầu.

## Giới hạn và tương thích

PNG/GIF tối đa 8 MB, 2048 × 2048 px; mỗi bộ tối đa 500 sticker và 128 MB. GIF tối đa 300 khung và tổng 16 triệu pixel giải mã. Trình chế tác thu ảnh về cạnh dài tối đa 480 px trước khi thêm viền. Không có dịch vụ AI xóa nền hoặc tải ảnh lên Internet.

Sticker đi qua luồng ảnh hiện có: TLS tới máy đã ghép đôi, tự tải, kiểm tra SHA-256, tiếp tục tải khi nối lại. Metadata tên/bộ được giữ trong lịch sử và chuyển tiếp. Client cũ vẫn nhận dưới dạng ảnh; PC cũ có thể chỉ hiện khung đầu GIF. Xưởng/thư viện là tính năng PC; chưa có trình quản lý bộ sticker trên mobile.

Ảnh và manifest thư viện lưu trong `stickers/` của workspace, chưa mã hóa riêng trên ổ đĩa giống media hiện có. Tệp bộ chỉ chứa manifest và ảnh được khai báo, kiểm tra kích thước, đường dẫn và hash trước khi nhập. Animation dừng khi phần tử bị gỡ/ẩn; trong chat còn dừng và nhả các khung đã giải mã khi ra ngoài vùng cuộn.

## Thư viện lấy từ Zalo trên máy nguồn

Đã quan sát trực tiếp Zalo PC: tab Sticker/Emoji/GIF, tìm kiếm, mục gần đây, hàng chọn bộ; kho sticker có tải bộ, quản lý có tên/số lượng, kéo đổi thứ tự và gỡ bộ.

Đã đọc riêng cache ảnh sticker, không lấy nội dung chat. Cache có 3.399 tệp thuộc 260 mã bộ; các thumbnail và phiên bản động cùng mã sticker được hợp nhất còn 1.993 sticker (1.404 động). Đây là tài nguyên đã có trong cache, bao gồm cả ảnh xem trước của bộ chưa tải đầy đủ; không phải toàn bộ kho Zalo online.

Phần lớn chuyển động nằm trong PNG dạng dải khung ngang. Importer chuyển dải này thành GIF với 100 ms/khung do cache ảnh không kèm timing. Giữ nguyên GIF gốc; chuyển WebP theo nội dung thực tế. GIF dùng bảng màu và alpha nhị phân nên mép bán trong suốt có thể khác ảnh gốc. Tên bộ/sticker dùng mã nguồn khi không có metadata tên đọc được; người dùng có thể đổi tên trong xưởng.

Tài nguyên nhúng trong assembly để giữ định dạng bộ cài/update sáu tệp hiện tại. Bộ mặc định chỉ cài một lần cho mỗi workspace; sửa/xóa của người dùng không bị bản mặc định ghi đè khi mở lại. Danh mục kiểm kê ở `src/Lantern.App/Assets/Stickers/source.json`.

## Kiểm thử

`dotnet run --project tests/Lantern.Tests -- --stickers-only` kiểm tra thư viện, nhập/xuất, dữ liệu sai, gửi riêng/nhóm, chuyển tiếp, gửi cả bộ và lịch sử.

`dotnet run --project tests/Lantern.UiTests -- --stickers <thư-mục-output>` giải mã toàn bộ thư viện đóng gói, kiểm tra chọn vùng/xóa nền/viền trắng/tìm kiếm/khóa hội thoại và render các giao diện để kiểm tra hình ảnh. Không mở hoặc đăng nhập hồ sơ thật.

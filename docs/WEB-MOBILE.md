# Lantern Web trên iPhone

## Cài và ghép đôi

1. Cập nhật Lantern PC lên **1.2.6** và mở khóa workspace.
2. Trên iPhone, dùng Safari mở [Lantern Web](https://lantern-private-relay-zeusato.tranquymtcn.chatgpt.site). Trang được mở công khai; không cần tài khoản ChatGPT hoặc ký IPA.
3. Chọn **Chia sẻ → Thêm vào Màn hình chính**, rồi mở biểu tượng Lantern mới.
4. Tạo mật khẩu bảo vệ bản lưu trên điện thoại. Đây là mật khẩu cục bộ, không phải tài khoản cloud mới.
5. Trên PC: **Cài đặt → Điện thoại → Ghép đôi iPhone · Bản web**. Trên điện thoại chọn **Mở camera** và cho phép camera.
6. So sánh mã xác minh trên hai máy, chọn **Cho phép thiết bị** trên PC. QR dùng một lần, hết hạn sau 2 phút.

Nếu camera khó nhận, dùng **Chọn ảnh QR** với ảnh chụp trọn mã. Bộ quét hiển thị riêng lỗi hết hạn và lỗi dùng mã Android/iOS; hãy tạo đúng mã Bản web.

PC giữ danh tính LAN. Điện thoại cần PC đang chạy, đã mở khóa Lantern và không sleep. Internet dùng cùng relay HTTPS, không cần mở cổng router hay VPN toàn máy. PC mới vẫn cần cấu hình relay riêng; khóa vận hành relay không nằm trong bản phát hành công khai.

## Trải nghiệm mobile

- Bố cục một cột, chat toàn màn hình, ô nhập bám bàn phím, vùng chạm lớn và bảng thao tác trượt từ dưới.
- Bấm gửi: ô nhập trống, tin xuất hiện ngay; lưu hàng đợi mã hóa trước khi chuyển về PC. Tự thử lại cùng mã tin để không gửi trùng. Gửi xong giữ focus.
- Lịch sử đã tải lưu mã hóa trong IndexedDB, mở lại hiện trước rồi đồng bộ. Bản đầu dùng snapshot tối đa 50 tin gần nhất mỗi hội thoại; lịch sử đầy đủ vẫn nằm trên PC.
- Nhấn giữ tin nhắn để react, trích dẫn, sao chép hoặc xóa. Chọn nhiều tin và dọn hội thoại có bước xác nhận. Xóa ở PC/PWA không thu hồi bản ở máy đối tác; PWA nhận phiên bản lịch sử mới để bỏ cache cũ kể cả hội thoại không đang mở.
- Ảnh nhỏ tự tải để hiển thị. Ảnh/video/tệp tối đa 100 MB; giữ app mở khi truyền. File cần nhận chủ động. File tải xong có thể lưu/chia sẻ qua iOS. Ảnh xem trước giữ trong bộ nhớ phiên, không lưu vào cache công khai của service worker.
- Hội thoại có mật khẩu khóa cả log và bảng thông tin. Mở trong cả phiên, có nút khóa riêng. Thiết lập mật khẩu và thời gian tự hủy trên PC; web nhận chính sách và ẩn tin khi hết hạn.
- 5 bảng màu: Ngọc bích, Đại dương, Tử đinh hương, Hồng trà, Hổ phách; tự thích nghi sáng/tối của điện thoại.
- Không triển khai Web Push. Khi iOS dừng ứng dụng, tin mới được đồng bộ ở lần mở tiếp theo. Thông báo trong app có thể tắt trong cài đặt.
- Bản web mới được tải qua service worker; chọn **Cài đặt → Kiểm tra cập nhật** để áp dụng. Không cần IPA/chứng chỉ.

## Bảo vệ và vận hành

Trang công khai chỉ cung cấp giao diện. Mỗi thiết bị cần route key ngẫu nhiên từ QR, khóa định danh riêng và PC xác nhận. Kênh web dùng chữ ký ECDSA P-256, ECDH tạm thời, HKDF-SHA256 và AES-256-GCM với số thứ tự chống phát lại. Chữ ký gắn với danh tính PC, route, phiên và khóa tạm thời. Kênh Android/iOS native vẫn dùng TLS.

Mật khẩu cục bộ dẫn xuất khóa bằng PBKDF2-SHA256 (310.000 vòng). Lịch sử hội thoại có mật khẩu được mã hóa thêm bằng khóa riêng; hàng đợi/nháp có hộp mã hóa riêng để việc xóa cache lịch sử không làm mất tin chưa gửi. Mã nguồn và browser bundle không chứa service credential của Sites.

Relay chỉ cho PC có cấu hình hợp lệ tạo route mới: yêu cầu HMAC có hạn thời gian, khóa kiểm chứng nằm trong secret runtime `LANTERN_REGISTRATION_KEY`. Route đã tồn tại có thể gia hạn bằng host key để giữ tương thích PC cũ. Các endpoint trao đổi/thu hồi vẫn kiểm tra host/client key, phiên và thời hạn. Không có API liệt kê người dùng/hội thoại công khai.

Nguồn ở `pwa/`, Worker ở `relay/`. Chạy `npm ci --prefix pwa`, `node relay/scripts/build-web.mjs` để tạo Worker chứa tài nguyên web. `scripts/Test-Web.ps1` kiểm thử WebCrypto thực với PC giả lập, chống giả mạo/replay, gửi trùng, khóa và xóa. Không đóng gói fixture vào app.

## Kiểm chứng tốc độ

Ngày 17/11/2026: phép đo tổng hợp qua relay Internet thật, PC và trình WebCrypto kiểm thử ở cùng máy chạy nhưng dữ liệu đi qua dịch vụ hosted. Hai lượt đo, mỗi lượt 5 mẫu: gửi→PC→xác nhận **856–2.656 ms**, tin LAN→web push **766–1.531 ms**; tải 31–41 tin khoảng **1,4–2,5 giây**. Ghép đôi mới khoảng 3–6 giây, không tính thời gian người dùng xác nhận. Lượt đo công khai cuối có 4/5 xác nhận gửi vượt 2 giây: **chưa đạt ngưỡng dưới 2 giây ổn định**. Tin vẫn xuất hiện ngay nhờ hàng đợi cục bộ. Kết quả không phải bảo đảm cho mạng 4G yếu, PC sleep hoặc lúc iOS tạm dừng app. Chưa kiểm tra trên iPhone vật lý.

## Phạm vi bản đầu

Quản lý danh bạ, đặt biệt danh, tạo nhóm, đặt mật khẩu hội thoại và thay chính sách tự hủy thực hiện trên PC. Web xem/gửi trong các hội thoại đã có, chia sẻ ảnh/tệp, react/quote, quản lý bản lưu và xóa lịch sử. Danh thiếp nhận được hiển thị trên web; kết nối liên hệ mới thực hiện trên PC. Video codec do Safari hỗ trợ; không chuyển mã video trên relay.

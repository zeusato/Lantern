# Lantern Web 1.2.8 r2

- Máy tính theo mẫu iPhone: nền đen, phím tròn, cột phép tính cam chữ trắng, dấu phẩy thập phân và bố cục phím mới.
- Lịch sử phép tính trong phiên; có thể chọn lại kết quả để tính tiếp. Không ghi phép tính mở Lantern vào lịch sử.
- Dùng icon do người dùng cung cấp, xuất PNG 180/192/512 px và cập nhật biểu tượng PWA/Safari.
- Giữ phép tính mở Lantern và chế độ im lặng. Bản vá chỉ cập nhật web; PC/APK giữ 1.2.8.

# Lantern 1.2.8

- **PC:** bảng nội dung chia sẻ giữ nguyên thumbnail khi có tin chữ, reaction hoặc trạng thái đọc; chỉ cập nhật ô ảnh/tệp thay đổi. Có nút **Làm mới** để tải lại thủ công.
- **Android:** giữ ô nhập, con trỏ và phần chữ đang gõ khi có tin đến hoặc kết nối đổi trạng thái. Nháp giữ khi chuyển hội thoại/xoay màn hình; ô nhập tự giãn tối đa 6 dòng và nút gửi dùng biểu tượng máy bay giấy.
- **Web/iPhone:** mở bằng giao diện **Máy tính**, có phép tính thông thường, phần trăm và đổi dấu. Gõ **123456789 × 91**, bấm **=** để vào Lantern; mật khẩu bảo vệ dữ liệu vẫn giữ nguyên.
- Chế độ máy tính không hiện thông báo chat/cập nhật, không âm thanh, không badge số tin hay nội dung chat. Tin vẫn đồng bộ âm thầm, chưa đánh dấu đã đọc. Chuyển app ra nền tự che lại; có nút **Mở máy tính** trong cài đặt. Tên và icon PWA mới là **Máy tính**.

Cài đè PC/APK để giữ dữ liệu. Web: **Cài đặt → Kiểm tra cập nhật → Cài bản web mới**. Lớp máy tính chỉ che giao diện, không thay mật khẩu hay mã hóa. Không phát hành IPA.

# Lantern 1.2.7

- **Ghép đôi trên mọi PC cài mới:** tự thiết lập relay, không nhập URL/khóa thủ công và không cần tài khoản ChatGPT. Nút **Ghép đôi iPhone · Bản web** luôn bấm được.
- Hiện trạng thái đang kết nối/tạo QR, lỗi mạng và cho thử lại; đóng bảng ghép đôi sẽ hủy thao tác đang chờ. Cấu hình relay riêng chuyển vào **Nâng cao**.
- Mỗi bản cài có khóa riêng, lưu mã hóa trong workspace. Khóa quản trị không nằm trong bộ cài; QR chỉ chứa quyền kết nối của thiết bị. Relay có giới hạn đăng ký và số kết nối theo từng bản cài.
- Giữ cấu hình relay và thiết bị đã ghép đôi. Android hỗ trợ relay công khai mà không gửi header credential rỗng; nếu Internet lỗi vẫn có thể ghép đôi trong LAN.

Cập nhật PC lên **1.2.7** rồi vào **Cài đặt → Điện thoại → Ghép đôi iPhone · Bản web**. Máy cài mới chỉ cần có Internet. APK cập nhật cùng bản; không phát hành IPA.

# Lantern 1.2.6

- PC: chọn bảng màu bằng 5 ô có màu mẫu, tên và dấu chọn. Bấm là áp dụng và lưu ngay toàn giao diện, không cần đóng cài đặt. Chế độ sáng/tối/theo Windows cũng đổi trực tiếp bằng nút.
- Web: sửa quét QR dày từ màn hình PC bằng cách giữ chi tiết vùng giữa ảnh, thử nhiều độ phân giải và đọc cả mã đảo màu. Dùng bộ đọc QR của trình duyệt khi hỗ trợ, có bộ đọc JavaScript dự phòng.
- Web: nút mở camera ẩn khi đang quét; có trạng thái, nút dừng và chọn ảnh QR. Báo rõ mã hết hạn hoặc mã dành cho app Android/iOS; tiếp tục quét để nhận mã đúng.
- Web: đóng màn hình quét sẽ tắt camera, kể cả khi yêu cầu cấp quyền hoặc giải mã còn đang chờ. Bản vá không thay đổi dữ liệu ghép đôi/lịch sử.

Trên iPhone: Cài đặt → Kiểm tra cập nhật → Cài bản web mới. Tạo mã bằng nút **Ghép đôi iPhone · Bản web** trên PC. Đã kiểm thử giải mã QR tạo bằng thư viện PC và camera giả lập trong trình duyệt; chưa xác nhận trên iPhone vật lý.

# Lantern 1.2.5

- **Lantern Web cho iPhone**: giao diện tối ưu mobile, thêm vào Màn hình chính bằng Safari, ghép QR và xác nhận trên PC. Không cần ký IPA hoặc đăng nhập ChatGPT để mở trang.
- Tin gửi hiện ngay, giữ focus ô nhập; hàng đợi/lịch sử mã hóa trên điện thoại, tự nối lại và không gửi trùng. React/quote bằng nhấn giữ, ảnh/tệp, khóa hội thoại và thông tin chia sẻ. Chi tiết cài đặt và giới hạn bản đầu: [WEB-MOBILE.md](https://github.com/zeusato/Lantern/blob/main/docs/WEB-MOBILE.md).
- **PC/PWA**: xóa một tin, chọn nhiều tin để xóa, dọn lịch sử hội thoại có xác nhận. Chỉ xóa bản của tài khoản mình, không thu hồi ở máy đối tác. Giữ tệp gốc ngoài workspace và chặn replay làm tin đã xóa xuất hiện lại.
- **5 bảng màu**: Ngọc bích, Đại dương, Tử đinh hương, Hồng trà, Hổ phách, dùng cùng chế độ sáng/tối.
- **PC**: reaction nhận hover và click, trễ ẩn 0,5 giây; đi vào dãy emoji hủy đếm ngược.
- **PC**: tùy chọn Khởi động cùng Windows, mặc định bật. Đăng ký cho người dùng hiện tại, giữ đúng workspace; có thể tắt trong cài đặt.
- **PC**: IP chỉ hiển thị trong bảng thông tin bên phải; bỏ khỏi danh sách hội thoại, tiêu đề chat, danh thiếp và thanh trạng thái.
- Relay public vẫn yêu cầu khóa riêng từng thiết bị và kênh mã hóa tới PC; tạo route mới cần HMAC của PC được cấu hình. Không nhúng credential vận hành vào web hoặc bản phát hành.

Cập nhật PC lên 1.2.5 trước khi ghép bản web. PC phải mở khóa và không sleep. Thông báo nền iOS không phải mục tiêu bản web này; khi app bị dừng, mở lại để nhận tin. Hai lượt đo relay thật: xác nhận gửi 0,9–2,7 giây, nhận 0,8–1,6 giây; chưa đạt dưới 2 giây ổn định và chưa thử trên iPhone vật lý.

Release 1.2.5 chỉ gồm PC và APK cùng checksum; ngừng đóng gói IPA trong quy trình release. iPhone dùng PWA qua Safari. APK tiếp tục ký bằng khóa hiện có; các thay đổi giao diện mới trong bản này áp dụng cho PC/PWA.

# Lantern 1.2.4

- Chat gọn hơn trên PC/Android/iOS: giờ và trạng thái nằm trong bong bóng. Các tin liên tiếp cùng người gửi, cách nhau tối đa 5 phút trong cùng ngày, chỉ hiện giờ/trạng thái đọc ở tin cuối; thu hẹp khoảng cách và chỉ hiện tên ở đầu nhóm. Trạng thái đang gửi/lỗi và bộ đếm tự hủy vẫn được giữ riêng.
- PC: rê chuột vào nút reaction mở ngay dãy emoji; chuyển chuột từ nút sang dãy chọn vẫn giữ mở. Không cần bấm để mở bảng chọn.

- Android: mục **⋯ → Cập nhật ứng dụng**, tự kiểm tra khi mở/quay lại app (giãn cách 6 giờ), tùy chọn tự tải và báo bản mới ngay trong app. Bấm **Cài đặt bản đã tải**, cấp quyền cài từ Lantern lần đầu và xác nhận với Android. Cài đè giữ ghép đôi/lịch sử.
- APK được đối chiếu SHA-256, mã ứng dụng, phiên bản và khóa ký của bản đang cài. Gói lỗi, gói cũ hoặc khác khóa ký bị từ chối. Tải cập nhật có tiến trình riêng, không xếp hàng cùng gửi/nhận tin nhắn.
- iOS: bổ sung IPA arm64 cho iOS 16+, ghép QR cùng PC, chat và lưu cache mã hóa. **Cài đặt → Cập nhật ứng dụng** tự kiểm tra/tải IPA, xác minh SHA-256 rồi chia sẻ sang Scarlet hoặc lưu vào Tệp để nhập trong Scarlet.
- IPA **chưa ký**; người dùng ký/cài bằng Scarlet với chứng chỉ hợp lệ. Lantern không tự ký hoặc cài âm thầm trên iOS. Giữ nguyên bundle ID `vn.lantern.companion` và danh tính ký; thay danh tính ký có thể cần ghép đôi lại. Không gỡ app trước khi cập nhật.
- Release tự động chỉ được công khai sau khi Windows, Android và IPA cùng checksum đã tải lên và kiểm tra đủ. Build IPA và kiểm thử Simulator là một bước bắt buộc của release.

Lần đầu lên 1.2.4 cần tải APK/IPA thủ công vì bản mobile cũ chưa có trình cập nhật. Hai tùy chọn tự kiểm tra và tự tải mặc định bật; có thể tắt riêng. Tải dùng kết nối hiện tại, gồm dữ liệu di động. iOS hiện nhận tin khi mở app, chưa có APNs; cài/ký bằng Scarlet trên iPhone thật cần chứng chỉ của người dùng.

## Sửa lỗi từ 1.2.3

- Sửa ô soạn tin Android mất focus sau khi gửi hoặc nhận cập nhật. Tự cuộn lịch sử giữ focus ở ô nhập; xóa nội dung vừa gửi ngay trong bộ đệm đang soạn, tiếp tục gõ tin kế tiếp bằng bàn phím đang mở.
- Kiểm thử gửi liên tiếp không chạm lại ô nhập, sau khi PC xác nhận và đẩy tin đến; kiểm tra focus, kết nối bàn phím và chữ đã được bố trí hiển thị.
- Nút gửi và nút đính kèm luôn căn dưới ô soạn nhiều dòng, không bị lệch theo đường căn chữ.
- Khi ngắt kết nối Android, khóa dữ liệu hội thoại trước khi công bố phiên đã dừng và bỏ qua phản hồi đồng bộ đến muộn.
- Có phương án iOS tại `docs/IOS-PLAN.md`: ghép QR cùng PC, kênh riêng qua LAN/relay, lưu lịch sử cục bộ và các cách phân phối IPA/TestFlight.

Cài đè APK để giữ ghép đôi. PC 1.2.2 tương thích với bản sửa Android này; gói PC 1.2.3 giữ cùng phiên bản phát hành.

## Cải tiến từ 1.2.2

- Bấm gửi trên Android: tin hiện ngay trong hội thoại, ô nhập trống ngay; truyền về PC chạy nền. Phân biệt Đang gửi, PC đã nhận, Đã nhận/Đã đọc và Chạm để thử lại.
- Tin chưa xác nhận giữ nguyên mã gửi khi thử lại, kể cả sau khi mở lại app. Hàng đợi được mã hóa trên điện thoại; không tự phát lại sau khi khởi động. Tin đã tự hủy không bị tạo lại bởi lần thử gửi cũ.
- PC chủ động đẩy tin và trạng thái mới xuống điện thoại. Gửi tin, mở khóa và đồng bộ không phải xếp sau toàn bộ ảnh/tệp đang tải. PC xác nhận sau khi lưu tin, tiếp tục chuyển tới đối tác LAN ở nền.
- Gộp danh bạ, lịch sử và xác nhận đã đọc trong một lượt đồng bộ; lịch sử không đổi chỉ trả hash. Kênh relay gửi/nhận độc lập, gộp các gói TLS và trao đổi dữ liệu bằng một giao dịch có xác thực.
- Lịch sử và ảnh xem trước đã tải được lưu mã hóa trên thiết bị, mở lại hiện ngay rồi đồng bộ nền. Cache tối đa 100 MB, tách theo danh tính PC. Hội thoại có mật khẩu khóa lại khi mất kết nối; tin tự hủy không lưu bền vững. Quên PC xóa cache và hàng đợi.
- Hộp mật khẩu giữ mở trong lúc xác thực, hiện tiến trình và lỗi rõ ràng. Mật khẩu đúng không còn bị trì hoãn cố định 1,5 giây. Hiển thị đường Wi-Fi/LAN hoặc Internet và tiến trình kết nối.
- Cài đặt PC rộng 720 px, thanh cuộn có rãnh riêng và nút Lưu cố định ở dưới.

Cập nhật cả PC và APK để dùng kênh push. APK dùng cùng khóa ký; cài đè để giữ ghép đôi. Mục tiêu gửi/nhận dưới 2 giây áp dụng khi kết nối ổn định; không phải cam kết thời gian với mạng gián đoạn, PC sleep hoặc điện thoại bị hạn chế chạy nền.

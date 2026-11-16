# Phương án Lantern Android

Đây là phương án triển khai tiếp theo, chưa phải tính năng của bản PC 1.1.3. Bản hiện tại chưa có APK, QR liên kết, đồng bộ nhiều thiết bị hoặc kết nối Internet.

## Kiến trúc đề xuất

Làm Android dưới dạng thiết bị liên kết của tài khoản PC. Điện thoại gửi yêu cầu qua kết nối mã hóa tới PC; PC gửi/nhận với các đối tác LAN bằng danh tính hiện có. Người đối diện vẫn thấy một tài khoản. PC giữ vai trò cầu nối và nguồn lịch sử chính.

```text
Android ── TLS + xác thực khóa thiết bị ── PC của chủ tài khoản ── P2P LAN ── Đối tác
             Wi-Fi LAN hoặc VPN
```

PC phải chạy Lantern, đã đăng nhập sau lần khởi động gần nhất và không ngủ. Đóng xuống tray vẫn hoạt động. PC tắt/ngủ/mất mạng: điện thoại xem phần lịch sử đã được đồng bộ và xếp hàng tin gửi, tiếp tục khi PC kết nối lại. Việc điện thoại chat độc lập khi PC tắt là giai đoạn khác, cần thiết kế nhiều thiết bị thực sự, đồng bộ danh tính, khóa và chuyển phát.

## Liên kết qua QR

1. Chủ tài khoản mở khóa PC → Cài đặt → Thiết bị liên kết → Thêm điện thoại.
2. PC tạo QR chứa địa chỉ kết nối, phiên bản giao thức, fingerprint khóa PC và token ngẫu nhiên dùng một lần, hết hạn sau khoảng 2 phút. QR không chứa mật khẩu hay khóa riêng PC. Mã định danh/mã đối chiếu hiện tại không đủ để đăng nhập.
3. Android quét QR, kiểm tra khóa PC theo fingerprint và tự tạo khóa thiết bị trong Android Keystore. Gửi yêu cầu liên kết qua TLS, chứng minh sở hữu khóa thiết bị và token.
4. PC hiện tên thiết bị và mã đối chiếu; chủ tài khoản xác nhận. Token bị tiêu thụ nguyên tử, không dùng lại. Điện thoại nhận quyền gắn với khóa riêng của nó, không nhận bản sao khóa riêng PC.
5. PC có danh sách thiết bị, lần hoạt động gần nhất và nút thu hồi. Thu hồi đóng ngay phiên hiện tại và từ chối mọi phiên sau, kể cả reconnect.

Android có khóa app riêng bằng mật khẩu/PIN hoặc sinh trắc thiết bị. Mật khẩu PC chỉ dùng mở workspace trên PC. Khóa app PC là khóa màn hình cục bộ; thiết bị đã liên kết có thể tiếp tục hoạt động trong phiên PC, có tùy chọn ngắt toàn bộ thiết bị. Hội thoại có mật khẩu mặc định không được xuất lịch sử/nội dung sang mobile: phải mở khóa và cấp quyền đồng bộ riêng trên PC. Mobile cũng chặn log, tìm kiếm, file/media/link, thông báo và cache giải mã khi hội thoại bị khóa. Không được dùng liên kết QR để bỏ qua khóa hội thoại.

## Dùng từ Internet

| Phương án | Việc cần làm | Đánh đổi |
|---|---|---|
| LAN trước | Kết nối Android trực tiếp tới dịch vụ companion trên PC; có nhập IP nếu discovery bị chặn | Không cần hạ tầng Internet; hai máy phải định tuyến được tới nhau |
| VPN cho bản đầu | PC và điện thoại cùng mạng VPN, ví dụ Tailscale; giới hạn dịch vụ companion vào mạng/thiết bị được cho phép | Dễ triển khai hơn; có thêm app VPN và hạ tầng của nhà cung cấp VPN |
| Internet tích hợp | Dịch vụ rendezvous tìm endpoint, thử kết nối trực tiếp qua NAT, relay mã hóa khi không nối thẳng được | Cần vận hành hạ tầng và băng thông relay; đổi lại người dùng không phải cài VPN |

Đề xuất phát hành theo thứ tự **LAN → VPN → Internet tích hợp**. Không cần máy chủ lưu lịch sử chat, nhưng kết nối ổn định qua NAT/CGNAT thường cần hỗ trợ thiết lập hoặc chuyển tiếp kết nối. Tailscale cũng phân biệt đường trực tiếp và đường relay khi đường trực tiếp không khả dụng: [tài liệu kết nối](https://tailscale.com/docs/reference/connection-types).

Lantern hiện kiểm tra địa chỉ chỉ thuộc IPv4 private/link-local/loopback; dải 100.64.0.0/10 của Tailscale chưa được chấp nhận. Không chỉ cài VPN rồi cho rằng app cũ sẽ chạy. Cần dịch vụ companion có chính sách endpoint riêng, xác thực thiết bị và cấu hình VPN rõ ràng. Không mở toàn bộ cổng LAN hiện tại ra Internet. Bản relay phải giữ lớp mã hóa đầu cuối giữa điện thoại và PC, ngoài TLS tới relay; relay không được đọc nội dung.

## Công nghệ và đồng bộ

- Android native bằng **Kotlin + Jetpack Compose**, giao diện cùng màu sắc và cách tương tác Lantern. Compose là bộ công cụ UI native được Android khuyến nghị: [tài liệu chính thức](https://developer.android.com/compose).
- Lưu lịch sử bằng Room với payload/cache media được mã hóa; khóa được bọc bằng Android Keystore. Camera để quét QR; bộ giải mã QR đóng gói sẵn để ghép đôi offline. Media3 cho video, bộ tải ảnh có giới hạn kích thước/cache.
- PC bổ sung module companion độc lập với cổng peer hiện có, tận dụng LocalStore/PeerNode. Không đưa trực tiếp DLL WPF/DPAPI lên Android. Giao thức companion cần đặc tả version, capabilities, kích thước frame, giới hạn request và bộ fixture dùng chung .NET/Kotlin.
- Đồng bộ sự kiện theo cursor tăng dần, ID tin ổn định, request ID để gửi lại không sinh tin trùng; truyền file theo khối có SHA-256/resume. Tin gửi trên điện thoại hiện ngay trên PC và ngược lại.
- Đồng bộ quote, reaction, read receipt, presence, liên hệ, chính sách tự hủy và tombstone xóa. Chỉ báo đã đọc khi nội dung thực sự hiện trong hội thoại đã mở khóa. Ảnh chưa tải không kích hoạt đếm ngược. Tin đã hết hạn không xuất hiện lại khi điện thoại reconnect.

## Nhận tin khi tắt màn hình

Android giới hạn mạng nền trong Doze/App Standby, nên không thể hứa socket LAN luôn nhận tức thì khi app bị hệ thống ngủ. Bản LAN cần chế độ kết nối liên tục do người dùng bật, với thông báo thường trực khi một loại foreground service phù hợp được hệ điều hành cho phép; vẫn cần kiểm tra hạn chế phiên bản Android và hãng máy. Không dùng foreground service như cách bỏ qua mọi giới hạn nền. Tham khảo [Doze](https://developer.android.com/training/monitoring-device-state/doze-standby) và [foreground services](https://developer.android.com/develop/background-work/services/fgs).

Bản Internet có thể thêm push để đánh thức app rồi lấy nội dung qua kênh mã hóa; payload push chỉ là tín hiệu có dữ liệu mới. Việc này thêm hạ tầng push và không hoạt động khi hoàn toàn không có Internet. Mặc định toast vẫn tắt; hai chế độ nội dung/riêng tư giữ như PC.

## Các mốc bàn giao và kiểm thử

1. **Nền tảng PC:** module companion, QR một lần, duyệt/thu hồi thiết bị, đặc tả giao thức, test QR hết hạn/replay/sai khóa/thu hồi phiên đang chạy. Không ảnh hưởng kết nối peer của PC cũ.
2. **APK LAN:** quét QR, danh bạ/log, chat 2 chiều, ảnh tự nhận, file nhận thủ công, video, quote/react, số chưa đọc, khóa và thông báo. Kiểm thử PC thật + điện thoại thật, mất Wi-Fi, đổi IP, app restart, PC sleep, file gián đoạn.
3. **Đồng bộ hoàn chỉnh + VPN:** lịch sử phân trang, reconnect không trùng, tự hủy/đã đọc, khóa hội thoại, thu hồi thiết bị, chuyển Wi-Fi/4G; kiểm tra Doze, quyền thông báo/camera/LAN theo phiên bản Android mục tiêu.
4. **Phát hành:** CI build/test Android; ký APK bằng keystore phát hành riêng, giữ khóa ngoài Git; đưa APK, SHA-256 và release notes lên GitHub. Không tự cài APK khi chưa có thao tác xác nhận của Android. Bàn giao thêm AAB nếu cần Google Play; yêu cầu cửa hàng được kiểm tra tại thời điểm phát hành.
5. **Internet không VPN:** triển khai và kiểm thử rendezvous/relay sau khi bản LAN/VPN ổn định. Đo reconnect, độ trễ, tải media, chi phí relay và pin trước khi đưa ra cam kết vận hành.

Bản đầu ưu tiên Android; iOS sẽ cần đánh giá riêng cơ chế chạy nền/push. Phương án này chưa bao gồm cuộc gọi thoại/video hoặc chat độc lập khi PC tắt.

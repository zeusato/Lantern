# Lantern

Ứng dụng Windows để chat và chia sẻ ảnh, video, tệp trong mạng LAN IPv4. Chat không cần máy chủ trung tâm, tài khoản cloud hoặc Internet; không có telemetry. Mỗi bản app lắng nghe kết nối của các peer khác. Chức năng cập nhật tùy chọn kết nối GitHub Releases của `zeusato/Lantern`. Khi mở tab Link, app truy cập trang web công khai và ảnh Open Graph để hiện bản xem trước; không có Internet vẫn hiện đường dẫn.

## Chạy bản phát hành

1. Giải nén `Lantern-Windows-x64.zip` và mở `Lantern.exe` trên mỗi máy Windows 10/11 x64.
2. Đặt tên hiển thị và mật khẩu ứng dụng tối thiểu 8 ký tự. App bắt buộc nhập mật khẩu mỗi lần mở, không có tự đăng nhập. Danh tính mạng dùng cặp khóa ECDSA P-256.
3. Nếu Windows hỏi quyền mạng, chỉ cho phép app trên mạng **Private** đáng tin cậy. Không cần tắt firewall. Máy công ty cần chính sách cho phép peer-to-peer từ quản trị mạng.
4. Các máy cùng LAN xuất hiện trong danh sách. Nếu tự tìm không hoạt động, bấm **Kết nối bằng IP** và nhập IP + cổng hiện ở thanh trạng thái máy kia.
5. Chọn máy, bấm **Xác minh** trên cả hai bên, đối chiếu **toàn bộ sáu nhóm mã** trực tiếp hoặc qua kênh đã tin cậy. Chỉ xác nhận khi hai mã giống nhau. Mỗi bên xác nhận một lần.
6. Gửi tin, kéo thả tệp, hoặc bấm kẹp giấy. Ảnh từ đối tác đã ghép đôi tự tải và hiển thị trong log. Tệp và video vẫn cần bấm **Nhận tệp**.

Bản phát hành tự chứa .NET runtime. Không cần cài SDK, Visual Studio hoặc dịch vụ riêng. Không tự cài chứng thư tin cậy vào Windows và không tự sửa firewall.

## Chức năng

- Danh tính cố định từ SHA-256 của public key; IP được cập nhật khi kết nối lại.
- TLS 1.2/1.3 hai chiều, chứng thư tự ký được ghép đôi và kiểm tra public key.
- Tự tìm máy bằng UDP multicast/broadcast; nhập IPv4 và cổng thủ công.
- Chat riêng, nhóm cố định 3–32 thành viên, trạng thái đã nhận/đã đọc.
- Hàng đợi SQLite bền vững: gửi lại khi kết nối, chống tin trùng, định kỳ thử lại tin chưa ACK.
- Gửi tệp tối đa 10 GB, chia khối 64 KB, tiếp tục từ `.part`, xác minh SHA-256 trước khi hoàn tất.
- Tối đa 3 tệp nhận đồng thời; ảnh được tự động xếp hàng, tiếp tục tải khi kết nối lại và kiểm tra SHA-256. Tệp/video cần người dùng chủ động nhận.
- Ảnh hiển thị trong log, mở lớn; video phát/tạm dừng/tua trong log bằng codec Windows.
- Tìm người/nhóm và nội dung tin; tải thêm lịch sử; lưu nháp trong phiên theo cuộc trò chuyện.
- Giao diện tiếng Việt, sáng/tối/theo Windows, thích ứng cửa sổ tối thiểu 780×580.
- Sao lưu/khôi phục danh tính `.lanternkey` có mật khẩu, chặn và bỏ chặn máy.
- Mật khẩu ứng dụng bắt buộc; đăng nhập, khóa app và mở khóa hội thoại ngay trong cửa sổ chính, không tạo cửa sổ mật khẩu riêng. Ô mật khẩu tự focus; lời chào dùng tên tài khoản. Ctrl+L khóa ngay; đổi mật khẩu có xác nhận mật khẩu hiện tại.
- Mật khẩu riêng cho từng cuộc trò chuyện; mở một lần cho cả phiên, khóa chủ động bằng nút khóa ở đầu hội thoại. Khóa hoặc thoát ứng dụng khóa lại tất cả hội thoại.
- Sửa tên bản thân; đặt tên riêng cho đối tác chỉ trên máy mình, giữ nguyên khi đối tác đổi tên/IP.
- Số tin chưa đọc trên từng hội thoại, icon tray và badge trên taskbar, hiển thị `99+` khi vượt 99. Nhấp đúp tray để mở app.
- Tùy chọn **Đóng cửa sổ xuống khay hệ thống** trong Hồ sơ và cài đặt, mặc định tắt. Khi bật, nút X chỉ ẩn cửa sổ; app vẫn nhận tin và cập nhật badge. Nhấp đúp tray để mở lại, chọn **Thoát Lantern** trong menu tray để thoát hẳn. App đang khóa vẫn yêu cầu mật khẩu khi mở lại.
- Emoji màu Microsoft Fluent 3D đóng gói sẵn trong picker, log chat và reaction; tự đổi `:)`, `:D`, `;)`, `<3` khi kết thúc ký hiệu hoặc gửi. Có thể tắt tự đổi trong bảng emoji; URL, đường dẫn và khối mã được giữ nguyên. Sao chép tin nhắn vẫn giữ Unicode emoji.
- Bong bóng co theo nội dung, tin mình bên phải và tin người khác bên trái. Rê chuột để hiện nút trả lời và react; emoji/số lượt react nằm sát mép dưới bong bóng. Mỗi người một biểu cảm; bấm vào lượt react của mình để gỡ. Trả lời có trích dẫn và nhảy tới tin gốc.
- Ctrl+V dán ảnh clipboard thành ảnh PNG, gửi theo luồng nhận tệp thông thường.
- Toast Windows khi có tin mới: **mặc định tắt**, bật trong Hồ sơ và cài đặt. Chọn **Hiện người gửi và nội dung** hoặc **Chỉ báo có tin nhắn mới**. App/hội thoại bị khóa luôn dùng thông báo chung, không hiện tên hoặc nội dung; Windows có thể ẩn thông báo theo cấu hình hệ thống.
- Chấm trạng thái dưới tên: xanh online, xám offline, vàng khi máy không có thao tác 15 phút, đỏ Không làm phiền. Bấm chấm trạng thái ở thanh trái để đổi. Không làm phiền chặn toast tin nhắn, vẫn đếm tin chưa đọc.
- Bảng thông tin có Chia sẻ, Tìm kiếm và Cài đặt; Chia sẻ chia thành Media, Tệp và Link. Media là lưới thumbnail vuông; video dùng thumbnail Windows nếu codec hỗ trợ. Link có ảnh Open Graph ở trên và tiêu đề nếu trang cung cấp. Không tải preview cho địa chỉ LAN/nội bộ.
- Tin tự hủy: Cài đặt hội thoại → Tin nhắn tự hủy → chọn 10 giây đến 7 ngày. Chỉ áp dụng cho tin gửi sau khi đổi cài đặt; đếm từ lúc người nhận đọc (nhóm: lượt đọc đầu tiên), đồng bộ hạn xóa. Khi offline, hạn xóa được đồng bộ lại lúc kết nối; máy đang chạy kiểm tra hạn mỗi giây. Hai bên cần dùng bản 1.1 trở lên.
- Chia sẻ liên hệ đã ghép đôi bằng thẻ tên/IP/danh tính. Tên riêng không được gửi. Người nhận vẫn phải xác minh trước khi tin cậy.
- Nút kính lúp ở thanh trái tìm theo tên công khai qua danh bạ các máy đã ghép đôi đang online. Kết quả không tự cấp quyền tin cậy. Nếu chưa ai biết người cần tìm, dùng IP hoặc nhờ chia sẻ liên hệ.
- Tự kiểm tra GitHub Releases khi mở app và mỗi 6 giờ, tải bản mới trong nền và xác minh SHA-256. Bấm Cập nhật → Cài đặt và khởi động lại để thay phiên bản; không sửa workspace. Có thể tắt tự kiểm tra/tải.
- Quên mật khẩu: xóa dữ liệu và bắt đầu mới, có mã xác nhận riêng và xác thực Windows Hello bắt buộc.

## Mật khẩu và xóa workspace

Mật khẩu ứng dụng bọc khóa ngẫu nhiên 256 bit bằng PBKDF2-HMAC-SHA256 (600.000 vòng) và AES-256-GCM. Payload lịch sử/danh bạ/nhóm được mã hóa bằng khóa workspace. Mật khẩu hội thoại là lớp khóa xem cục bộ bổ sung; nó không tạo khóa mã hóa độc lập cho từng hội thoại và không áp dụng lên máy người bên kia.

Để đặt mật khẩu hội thoại, mở thông tin cuộc trò chuyện → **Cài đặt → Mật khẩu hội thoại**. Với cửa sổ nhỏ, bấm nút thông tin ở đầu hội thoại. **Khóa hội thoại ngay** hoặc nút khóa trên tiêu đề khóa tức thì. Chuyển hội thoại không làm mất trạng thái mở khóa trong phiên. Khi khóa, toàn bộ bảng thông tin, Media/Tệp/Link, tìm kiếm và cài đặt đều bị che và chặn tương tác, kể cả bảng thông tin ở cửa sổ nhỏ.

Nếu quên mật khẩu ứng dụng:

1. Ở màn hình khóa, chọn **Quên mật khẩu?**.
2. Nhập chính xác `XOA XXXXXXXX`, trong đó mã được lấy từ workspace hiện tại. Chỉ được đi tiếp khi đúng mã và đúng tài khoản Windows sở hữu.
3. Xác thực **Windows Hello/PIN/vân tay/khuôn mặt của tài khoản Windows đã tạo workspace** qua hộp thoại hệ thống.
4. Chỉ khi Windows trả kết quả Verified, app mới xóa dữ liệu Lantern của workspace đó và đưa về bước tạo mật khẩu mới.

Không có nút bỏ qua Windows Hello, câu hỏi bí mật hoặc mã xác nhận tĩnh chung. Hủy, thất bại, sai chủ sở hữu, thông tin chủ bị sửa hoặc Windows Hello không khả dụng đều không cho đi vào bước xóa. Luồng Hello desktop cần **Windows 11**. Trên Windows 10 hoặc máy chưa thiết lập Hello, reset bằng app bị khóa; không hạ xuống cách xác nhận kém hơn.

Xóa workspace xóa danh tính, danh bạ, lịch sử, khóa hội thoại, cấu hình và media do Lantern quản lý trong thư mục đó. Workspace mới có danh tính mới, nên cần ghép đôi lại. Tệp đã xuất ra thư mục khác và bản chat ở máy khác không bị xóa. Việc xóa là xóa file logic; không phải công cụ xóa dấu vết chuyên dụng trên ổ đĩa.

Nếu quên mật khẩu hội thoại, có luồng **Xóa lịch sử hội thoại** riêng với mã đúng hội thoại và Windows Hello. Chỉ lịch sử/media/mật khẩu của hội thoại đó trên máy bị xóa; danh bạ và hội thoại khác vẫn giữ. ID của tin đã xóa được giữ làm dấu chống tin cũ gửi lại.

Các khóa trong app chống người thao tác nhầm hoặc mở app khi không biết mật khẩu. Người có quyền quản trị hệ điều hành hoặc quyền trực tiếp sửa/xóa file vẫn có thể phá dữ liệu bên ngoài app; giao diện không thể ngăn thao tác đó.

## Lưu trữ và bảo mật

Dữ liệu mặc định ở `%LOCALAPPDATA%\Lantern`:

| Tệp/thư mục | Nội dung |
|---|---|
| `identity.bin` | Khóa riêng và chứng thư PFX được mã hóa bằng khóa workspace, rồi được Windows DPAPI bảo vệ theo tài khoản Windows |
| `workspace.json` | Khóa workspace được bọc bằng mật khẩu, salt/KDF và chứng thực chủ sở hữu Windows |
| `history.db` | Payload tin nhắn, danh bạ, nhóm được mã hóa; ID và thời gian vẫn là metadata |
| `conversation-locks.enc` | Bản ghi mật khẩu hội thoại đã băm, được mã hóa bằng khóa workspace |
| `media/` | Bản sao tệp gửi/nhận và phần tải dở |
| `settings.json` | Tên, theme, cổng |

**Media chưa mã hóa riêng khi nằm trên ổ đĩa.** Mật khẩu hội thoại khóa phần xem trong app, không khóa ứng dụng xem tệp bên ngoài. Tài khoản Windows và chính sách bảo vệ ổ đĩa quyết định ai đọc được media trực tiếp. DPAPI bảo vệ khóa định danh; AES-GCM bảo vệ payload lịch sử. Không lưu mật khẩu dạng rõ và không truyền mật khẩu cho peer.

Sao lưu danh tính chỉ giữ danh tính, không mang theo lịch sử/media hoặc mật khẩu workspace. Có thể đóng Lantern rồi sao chép cả thư mục dữ liệu để dự phòng trên cùng tài khoản Windows. Phiên bản này chưa có luồng di chuyển lịch sử mã hóa sang tài khoản Windows khác. Khi cài lại hoặc chuyển máy, tạo workspace mới và nhập `.lanternkey` để dùng lại danh tính mạng; lịch sử bắt đầu mới. Không chạy cùng một danh tính đồng thời trên nhiều máy. Đặt mật khẩu sao lưu dài và giữ tệp tách biệt với mật khẩu.

Các máy đã ghép đôi giữ tên liên hệ đã xác nhận. Đổi tên cục bộ không tự thay tên đã tin cậy trên máy khác.

## Mạng

- Discovery: UDP `45852`, multicast `239.255.42.99`, TTL 1, kèm broadcast trên các giao diện LAN.
- Chat/file: TCP `45853` mặc định. Nếu cổng bận, app chọn cổng trống và hiển thị cổng thực tế trên thanh trạng thái.
- Chỉ nhận/kết nối địa chỉ IPv4 private, link-local hoặc loopback. Chưa hỗ trợ IPv6 thuần, Internet hoặc NAT traversal.
- LAN có VLAN, client isolation hoặc multicast bị chặn có thể không tự tìm được máy. Thêm IP giúp khi định tuyến trực tiếp vẫn được phép; nó không vượt qua firewall.
- Discovery chưa được xác thực. **Không cấp quyền dựa trên tên hoặc IP trong gói discovery.** Chỉ chứng thư có public key phù hợp và quan hệ ghép đôi đã xác nhận mới được xử lý chat/file.

## Giới hạn

- Người gửi và nhận phải có lúc cùng online để chuyển tin/tệp. Không có peer giữ hộ khi cả hai không đồng thời online.
- Nhóm có danh sách thành viên cố định. Tạo nhóm mới nếu cần đổi danh sách. Tất cả thành viên cần ghép đôi trực tiếp với nhau. Chủ nhóm phải online để phân phối lời mời ban đầu cho từng người.
- Chưa có cuộc gọi thoại/video, đăng nhập tập trung, nhiều thiết bị cùng danh tính hoặc server chuyển tiếp.
- Video tải xong mới phát. Codec phụ thuộc Windows; không phải mọi định dạng đều xem được trong app. Có nút **Mở** bằng ứng dụng hệ thống.
- Ảnh GIF được hiển thị như ảnh tĩnh; định dạng ảnh không được Windows hỗ trợ có thể mở ngoài app.
- Đang phát video có thể dừng khi cuộc hội thoại được dựng lại do tin mới/theme; có thể bấm phát lại.
- Bản exe chưa ký bằng chứng thư phát hành thương mại. Không có SmartScreen reputation sẵn.
- Kiểm thử tự động và UI được thực hiện với các peer thật trên loopback cùng một máy Windows. Chưa xác minh mạng giữa hai máy vật lý, firewall/VLAN doanh nghiệp hoặc tải 32 người/10 GB.
- Bước Windows Hello dùng API thật của Windows; không tự nhập hoặc giả lập PIN/sinh trắc của người dùng trong kiểm thử. Các điều kiện chặn xóa được kiểm thử bằng dữ liệu QA.

## Build và kiểm thử

Cần .NET 10 SDK trên Windows x64. Không cần WinUI workload.

```powershell
dotnet build src/Lantern.App/Lantern.App.csproj -c Release
dotnet run --project tests/Lantern.Tests/Lantern.Tests.csproj -c Release
./build.ps1
```

`build.ps1` build, chạy kiểm thử giao thức thật trên loopback rồi publish một exe self-contained vào `dist/Lantern`.

Mở hồ sơ thử nghiệm tách biệt:

```powershell
./Lantern.exe --data C:\Temp\Lantern-A --local-test
```

`--local-test` chỉ bind loopback và tắt discovery. Chỉ dùng cho QA, không dùng để chat với máy khác.

## Cấu trúc mã nguồn

- `src/Lantern.Core`: danh tính, SQLite, khung giao thức, discovery, TLS, outbox, nhóm, truyền tệp.
- `src/Lantern.App`: WPF native, tài nguyên Fluent, cửa sổ chat, xem media và hộp thoại.
- `tests/Lantern.Tests`: chương trình kiểm thử tích hợp có exit code; không cần framework test bổ sung.
- `docs/PROTOCOL.md`: quy ước giao thức và ràng buộc tin cậy.

Android companion: cài `Lantern-Android.apk`, mở khóa PC → Cài đặt → Điện thoại → Ghép đôi QR. Đối chiếu mã rồi chấp nhận trên PC. PC cần chạy và không sleep; mạng Internet dùng relay riêng trên Sites, không cần VPN hay mở cổng router. Credential relay được cấu hình riêng, không chứa trong bản phát hành công khai. Chi tiết và giới hạn bản mobile đầu: [docs/MOBILE-PLAN.md](docs/MOBILE-PLAN.md).

Thiết kế tham khảo [WinUI App skill](https://github.com/openai/skills/tree/main/skills/.curated/winui-app), [Windows design](https://learn.microsoft.com/windows/apps/design/) và [WPF Fluent](https://learn.microsoft.com/dotnet/desktop/wpf/whats-new/net100). Runtime thực tế là WPF/.NET 10; không phụ thuộc WinUI 3.

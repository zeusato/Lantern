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

- **Game đối kháng trên PC**: bấm 🎮 trong chat riêng để mời chơi Caro, Bắn tàu, Nối chữ hoặc Cờ lật. Bàn chơi dùng hình vector tại máy; mỗi lượt chỉ cập nhật ô thay đổi, không dựng lại log chat. Khi chấp nhận, chat bên trái và game bên phải chia đều 1/1. Ván được lưu và tiếp tục sau mất kết nối; có xin hòa, đầu hàng và tái đấu. Android không hỗ trợ game và không nhận dữ liệu bàn chơi theo lượt. Xem [luật chơi và cách dùng](docs/CHAT-GAMES.md).

- **Xưởng chế tác sticker trên PC**: mở bằng nút sticker cạnh emoji hoặc trong Hồ sơ và cài đặt. Nhập nhiều PNG/GIF, đặt tên bộ và từng sticker; tìm kiếm không dấu, dùng gần đây, đổi tên/xóa/đổi thứ tự sticker. Tạo PNG trong suốt bằng vùng chọn chữ nhật hoặc vẽ tự do, xóa nền đơn sắc và thêm viền trắng. Gửi sticker động trong chat riêng/nhóm; xuất, gửi và nhập cả bộ qua tệp `.lanternstickers`. Thư viện đóng gói có 260 bộ/1.993 sticker lấy từ cache Zalo trên máy nguồn, gồm 1.404 ảnh động; các bộ chỉ có ảnh xem trước trong cache có thể chưa đầy đủ. Xem [chi tiết sticker](docs/STICKERS.md).

- Soạn thảo PC có thanh công cụ ngang; bấm **A✎** hoặc **Ctrl+Shift+X** để mở thanh định dạng phía dưới: đậm, nghiêng, gạch chân/ngang, cỡ/màu chữ, danh sách, thụt lề, hoàn tác/làm lại. Nút mở rộng tăng vùng nhập; ẩn thanh công cụ vẫn giữ định dạng. Rich text được giữ trong nháp theo hội thoại, lịch sử, gửi/nhận và chuyển tiếp trên PC; thiết bị chưa hỗ trợ vẫn đọc phần chữ thuần.
- Click phải ảnh đã tải trong log chat → **Sao chép ảnh** để dán ảnh gốc sang ứng dụng khác; giữ độ phân giải và độ trong suốt.
- Bình chọn nhóm trên PC: câu hỏi, 2–10 phương án, chọn một/nhiều, thời hạn tùy chọn, đổi/gỡ phiếu và xem người đã chọn. Người tạo kết thúc poll; phiếu được lưu chờ nếu máy người tạo offline và xác nhận khi kết nối lại. Xem [soạn thảo và bình chọn](docs/CHAT-COMPOSER-POLLS.md).

- Ra lệnh từ Android/PWA đã ghép đôi: khóa/tắt Windows, duyệt và tải tệp trong thư mục được cấp, gửi prompt vào AI đang mở (thử nghiệm). Cần cấp quyền riêng trên PC; xem [hướng dẫn và giới hạn](docs/PC-COMMANDS.md).

- Danh tính cố định từ SHA-256 của public key; IP được cập nhật khi kết nối lại.
- Ảnh đại diện trong **Hồ sơ và cài đặt**: chọn ảnh, kéo vùng cắt vuông 1:1, phóng to hoặc xoay rồi lưu. Ảnh được thu nhỏ thành PNG 192 × 192 và đồng bộ riêng qua các kết nối đã ghép đôi; có thể gỡ ảnh để dùng chữ viết tắt.
- TLS 1.2/1.3 hai chiều, chứng thư tự ký được ghép đôi và kiểm tra public key.
- **Danh bạ trên PC** gồm **Bạn bè** (máy đã xác minh) và **Trong LAN** (máy chưa xác minh, lưu IP/cổng để tìm lại). Tìm theo tên hoặc tiền tố IP. Máy được phát hiện và bạn bè chưa có lịch sử không tự xuất hiện trong **Trò chuyện**; xóa hội thoại vẫn giữ bạn bè trong Danh bạ, tin mới sẽ đưa hội thoại trở lại.
- Tự tìm máy bằng UDP multicast/broadcast; nút **Tìm máy trong LAN** mở màn quét riêng, có tiến độ, kết quả và nút dừng. Chỉ cần nhập `10.10` để dò lần lượt các dải `10.10.0.x` đến `10.10.255.x`, ưu tiên dải máy hiện tại; nhập `10.10.14` để quét một dải. Vẫn hỗ trợ IP đầy đủ và nhiều CIDR phân cách bằng dấu phẩy, tối đa 65.536 địa chỉ. Quét trực tiếp UDP/TCP, hiển thị dải đang dò; toàn bộ /16 có thể mất hơn 10 phút. Router/firewall phải cho phép kết nối giữa các dải. Nút **Sao chép IP của tôi** cạnh **Kết nối bằng IP** sao chép `IP:cổng`; máy nhận có thể dán nguyên vào ô địa chỉ.
- **Hồ sơ và cài đặt → Xuất toàn bộ dữ liệu để chuyển máy** tạo một file `.lanternbackup` mã hóa bằng mật khẩu ít nhất 12 ký tự, gồm danh tính, lịch sử, danh bạ, nhóm, cài đặt, sticker và tệp đính kèm hiện có. Trên máy mới chưa tạo workspace, chọn **Khôi phục dữ liệu từ máy cũ** ở màn đăng nhập; mật khẩu sao lưu trở thành mật khẩu mở app, mật khẩu hội thoại được giữ nguyên. Tệp chưa tải/đang tải cần nhận lại; điện thoại cần ghép đôi lại. Đóng app trên máy cũ sau khi chuyển để tránh chạy trùng danh tính.
- Bảng thông tin bên phải ẩn mặc định khi chọn hoặc đổi hội thoại; bấm nút **Thông tin cuộc trò chuyện** để mở.
- Chat riêng, tạo nhóm 3–32 thành viên, trạng thái đã nhận/đã đọc. Chủ nhóm hiện tại có thể thêm thành viên trong **Thông tin → Cài đặt → Thêm thành viên**, chọn cho người mới đọc lịch sử còn lưu hoặc chỉ nhận tin từ lúc tham gia.
- Thành viên có thể rời nhóm và giữ lịch sử đã nhận ở chế độ chỉ đọc. Chủ nhóm rời sẽ chuyển quyền cho người được chỉ định hoặc chọn ngẫu nhiên người còn lại. Chủ nhóm có quyền xóa nhóm trên mọi máy; máy offline thực hiện khi kết nối lại. Thành viên cuối cùng rời sẽ xóa nhóm. Tệp đã lưu riêng bên ngoài Lantern không bị xóa.
- Hội thoại online xếp trước offline; trong mỗi phần ưu tiên hội thoại đã ghim rồi tới tin mới nhất. Click phải hội thoại để ghim, phân loại, thêm người vào nhóm hoặc xóa lịch sử cục bộ và ẩn hội thoại tới khi có tin mới. Chọn hội thoại tự focus ô nhập.
- Click phải tin nhắn để trả lời, chia sẻ sang log khác, copy, ghim trên máy mình, chọn nhiều hoặc xóa chỉ ở phía mình. Danh sách tin ghim nằm trong **Thông tin → Cài đặt**. Chuyển tiếp hỗ trợ chữ, liên hệ và tệp; tệp chưa nhận được tải trước khi chuyển tiếp.
- **Thông tin → Cài đặt → Quyền hội thoại** đồng bộ quyền chuyển tiếp và chống chụp cho các bên; nhóm chỉ chủ nhóm được sửa. Trong chat hai người, mỗi quyền có người thiết lập riêng: chỉ người bật hạn chế được gỡ, trả về mặc định thì cả hai lại được đổi. Cả hai PC cần bản hỗ trợ protocol 7 để chỉnh quyền theo cơ chế này. Hai yêu cầu hạn chế phát sinh đồng thời được giữ riêng, mỗi người chỉ gỡ phần mình đặt. Chống chụp dùng API Windows cho toàn cửa sổ khi đang mở log đó, gồm cửa sổ xem ảnh/HTML; nếu API không bật được thì ẩn nội dung. Android dùng cửa sổ bảo vệ sẵn có. PWA vẫn cho đọc/gửi tin và chỉ báo không hỗ trợ chống chụp; nội dung hiển thị trên PWA có thể bị chụp lại. Thiết bị native cũ chưa hỗ trợ vẫn bị chặn nội dung. Không bảo đảm chặn camera ngoài hoặc ứng dụng đã bị sửa đổi; quyền mới áp dụng sau khi máy offline kết nối lại.
- Hàng đợi SQLite bền vững: gửi lại khi kết nối, chống tin trùng, định kỳ thử lại tin chưa ACK.
- Gửi tệp tối đa 10 GB, chia khối 64 KB, tiếp tục từ `.part`, xác minh SHA-256 trước khi hoàn tất.
- Tối đa 3 tệp nhận đồng thời; ảnh được tự động xếp hàng, tiếp tục tải khi kết nối lại và kiểm tra SHA-256. Tệp/video cần người dùng chủ động nhận.
- Ảnh hiển thị trong log, mở lớn; video phát/tạm dừng/tua trong log bằng codec Windows.
- Link HTTP/HTTPS và `www.` trong tin nhắn bấm mở bằng trình duyệt mặc định; vẫn chọn và sao chép được nguyên văn.
- Bấm **Mở HTML** trên tệp `.html`/`.htm` để tự tải ngầm, kiểm tra toàn vẹn rồi chạy HTML/CSS/JavaScript ngay trong khung xem. Tối đa 16 MB, cần Microsoft Edge WebView2 Runtime. Khung xem dùng hồ sơ riêng, không cung cấp dữ liệu chat hoặc thư mục máy; tài nguyên ngoài chỉ tải khi bật **Tải tài nguyên Internet**. Tệp đi kèm ở thư mục khác không tự được chia sẻ.
- Tìm người/nhóm và nội dung tin; tải thêm lịch sử; lưu nháp trong phiên theo cuộc trò chuyện.
- Giao diện tiếng Việt, sáng/tối/theo Windows, thích ứng cửa sổ tối thiểu 780×580.
- Sao lưu/khôi phục danh tính `.lanternkey` có mật khẩu, chặn và bỏ chặn máy.
- Mật khẩu ứng dụng bắt buộc; đăng nhập, khóa app và mở khóa hội thoại ngay trong cửa sổ chính, không tạo cửa sổ mật khẩu riêng. Ô mật khẩu tự focus; lời chào dùng tên tài khoản. Ctrl+L khóa ngay; đổi mật khẩu có xác nhận mật khẩu hiện tại.
- Android dùng mật khẩu tài khoản Lantern trên PC khi mở lại sau khi đóng hẳn app hoặc khóa chủ động; ghép đôi không tự đăng nhập. Cần PC đang chạy và cập nhật cả hai ứng dụng. Chuyển ứng dụng giữ phiên mở khóa. Điện thoại không lưu mật khẩu; trước đăng nhập không hiện danh sách chat hay dữ liệu cache.
- Mật khẩu riêng cho từng cuộc trò chuyện; mở một lần cho cả phiên, khóa chủ động bằng nút khóa ở đầu hội thoại. Khóa hoặc thoát ứng dụng khóa lại tất cả hội thoại.
- Sửa tên bản thân; đặt tên riêng cho đối tác chỉ trên máy mình, giữ nguyên khi đối tác đổi tên/IP.
- Số tin chưa đọc trên từng hội thoại, icon tray và badge trên taskbar, hiển thị `99+` khi vượt 99. Nhấp đúp tray để mở app.
- Tùy chọn **Đóng cửa sổ xuống khay hệ thống** trong Hồ sơ và cài đặt, mặc định tắt. Khi bật, nút X chỉ ẩn cửa sổ; app vẫn nhận tin và cập nhật badge. Nhấp đúp biểu tượng ở khay hoặc mở Lantern từ lối tắt để quay lại phiên đang chạy. Chọn **Thoát Lantern** trong menu khay để thoát hẳn. App đang khóa vẫn yêu cầu mật khẩu khi mở lại.
- Emoji màu Microsoft Fluent 3D đóng gói sẵn trong picker, log chat và reaction; tự đổi `:)`, `:D`, `;)`, `<3` khi kết thúc ký hiệu hoặc gửi. Có thể tắt tự đổi trong bảng emoji; URL, đường dẫn và khối mã được giữ nguyên. Sao chép tin nhắn vẫn giữ Unicode emoji.
- Bong bóng co theo nội dung, tin mình bên phải và tin người khác bên trái. Rê chuột để hiện nút trả lời và react; vùng hover rộng và khoảng chờ một giây giúp di chuyển sang bảng cảm xúc. Tooltip dưới số react hiển thị tối đa năm tên; bấm mở danh sách đầy đủ, lọc theo emoji. Gỡ bằng nút **Gỡ cảm xúc của bạn** trong bảng chọn hoặc danh sách. Mỗi người một biểu cảm. Trả lời có trích dẫn và nhảy tới tin gốc.
- Ctrl+V dán ảnh clipboard thành ảnh PNG, gửi theo luồng nhận tệp thông thường.
- Toast Windows khi có tin mới: **mặc định bật, chỉ báo có tin nhắn mới**; giữ lựa chọn đã lưu của người dùng. Trong Hồ sơ và cài đặt có thể chọn hiện người gửi/nội dung hoặc tắt. App/hội thoại bị khóa hoặc bật chống chụp luôn dùng thông báo chung; Windows có thể ẩn thông báo theo cấu hình hệ thống.
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
| `stickers/` | Thư viện sticker, tên bộ/từng sticker và ảnh PNG/GIF cục bộ |

**Media chưa mã hóa riêng khi nằm trên ổ đĩa.** Mật khẩu hội thoại khóa phần xem trong app, không khóa ứng dụng xem tệp bên ngoài. Tài khoản Windows và chính sách bảo vệ ổ đĩa quyết định ai đọc được media trực tiếp. DPAPI bảo vệ khóa định danh; AES-GCM bảo vệ payload lịch sử. Không lưu mật khẩu dạng rõ và không truyền mật khẩu cho peer.

Sao lưu danh tính chỉ giữ danh tính, không mang theo lịch sử/media hoặc mật khẩu workspace. Có thể đóng Lantern rồi sao chép cả thư mục dữ liệu để dự phòng trên cùng tài khoản Windows. Phiên bản này chưa có luồng di chuyển lịch sử mã hóa sang tài khoản Windows khác. Khi cài lại hoặc chuyển máy, tạo workspace mới và nhập `.lanternkey` để dùng lại danh tính mạng; lịch sử bắt đầu mới. Không chạy cùng một danh tính đồng thời trên nhiều máy. Đặt mật khẩu sao lưu dài và giữ tệp tách biệt với mật khẩu.

Các máy đã ghép đôi giữ tên liên hệ đã xác nhận. Đổi tên cục bộ không tự thay tên đã tin cậy trên máy khác.

## Mạng

- Discovery: UDP `45852`, multicast `239.255.42.99`, TTL 1, kèm broadcast theo subnet trên từng giao diện LAN. Thành viên multicast được cập nhật khi địa chỉ mạng thay đổi; lỗi gửi multicast không ngăn broadcast.
- Nút tìm máy dò UDP unicast để lấy cổng thực tế và thử TCP `45853` cho máy cũ hoặc mạng chặn UDP. Chỉ dò subnet đang kết nối; subnet lớn giới hạn ở vùng `/22` chứa IP hiện tại, tối đa 4096 địa chỉ, 32 kết nối TCP song song và 25 giây. Máy chạy bản cũ ở cổng tùy chỉnh có thể vẫn cần thêm IP/cổng thủ công.
- Chat/file: TCP `45853` mặc định. Nếu cổng bận, app chọn cổng trống và hiển thị cổng thực tế trên thanh trạng thái.
- Chỉ nhận/kết nối địa chỉ IPv4 private, link-local hoặc loopback. Chưa hỗ trợ IPv6 thuần, Internet hoặc NAT traversal.
- LAN có VLAN, client isolation hoặc multicast bị chặn có thể không tự tìm được máy. Thêm IP giúp khi định tuyến trực tiếp vẫn được phép; nó không vượt qua firewall.
- Discovery chưa được xác thực. **Không cấp quyền dựa trên tên hoặc IP trong gói discovery.** Chỉ chứng thư có public key phù hợp và quan hệ ghép đôi đã xác nhận mới được xử lý chat/file.

## Giới hạn

- Người gửi và nhận phải có lúc cùng online để chuyển tin/tệp. Không có peer giữ hộ khi cả hai không đồng thời online.
- Chỉ chủ nhóm hiện tại được thêm thành viên; thành viên có thể tự rời nhóm, chưa hỗ trợ chủ nhóm loại riêng một thành viên. Tất cả thành viên cần phiên bản hỗ trợ thêm người và ghép đôi trực tiếp với nhau. Chủ nhóm phân phối danh sách mới khi online; lịch sử được đồng bộ từ từng người gửi khi họ kết nối lại. Tin đã xóa/hết hạn không được khôi phục. Quyền lịch sử của mỗi thành viên được giữ nguyên qua các lần thêm người sau.
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

Android companion: cài `Lantern-Android.apk`, mở khóa PC → Cài đặt → Điện thoại → Ghép đôi QR. Đối chiếu mã rồi chấp nhận trên PC. PC cần chạy và không sleep; mạng Internet dùng relay riêng trên Sites, không cần VPN hay mở cổng router. Từ 1.2.7, relay được thiết lập tự động với khóa riêng cho mỗi PC; người dùng không nhập cấu hình và bộ cài không chứa khóa quản trị. Chi tiết và giới hạn bản mobile đầu: [docs/MOBILE-PLAN.md](docs/MOBILE-PLAN.md).

iPhone dùng PWA bên dưới; release không còn kèm IPA. Mã native SwiftUI cũ vẫn giữ trong `ios/` để tham khảo.

Thiết kế tham khảo [WinUI App skill](https://github.com/openai/skills/tree/main/skills/.curated/winui-app), [Windows design](https://learn.microsoft.com/windows/apps/design/) và [WPF Fluent](https://learn.microsoft.com/dotnet/desktop/wpf/whats-new/net100). Runtime thực tế là WPF/.NET 10; không phụ thuộc WinUI 3.

### iPhone dùng bản web

Từ 1.2.5 có PWA tối ưu mobile: cài qua Safari, ghép QR với PC, không cần ký IPA. Xem [hướng dẫn Lantern Web](docs/WEB-MOBILE.md). PC/PWA có 5 bảng màu, xóa từng/nhiều tin và dọn hội thoại; PC có tùy chọn khởi động cùng Windows mặc định bật.

Ứng dụng kiểm tra cập nhật lúc mở và mỗi 6 giờ khi bật tự cập nhật. Tải và kiểm tra gói trong nền, báo khi đã sẵn sàng; chỉ cài sau khi người dùng bấm xác nhận. **Bỏ qua · Giữ bộ cài** giữ gói đã tải, kể cả mở lại app khi offline. Gói cache phải vượt kiểm tra SHA-256 trước khi dùng.

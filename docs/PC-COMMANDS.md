# Ra lệnh PC từ điện thoại

Tính năng có từ Lantern 1.2.9, dành cho Windows và điện thoại đã ghép đôi. Android có mục **Máy tính** trong menu; PWA có mục **Máy tính** ở thanh điều hướng. Phần tích hợp AI vẫn là tính năng thử nghiệm.

## Bật và sử dụng

1. Chạy bản PC mới, mở khóa Lantern. Vào **Cài đặt → Điện thoại**, chọn **Quyền ra lệnh và lấy tệp…** dưới đúng thiết bị.
2. Bật riêng quyền khóa/tắt Windows, quyền AI và chọn các thư mục được đọc. Ghép đôi không tự cấp những quyền này. Có thể cấp cả ổ dữ liệu cục bộ nếu muốn duyệt từ gốc ổ.
3. Trên điện thoại đang kết nối, mở **Máy tính**. Lệnh khóa/tắt máy và gửi prompt đều có bước xác nhận. Khi mất kết nối, kiểm tra kết quả thay vì gửi lại lệnh mới.

Khóa/tắt máy dùng API Windows và `shutdown.exe /s /t 0`, không cưỡng ép đóng ứng dụng có tài liệu chưa lưu. Chức năng không cung cấp chuột, màn hình từ xa hay lệnh shell tùy ý.

## Lấy tệp

Chỉ liệt kê thư mục đang mở, tối đa 100 mục/trang; người dùng mở dần cây và chọn tệp. Không quét đệ quy cả máy. Mỗi tệp tối đa 100 MB, tải theo khối 256 KB và xác minh SHA-256 trước khi cho lưu. Android lưu qua bộ chọn nơi lưu; PWA dùng tải xuống hoặc Chia sẻ/Lưu vào Tệp. Giữ PWA ở phía trước trong lúc tải.

Không hỗ trợ UNC, ổ mạng, junction/symlink, tệp hệ thống, ADS hoặc đường dẫn thoát khỏi gốc được cấp. PC giữ tệp chỉ đọc và chặn thay đổi trong lúc tải; đóng phiên sẽ thả handle. Phiên bỏ dở hết hạn sau 2 phút không hoạt động, bộ dọn chạy mỗi 30 giây kể cả khi điện thoại không gửi thêm yêu cầu. Tối đa 2 lượt tải/thiết bị, 8 lượt toàn PC. Mỗi khối kiểm tra lại quyền thư mục.

## AI đang mở — thử nghiệm

- Tìm Claude hoặc ChatGPT/Codex đang chạy, khôi phục cửa sổ thu nhỏ/ẩn bằng Windows, rồi đọc UI Automation. Không chụp màn hình và không phát sinh phiên screen capture.
- Nếu app chỉ còn tiến trình tray và không có cửa sổ hợp lệ, trả trạng thái chưa xác định; cần mở từ tray trên PC. Windows đang khóa cũng không cho thao tác AI.
- Phân biệt sẵn sàng, đang làm việc, chờ xác nhận, có bản nháp và chưa xác định. Chỉ trạng thái sẵn sàng mới cho gửi. Không tự bấm duyệt quyền hoặc xóa bản nháp.
- Hội thoại mới chưa có tên vẫn có thể sẵn sàng. Lantern đọc đúng vùng chứa ô nhập hiện tại; tên lớp vỏ ứng dụng và nút dừng bị vô hiệu hóa không quyết định trạng thái chat.
- Lượt kiểm tra có giới hạn 8 giây trên PC, giữ kết quả đã đọc được nếu ứng dụng khác phản hồi chậm. Android hiện tiến trình, chặn bấm lặp và cho kiểm tra lại sau lỗi hoặc khi quay lại màn hình.
- Đọc trạng thái hai lần và kiểm tra lại cửa sổ/cuộc trò chuyện ngay trước khi gửi. Đặt prompt bằng ValuePattern và gọi nút Send bằng InvokePattern, không dùng tọa độ chuột hay gõ phím toàn cục. Windows có thể không cho giành focus; adapter vẫn nhắm trực tiếp đúng control trong cửa sổ đã hiện.
- Mã nhận diện gồm tiến trình, thời điểm khởi động, HWND, runtime ID của vùng chat/ô nhập và địa chỉ hội thoại khi ứng dụng cung cấp; địa chỉ chỉ được băm để so khớp, không trả về điện thoại. Nếu không có địa chỉ, dùng tên và runtime ID tiêu đề làm dự phòng. Đây không phải API thread ID chính thức: trong trường hợp dự phòng, hai cuộc trò chuyện trùng tên và dùng lại control có thể không phân biệt được. Tránh đổi cuộc trò chuyện trên PC trong lúc xác nhận/gửi từ điện thoại.
- Sau lần gửi đầu tiên, app AI có thể tạo tên/địa chỉ mới. Lantern chỉ xác nhận khi vẫn ở cùng vùng chat, ô nhập đã trống và tìm thấy đúng nội dung vừa gửi; không tự gửi lại khi kết quả chưa rõ.
- UI và nhãn điều khiển của ứng dụng AI có thể thay đổi. Phiên bản không nhận diện được sẽ từ chối. Không hỗ trợ tab AI trong trình duyệt.
- Nếu giao diện đổi sau khi điền, prompt có thể còn trong ô nhập trên PC mà chưa gửi. Nếu không xác nhận được sau khi bấm Send, báo kết quả chưa rõ và không tự gửi lại.

## Giao thức

Capability `pc-v1` trên kênh điện thoại đã xác thực/mã hóa. Packet `{type:"pc", pc:{action:…}}`; kênh này độc lập với hàng đợi chat:

| Action | Mục đích |
| --- | --- |
| `status` | Quyền và gốc được cấp, không lộ đường dẫn vật lý |
| `ai.inspect` | Hiện cửa sổ và đọc trạng thái/cuộc trò chuyện |
| `prepare` | Chuẩn bị `power.lock`, `power.shutdown` hoặc `ai.send` |
| `execute` | Thực thi ticket một lần |
| `result` | Đọc kết quả ticket, không thực thi lại |
| `files.list` | Liệt kê một trang trong thư mục |
| `files.open` | Mở phiên tải, trả tên/kích thước/checksum/ticket |
| `files.read` | Đọc một khối tại offset |
| `files.close` | Đóng phiên tải |

Ticket lệnh ngẫu nhiên, gắn với thiết bị và tham số lưu trên PC, hết hạn sau 45 giây theo đồng hồ PC. Ticket bị tiêu thụ trước tác động; yêu cầu lặp chỉ nhận cùng kết quả. Khởi động lại PC làm mất ticket. Không lưu lệnh vào outbox, không tự phát lại khi mạng hồi phục. Mọi lệnh kiểm tra lại quyền và trạng thái khóa Lantern. Deadline thực thi 12 giây; kết quả không chắc chắn vẫn là kết quả cuối cho ticket đó.

## Kiểm chứng

- `dotnet run --project tests/Lantern.Tests -c Release`: kiểm thử quyền, ticket một lần, hết hạn/thu hồi, chống thoát cây, checksum, tải phân khối và dọn handle. Lệnh khóa/tắt máy dùng adapter giả, không tắt máy thật.
- `dotnet run --project tests/PcDesktopProbe -c Release -- --fixture`: kiểm thử bộ đọc UI Automation với cửa sổ và hội thoại giả lập riêng; không tác động ứng dụng AI thật.
- `npm test --prefix pwa`: unit tests, gồm client PC không phát lại lệnh.
- `pwsh scripts/Test-Web.ps1`: relay và WebCrypto/.NET interop, gồm lệnh PC/tải tệp trên phiên ghép đôi thử.
- `cd android; ./gradlew.bat assembleDebug lintDebug`: APK debug và Android lint.
- `node pwa/tests/pc-ui.mjs` từ gốc repo: kiểm thử giao diện Chrome ở 390×844 với dữ liệu giả. Cần Playwright và Chrome; có thể đặt `LANTERN_PLAYWRIGHT` thành file URL của module Playwright, `LANTERN_BROWSER_CHANNEL` chọn channel và `LANTERN_UI_SCREENSHOT` chọn đường dẫn ảnh đầu ra.
- `dotnet run --project tests/PcDesktopProbe -c Release -- --minimized`: probe thủ công dùng adapter thật, thu nhỏ/khôi phục các cửa sổ AI đang mở và đọc trạng thái. Không gửi prompt hoặc chụp màn hình. `--diagnostics` bổ sung cấu trúc document/toolbar, các nút quanh ô nhập và thuộc tính ô nhập; không in nội dung bản nháp.

Đã kiểm tra trạng thái trên Claude và ChatGPT/Codex thật, kiểm thử UI PWA và kết nối mã hóa bằng fixture. Chưa kiểm thử toàn tuyến gửi prompt từ điện thoại qua adapter sản xuất vào AI thật; chưa thử trên Android/iPhone vật lý. Không coi việc build thành công là bằng chứng đã kiểm thử các trường hợp này.

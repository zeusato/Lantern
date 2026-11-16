# Kiểm chứng tốc độ Lantern 1.2.2

Ngày đo: 16/11/2026. Dữ liệu tổng hợp do bộ thử tạo, không dùng hội thoại thật.

## Kết quả qua relay Internet

Hai đầu TLS thử nghiệm chạy trên cùng PC ở Việt Nam nhưng **bắt buộc đi qua relay Sites đã triển khai**, không kết nối tắt loopback. Đối tác LAN thử nghiệm cũng chạy trên máy này. Không bao gồm thời gian quét QR/duyệt ghép đôi hoặc vẽ giao diện điện thoại thật.

| Lượt | Companion → lưu trên PC | PC xác nhận về companion | Đối tác LAN → PC → push xuống companion |
| --- | ---: | ---: | ---: |
| 1 | 654 ms | 996 ms | 1079 ms |
| 2 | 521 ms | 971 ms | 664 ms |
| 3 | 440 ms | 994 ms | 686 ms |
| 4 | 353 ms | 651 ms | 1056 ms |
| 5 | 590 ms | 974 ms | 1010 ms |

Một lần đồng bộ lịch sử có ít nhất 30 tin: **1283 ms**. Các phép đo gửi/nhận này đều dưới 2 giây. Năm mẫu không phải cam kết độ trễ tối đa trên mọi mạng; vẫn cần kiểm tra Wi-Fi/4G của người dùng và chế độ tiết kiệm pin Android.

## Những thay đổi ảnh hưởng trực tiếp

- UI thêm tin vào hội thoại ngay sau khi bấm gửi; dữ liệu được lưu mã hóa và gửi nền. ACK mới xác nhận PC đã lưu, không coi hiển thị cục bộ là đã giao tới đối tác.
- PC đẩy thay đổi ngay khi phát sinh; điện thoại không cần chờ vòng hỏi lịch sử tiếp theo. Các phản hồi có số thứ tự để dữ liệu cũ không ghi đè trạng thái mới.
- Lệnh gửi/khóa/react có thể truyền trong khi đang chờ các khối ảnh; request ID ghép đúng từng phản hồi.
- Một yêu cầu đồng bộ chứa danh bạ, lịch sử và ACK đã đọc. Dữ liệu không đổi chỉ trả hash. Lịch sử đã tải được mở lại từ cache.
- Relay tách luồng đẩy/lấy, gộp bản ghi TLS và xử lý xác thực/ACK/ghi/đọc trong một giao dịch D1. Trước sửa, riêng HTTP đẩy/lấy tuần tự mất khoảng 1,81 + 1,24 giây, chưa tính toàn bộ một lệnh chat.

## Tái kiểm tra

`tests/Lantern.Tests/MobileLatencyTests.cs` đo riêng thời gian PC lưu tin, ACK và push chiều về; bài LAN đặt ngưỡng 2000 ms. Chế độ `--mobile-relay` của bộ core nhận cấu hình relay qua stdin, không qua tham số lệnh hoặc Git. Chỉ chạy với relay được cấp quyền.

`CompanionFlowTest` trên Android kiểm tra bấm gửi khi bàn phím mở, 30 tin có sẵn và ảnh nền 8 MB đang tải; tin xuất hiện cục bộ, ACK/push không phải chờ ảnh, bấm lại ô rỗng không gửi trùng. Các bài riêng kiểm tra cache mã hóa, khóa hội thoại, phục hồi hàng đợi và loại tin tự hủy khỏi lưu trữ bền vững.

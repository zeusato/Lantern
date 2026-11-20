# Game trong chat riêng

Game chỉ hỗ trợ trên PC: Caro, Bắn tàu và Nối chữ. Hai PC cần bản hỗ trợ game (peer protocol 6). Android không có bàn chơi, không nhận trạng thái game hoặc dữ liệu đội tàu. Chưa triển khai Cờ lật hoặc Đoán mã.

## Trải nghiệm

- Bấm 🎮 trong chat riêng, chọn game và gửi lời mời. Mỗi hội thoại có tối đa một lời mời hoặc ván đang chơi.
- Chấp nhận lời mời mới chia vùng hội thoại thành hai cột bằng nhau: chat bên trái, game bên phải. Thanh thông tin ẩn; vẫn gửi tin và tệp bình thường.
- Thu gọn bàn chơi khôi phục chiều rộng chat; mở lại bằng 🎮. Đổi hội thoại không hủy ván.
- Có đầu hàng, đề nghị hòa, từ chối hòa và tái đấu đổi người đi trước. Các ván không giới hạn thời gian lượt.
- Khi mất kết nối, ván được lưu. Lượt đang gửi chờ xác nhận và được gửi lại sau kết nối; không tính thua vì mất mạng.

## Luật

**Caro:** bàn 15×15, từ năm quân liên tiếp theo hàng, cột hoặc đường chéo là thắng. Không áp dụng luật chặn hai đầu. Hết ô thì hòa.

**Bắn tàu:** bàn 10×10, năm tàu dài 5–4–3–3–2 ô, nằm thẳng và không chồng nhau. Xếp tay hoặc tự động, sau đó khóa đội tàu. Mỗi lượt bắn một ô, kể cả khi trúng cũng chuyển lượt. Hạ đủ 17 ô và xác minh đội tàu ban đầu để thắng.

**Nối chữ:** cụm gồm đúng hai tiếng; tiếng cuối của lượt trước là tiếng đầu của lượt sau. Không lặp cụm đã dùng; chuẩn hóa chữ hoa và khoảng trắng, giữ dấu tiếng Việt. Bộ từ điển nhỏ đóng gói cùng ứng dụng cho phép công nhận tự động; cụm chưa có cần đối thủ đồng ý, nếu từ chối người đưa từ vẫn giữ lượt. Tiếng bắt buộc được khóa sẵn trong ô nhập. Chuỗi được trình bày bằng thẻ hai tiếng, nhấn màu tiếng nối, phân bên theo người chơi và có lịch sử đầy đủ. Bí từ có thể đầu hàng hoặc xin hòa.

## Đồng bộ và dữ liệu

Người mời điều phối thứ tự sự kiện. Mỗi sự kiện có chữ ký của người thực hiện, số phiên bản và hash sự kiện trước; cả hai PC kiểm tra luật. Lượt trùng không được áp dụng lại. Bắn tàu dùng cam kết SHA-256 cho từng ô với salt ngẫu nhiên; chỉ công bố bằng chứng ô đã bắn, toàn bộ đội tàu được kiểm chứng cuối ván.

SQLite lưu lịch sử sự kiện, lượt chờ và đội tàu riêng trong dữ liệu được mã hóa bằng vault. Export/import workspace đã bao gồm dữ liệu này. Xóa thẻ game cũng xóa dữ liệu ván cục bộ; tombstone ngăn đồng bộ làm sống lại ván. Hội thoại khóa che bàn chơi trên PC. Sự kiện game dùng kênh thông báo riêng trong ứng dụng, không kích hoạt push đồng bộ chat tới điện thoại theo từng lượt. Android chỉ có thể thấy dòng lời mời dạng tin nhắn thông thường trong lịch sử chat.

## Kiểm thử

- `dotnet run --project tests/Lantern.Tests -- --games-only`: hai PC TLS thật, luật ba game, ký sự kiện, ẩn đội tàu, trận hoàn chỉnh, lưu/khôi phục và gửi lại sau mất kết nối.
- `dotnet run --project tests/Lantern.Tests -- --mobile-only`: xác nhận lượt game PC không làm đổi dữ liệu lịch sử điện thoại, không có trạng thái game và không chấp nhận thao tác game từ điện thoại.
- `dotnet run --project tests/Lantern.UiTests -- --games`: tỷ lệ 1/1, chat đồng thời, đổi hội thoại, thu gọn, khóa và ảnh ba game.

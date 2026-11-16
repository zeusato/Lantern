# Phát hành và cập nhật

Repo: https://github.com/zeusato/Lantern

Mỗi lần đẩy tag `vMAJOR.MINOR.PATCH`, workflow `Windows release` chạy kiểm thử trên Windows, build portable tự chứa .NET 10 và tạo GitHub Release với `Lantern-Windows-x64.zip` cùng `SHA256SUMS.txt`. Tag là bản phát hành ổn định, không dùng prerelease trong kênh cập nhật hiện tại.

Để phát hành bản tiếp theo:

1. Đổi `<Version>` trong `src/Lantern.App/Lantern.App.csproj`, cập nhật ghi chú và commit thay đổi.
2. Đẩy `main`, đợi workflow `Windows checks` thành công.
3. Tạo tag cùng phiên bản, ví dụ `git tag v1.1.1`, rồi `git push origin v1.1.1`.
4. Kiểm tra workflow release hoàn tất và trang Releases có đủ hai asset. Không ghi đè hoặc di chuyển tag đã phát hành.

Build gói cục bộ: `./scripts/Package-Release.ps1 -Version 1.1.1`. Script chạy test trước khi publish. Thư mục `release-output` không được commit. CI pin action theo commit SHA và chỉ cấp `contents: write` cho job release. Không cần PAT lưu trong repo: upload dùng token của GitHub Actions.

App đọc `/repos/zeusato/Lantern/releases/latest`, chỉ nhận version mới hơn, asset đúng tên từ đúng repo và digest SHA-256 do GitHub cung cấp. Gói sai kích thước/hash, thiếu digest, có đường dẫn lạ, file trùng hoặc symlink đều bị từ chối. Chat LAN vẫn hoạt động khi không có Internet; cập nhật báo lỗi và có thể thử lại.

Tải tự động không tự đóng app. Người dùng chọn **Cài đặt và khởi động lại**. Helper được nhúng sẵn trong app chờ tiến trình thoát, sao lưu các file ứng dụng, thay file, khởi động lại đúng workspace; lỗi sao chép khôi phục bản cũ. Chỉ hỗ trợ cập nhật trực tiếp bản portable `Lantern.exe` trong thư mục người dùng có quyền ghi. Cache và log cập nhật nằm ở `%LOCALAPPDATA%\LanternUpdates`, tách khỏi workspace.

Digest kiểm tra tính toàn vẹn từ repo đã tin cậy; đây không phải chữ ký phát hành Authenticode. Bản hiện tại chưa được ký chứng thư thương mại.

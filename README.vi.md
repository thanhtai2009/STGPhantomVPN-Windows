[English](README.md) | **Tiếng Việt**

# STG Phantom VPN for Windows - 1.0.11

Ứng dụng VPN miễn phí cho Windows, giúp kết nối nhanh tới các máy chủ công cộng của VPN Gate chỉ với vài cú nhấp chuột. Giao diện đơn giản, trực quan, là lựa chọn thay thế gọn nhẹ cho SoftEther VPN Client chính thức.

## Điểm nổi bật

- Danh sách máy chủ VPN Gate tự động cập nhật, kèm cờ quốc gia; kết nối hoặc đổi máy chủ chỉ bằng một nút bấm.
- Tự cài đặt các thành phần cần thiết ở lần sử dụng đầu tiên, không cần cấu hình thủ công.
- Hiển thị IP thực và IP VPN để kiểm tra kết nối; chặn rò rỉ IPv6 và cho phép chọn DNS riêng.
- Giao diện phong cách Windows 11 (Fluent): nền Mica trên Windows 11, tự theo chế độ sáng/tối của hệ thống.
- Chạy nền dưới khay hệ thống, VPN vẫn giữ kết nối khi đóng cửa sổ.
- Giao diện tiếng Việt, English, 简体中文 và ភាសាខ្មែរ.

## Yêu cầu hệ thống

- Windows 10 hoặc 11 (x64, x86, ARM64); Windows 8.1 và Windows 7 SP1 cần thêm một số bản cập nhật, ứng dụng sẽ tự kiểm tra và hướng dẫn.
- .NET Framework 4.8 (có sẵn trên Windows 10 1903 trở lên).
- Kết nối Internet. Ở lần dùng đầu, ứng dụng tải về OpenVPN chính thức và driver mạng ảo TAP-Windows6; bước này cần xác nhận quyền quản trị (UAC) một lần.

## Cài đặt

1. Tải `STGPhantomVPN-Setup-1.0.11.exe` (khuyên dùng) hoặc bản portable `STGPhantomVPN-Windows-1.0.11.zip`.
2. Chạy trình cài đặt và làm theo hướng dẫn. Với bản portable, giải nén toàn bộ thư mục rồi chạy `STGPhantomVPN.exe`.
3. Ứng dụng chưa được ký số nên Windows SmartScreen có thể cảnh báo. Đối chiếu mã SHA-256 với tệp `.sha256` đi kèm, sau đó chọn **Thông tin thêm → Vẫn chạy**.

## Sử dụng

1. Mở ứng dụng và chờ danh sách máy chủ tải xong.
2. Nhấn **Kết nối** để dùng máy chủ được đề xuất, hoặc chọn một máy chủ khác trong danh sách.
3. Ở lần đầu, đồng ý cài OpenVPN và driver khi được hỏi.
4. Khi đã kết nối, trang chính hiển thị IP VPN để xác nhận. Nhấn **Ngắt kết nối** để dừng.

## Lưu ý

- Ứng dụng chưa có kill switch: nếu đường hầm VPN bị ngắt, lưu lượng sẽ đi qua kết nối thông thường.
- Máy chủ VPN Gate do tình nguyện viên vận hành, tốc độ và độ ổn định có thể khác nhau.

## Credits

- [VPN Gate](https://www.vpngate.net/) – dự án máy chủ VPN công cộng của Đại học Tsukuba, vận hành trên nền tảng [SoftEther VPN](https://www.softether.org/) (mã nguồn mở, giấy phép Apache 2.0).
- [OpenVPN](https://openvpn.net/) – được cài từ bộ cài chính thức, không chỉnh sửa. "OpenVPN" là nhãn hiệu đã đăng ký của OpenVPN Inc.
- Thông tin giấy phép của các thành phần bên thứ ba: `licenses/THIRD-PARTY-NOTICES.txt` và `licenses/SOURCE-OFFER.txt`.

Hướng dẫn tự build từ mã nguồn: xem [BUILDING.vi.md](BUILDING.vi.md).

STG Phantom VPN là dự án độc lập, không liên kết với SoftEther VPN, VPN Gate hay OpenVPN Inc.

Designed by TaiNT13@PNC STG

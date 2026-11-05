[English](BUILDING.md) | **Tiếng Việt**

# Hướng dẫn build STG Phantom VPN

Tài liệu này dành cho người muốn tự build ứng dụng từ mã nguồn. Ứng dụng là WPF trên .NET Framework 4.8, chạy được trên Windows 7 SP1 trở lên.

## Yêu cầu

| Thành phần | Ghi chú |
|---|---|
| Windows 10/11 hoặc Linux (x64) | Trên Linux, `net48` được biên dịch nhờ `EnableWindowsTargeting`. |
| [.NET SDK 8](https://dotnet.microsoft.com/download) trở lên | Bắt buộc. Không cần cài Visual Studio. |
| [Inno Setup 6](https://jrsoftware.org/isdl.php) | Tùy chọn, chỉ cần khi build bộ cài `.exe`. |
| Wine (có hỗ trợ 32-bit) | Chỉ cần trên Linux khi build bộ cài. Debian/Ubuntu: `wine wine64 wine32:i386`. |

## Build trên Windows

Mở PowerShell tại thư mục gốc của mã nguồn:

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1              # chạy test, build, đóng gói zip portable
powershell -ExecutionPolicy Bypass -File build\build.ps1 -Installer   # thêm bộ cài (cần Inno Setup 6)
powershell -ExecutionPolicy Bypass -File build\build.ps1 -BundleOpenVpn -OutDir D:\out
```

- `-Installer`: tìm `ISCC.exe` qua biến môi trường `ISCC`, registry của Inno Setup 6 hoặc các thư mục cài mặc định.
- `-BundleOpenVpn`: đưa bộ cài OpenVPN chính thức (đã kiểm tra SHA-256) vào thư mục `redist\` của bản zip, kèm mã nguồn GPL trong `source\`.
- `-OutDir <thư mục>`: đổi thư mục đầu ra. `-SkipTests`: bỏ qua bước test (không khuyến khích).

Ngoài script, có thể mở `src\STGPhantomVPN\STGPhantomVPN.csproj` bằng Visual Studio 2022, hoặc build trực tiếp:

```powershell
dotnet build src\STGPhantomVPN\STGPhantomVPN.csproj -c Release
```

Kết quả nằm trong `src\STGPhantomVPN\bin\Release\net48\`.

## Build trên Linux

```bash
build/build.sh                    # chạy test, build, đóng gói zip portable
build/build.sh --installer        # thêm bộ cài; Inno Setup 6.7.4 được tải về (kiểm tra SHA-256) và chạy qua Wine
build/build.sh --bundle-openvpn   # zip kèm bộ cài OpenVPN và mã nguồn GPL
OUT_DIR=/duong/dan build/build.sh # đổi thư mục đầu ra
```

## Chạy test

```bash
dotnet run --project tests/STGPhantomVPN.Tests -c Release
```

Cả hai script build đều chạy test trước và dừng lại nếu có test lỗi.

## Kết quả build

Mặc định mọi tệp được ghi vào thư mục `out/` trong mã nguồn:

- `STGPhantomVPN-Windows-<phiên bản>.zip`: bản portable, kèm tệp `.sha256`.
- `STGPhantomVPN-Setup-<phiên bản>.exe`: bộ cài (khi dùng `-Installer` / `--installer`), kèm tệp `.sha256`.

## Đổi phiên bản

Phiên bản chính nằm ở `<Version>` trong `src/STGPhantomVPN/STGPhantomVPN.csproj`; cả hai script build đọc từ đây. Khi đổi phiên bản, cập nhật cùng giá trị ở:

- `<FileVersion>` và `<InformationalVersion>` trong cùng tệp csproj,
- `src/STGPhantomVPN/app.manifest`,
- `AppVersion.Current` trong `src/STGPhantomVPN/Core/VpnGateClient.cs`,
- `#define AppVersion` trong `installer/STGPhantomVPN.iss`,
- dòng tiêu đề và tên tệp trong `README.md` và `README.vi.md`, dòng đầu của `licenses/THIRD-PARTY-NOTICES.txt`.

Test sẽ báo lỗi nếu các giá trị này không khớp nhau.

## Lưu ý

- Bản build không được ký số (code signing), nên Windows SmartScreen sẽ cảnh báo khi chạy lần đầu. Hãy đối chiếu SHA-256 với tệp `.sha256`, sau đó chọn **Thông tin thêm → Vẫn chạy**.
- Bộ cài và bản zip mặc định không chứa OpenVPN. Ở lần dùng đầu, ứng dụng tự tải bộ cài OpenVPN chính thức từ openvpn.net và chỉ cài khi mã SHA-256 khớp với giá trị được ghim sẵn trong mã nguồn (`Core/PinnedDownloads.cs`).
- Thời gian của tệp trong zip và bộ cài build trên Linux được đặt theo `RELEASE_DATE` trong `build/build.sh` (hoặc `SOURCE_DATE_EPOCH`), nên build lại cùng mã nguồn cho kết quả giống hệt. Bản zip do `build.ps1` tạo dùng thời gian build hiện tại.

# SelfStore

Ứng dụng WPF .NET 8 quản lý thuê kho, sử dụng **Microsoft SQL Server** qua `Microsoft.Data.SqlClient`.

## Chuẩn bị

- Windows và .NET SDK 8 trở lên.
- SQL Server 2019 trở lên, SQL Server Express hoặc LocalDB. Database cần compatibility level từ 130 (để dùng `OPENJSON`).
- SSMS hoặc `sqlcmd` để tạo database lần đầu.

## 1. Tạo database

Từ thư mục gốc repo, với SQL Server LocalDB:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -b -i database\CreateDatabase.sql
```

Với SQL Server cài dạng instance mặc định, thay server bằng `.`; với Express, dùng `.\SQLEXPRESS` theo tên instance trên máy.

Ứng dụng tự tạo các bảng khi mở lần đầu. Có thể tạo bảng trước bằng:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -b -d SelfStorage -i database\Schema.sql
```

Trong SSMS: chạy `CreateDatabase.sql` trên `master`, chọn database `SelfStorage`, rồi chạy `Schema.sql`. Hai script có thể chạy lại. Script tạo cấu trúc; dữ liệu mẫu được ứng dụng thêm khi khởi động.

## 2. Cấu hình kết nối và chạy

`SelfStorage/appsettings.json` mặc định dùng LocalDB, Windows Authentication và database `SelfStorage`. File này được chép cạnh ứng dụng khi build.

Có thể dùng biến môi trường để ghi đè cấu hình, ví dụ với SQL Server trên máy:

```powershell
$env:SELFSTORAGE_CONNECTION_STRING = "Server=.;Database=SelfStorage;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10"
dotnet run --project SelfStorage/SelfStorage.csproj
```

Biến môi trường được ưu tiên hơn `appsettings.json`. Với SQL Authentication, đặt `User ID` và `Password` qua biến môi trường, không commit mật khẩu vào repo. `TrustServerCertificate=True` trong cấu hình mẫu dành cho máy phát triển; khi kết nối server có chứng chỉ hợp lệ, dùng `False`.

Tài khoản chạy lần đầu cần quyền tạo bảng/index và gọi `sp_getapplock` trong database đã tạo, hoặc quản trị viên chạy `Schema.sql` trước. Tài khoản ứng dụng cần quyền đọc/ghi các bảng và thực thi `sp_getapplock` để kiểm tra schema khi khởi động.

Khi database mới, chưa có dữ liệu JSON cũ, ứng dụng tạo 3 cơ sở, 36 kho và 2 tài khoản demo:

| Vai trò | Email | Mật khẩu |
| --- | --- | --- |
| Admin | admin@demo.com | admin123 |
| Khách | demo@demo.com | 123456 |

## Dữ liệu và chuyển đổi từ bản cũ

- `Users`: tài khoản, mật khẩu băm và vai trò.
- `Facilities`: cơ sở kho.
- `StorageUnits`: kho, diện tích, giá thuê và trạng thái.
- `Rentals`: khách/kho thuê, thời hạn, tiền cọc/phí thuê, phương thức thanh toán, QR và thông tin trả kho.
- `StoreState`: phiên bản schema và phiên bản dữ liệu dùng để phát hiện ghi đè từ phiên ứng dụng cũ.

Khóa ngoại bảo vệ quan hệ dữ liệu. Email, tên cơ sở và mã kho trong từng cơ sở có ràng buộc duy nhất. Mỗi kho chỉ có một đơn thuê chưa trả. Tiền dùng `decimal(18,2)`, ngày giờ dùng `datetime2`, nội dung tiếng Việt dùng `nvarchar`.

Ở lần mở đầu tiên của database trống, nếu có `%APPDATA%\SelfStorage\data.json`, ứng dụng nhập dữ liệu đó trong một transaction và giữ nguyên file JSON. Những lần mở sau chỉ dùng SQL Server. JSON hỏng hoặc không thỏa ràng buộc sẽ báo lỗi và không seed đè; sửa file rồi mở lại để thử nhập lại. Dữ liệu đã có sẵn trong SQL Server được giữ lại.

Giao diện hiện giữ các danh sách trong bộ nhớ. Mỗi lần lưu, ứng dụng đồng bộ thay đổi trong một transaction, khóa và kiểm tra `Revision` để tránh phiên cũ ghi đè dữ liệu phiên khác. Nếu có xung đột hoặc lỗi SQL, transaction không commit và ứng dụng báo lỗi rồi đóng; mở lại để tải dữ liệu mới. Các thay đổi trực tiếp bằng SQL ngoài ứng dụng phải tăng `StoreState.Revision` trong cùng transaction.

## Kiểm tra

```powershell
dotnet build SelfStorage/SelfStorage.csproj --configuration Release
dotnet run --project tests/SelfStorage.IntegrationTests --configuration Release
```

Bộ kiểm tra tích hợp dùng SQL Server LocalDB, tự tạo và xóa các database riêng mang tên `SelfStorage_Test_<guid>`. Kiểm tra dữ liệu mẫu, CRUD, đăng ký, thuê/trả, tiền/ngày giờ, QR, khóa ngoại, email trùng, rollback, xung đột lưu và nhập JSON. Có thể đặt `SELFSTORAGE_TEST_CONNECTION_STRING` để dùng SQL Server khác; tài khoản kiểm tra cần quyền tạo/xóa database thử nghiệm.

Phần thanh toán hiện vẫn là mô phỏng theo code ban đầu.

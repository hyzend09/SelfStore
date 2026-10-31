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

Tài khoản chạy lần đầu cần quyền tạo/sửa bảng, index, ràng buộc và gọi `sp_getapplock` trong database đã tạo, hoặc quản trị viên chạy `Schema.sql` trước. Tài khoản ứng dụng cần quyền đọc/ghi các bảng và thực thi `sp_getapplock` để kiểm tra schema khi khởi động.

Khi database mới, chưa có dữ liệu JSON cũ, ứng dụng tạo 3 cơ sở, 36 kho và 2 tài khoản demo:

| Vai trò | Email | Mật khẩu |
| --- | --- | --- |
| Admin | admin@demo.com | admin123 |
| Khách | demo@demo.com | 123456 |

## Dữ liệu và chuyển đổi từ bản cũ

- `Users`: tài khoản, mật khẩu băm và vai trò.
- `Facilities`: cơ sở kho.
- `StorageUnits`: kho, diện tích, giá thuê và trạng thái.
- `Rentals`: thông tin khách/cơ sở/kho tại thời điểm đặt, thời hạn, cọc/phí thuê, QR, bàn giao, yêu cầu trả, biên bản kiểm tra, khấu trừ và đối soát hoàn cọc.
- `StoreState`: phiên bản schema và phiên bản dữ liệu dùng để phát hiện ghi đè từ phiên ứng dụng cũ.

Khóa ngoại bảo vệ quan hệ dữ liệu. Email, tên cơ sở và mã kho trong từng cơ sở có ràng buộc duy nhất. Mỗi kho chỉ có một đơn thuê chưa trả. Tiền dùng `decimal(18,2)`, ngày giờ dùng `datetime2`, nội dung tiếng Việt dùng `nvarchar`.

Ở lần mở đầu tiên của database trống, nếu có `%APPDATA%\SelfStorage\data.json`, ứng dụng nhập dữ liệu đó trong một transaction và giữ nguyên file JSON. Những lần mở sau chỉ dùng SQL Server. JSON hỏng hoặc không thỏa ràng buộc sẽ báo lỗi và không seed đè; sửa file rồi mở lại để thử nhập lại. Dữ liệu đã có sẵn trong SQL Server được giữ lại.

Ứng dụng tự nâng schema phiên bản 1 lên phiên bản 2 khi mở, hoặc quản trị viên chạy lại `database/Schema.sql` trước. Chỉ dùng bản ứng dụng mới sau khi nâng schema. Dữ liệu cũ và QR được giữ lại; tên khách, mã kho, tên/địa chỉ cơ sở được chụp lại để lịch sử không đổi theo chỉnh sửa sau này. Với đơn cũ đã trả, ứng dụng hiển thị chưa có đối soát cọc; không tự suy đoán rằng đã hoàn tiền. Đơn cũ đang thuê vẫn có thể gửi yêu cầu trả.

Nút làm mới, chuyển tab khách hàng và đăng nhập đọc lại dữ liệu SQL Server. Mỗi thao tác ghi tải dữ liệu mới trước, đồng bộ thay đổi trong một transaction và kiểm tra `Revision`. Lỗi SQL/xung đột không đóng ứng dụng; thay đổi chưa lưu được bỏ, dữ liệu được tải lại để người dùng kiểm tra và thử lại. Cửa sổ sửa kho/cơ sở dùng bản sao và phát hiện thay đổi từ phiên khác. Các thay đổi trực tiếp bằng SQL ngoài ứng dụng phải tăng `StoreState.Revision` trong cùng transaction.

## Luồng đặt, nhận, trả kho và hoàn cọc

1. **Khách đặt kho**: chọn ngày bắt đầu (trong vòng 5 năm), thuê 1/3/6/12 tháng, xem phí và cọc. Thanh toán mô phỏng thành công thì tạo đơn **Chờ nhận kho**, giữ kho và cấp QR. Hủy cửa sổ thanh toán không tạo đơn. Giá và trạng thái kho được kiểm tra lại khi lưu.
2. **Admin bàn giao**: tại tab **Nhận / trả kho và cọc**, chọn đơn, bấm **Bàn giao nhận kho (QR)**. Đầu đọc QR có thể nhập mã vào ô văn bản, hoặc nhân viên dán nội dung từ màn hình QR của khách. Mã phải khớp đúng đơn, đúng ngày nhận, chưa dùng và đơn còn hiệu lực. Xác nhận ghi lại người bàn giao và thời gian; trạng thái chuyển thành **Đang thuê**. Không tích hợp camera đọc QR.
3. **Khách yêu cầu trả**: tại **Kho của tôi**, chọn đơn đã nhận và bấm **Yêu cầu trả kho**. Đơn chuyển thành **Chờ kiểm tra trả kho**; kho vẫn bận.
4. **Admin kiểm tra**: chọn đơn, bấm **Kiểm tra trả kho**, nhập biên bản và khoản khấu trừ từ 0 đến tiền cọc. Số được hoàn = cọc − khấu trừ. Đơn chuyển thành **Chờ hoàn cọc**; kho vẫn chưa cho thuê lại.
5. **Admin hoàn tiền và đối soát**: thực hiện chuyển tiền/chi tiền ngoài ứng dụng, rồi bấm **Xác nhận hoàn cọc**, nhập mã giao dịch hoặc số phiếu chi và đánh dấu xác nhận. Ứng dụng ghi người thực hiện, thời gian, số tiền, mã đối soát; đóng đơn và mở kho cho thuê lại trong cùng transaction. Nếu khấu trừ hết cọc, vẫn phải xác nhận đối soát nhưng không cần mã chuyển tiền. Không thể hoàn cọc hai lần.
6. **Khách theo dõi**: chọn đơn trong **Kho của tôi** để xem thời gian nhận/trả, biên bản, khấu trừ, số tiền và mã đối soát.

Tiền được nhập/lưu/hiển thị với tối đa 2 chữ số thập phân. Kho đang có đơn chưa hoàn tất không được đổi cơ sở hoặc mở trống thủ công. Đơn quá hạn được gắn nhãn; không tự tính phụ phí ngoài biên bản kiểm tra.

## Kiểm tra

```powershell
dotnet build SelfStorage/SelfStorage.csproj --configuration Release
dotnet run --project tests/SelfStorage.IntegrationTests --configuration Release
```

Bộ kiểm tra tích hợp dùng SQL Server LocalDB, tự tạo và xóa các database riêng mang tên `SelfStorage_Test_<guid>`. Kiểm tra dữ liệu mẫu, CRUD, đăng ký, tiền/ngày giờ, khóa ngoại, email trùng, rollback, lưu/đặt kho đồng thời, nhập JSON, nâng schema cũ và toàn bộ trạng thái nhận/trả/đối soát cọc. Kiểm tra WPF chạy các cửa sổ thật trên luồng STA: sửa giá, hủy thanh toán khi đang xử lý, đặt kho qua thanh toán/QR, tải lại dữ liệu từ phiên khác, bàn giao, kiểm tra và xác nhận hoàn cọc.

Có thể đặt `SELFSTORAGE_TEST_CONNECTION_STRING` để dùng SQL Server khác; tài khoản kiểm tra cần quyền tạo/xóa database thử nghiệm. Các kiểm tra WPF cần phiên Windows desktop.

Phần thanh toán vẫn là mô phỏng; xác nhận hoàn cọc là ghi nhận tiền đã hoàn bên ngoài, chưa có cổng thanh toán/chuyển tiền thật. Gia hạn, hủy đơn đã đặt, tự tính phí quá hạn và khôi phục mật khẩu chưa được triển khai.

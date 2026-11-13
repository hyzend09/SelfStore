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

Khi database mới, chưa có dữ liệu JSON cũ, ứng dụng tạo 3 vị trí mẫu, 36 kho và 2 tài khoản demo:

| Vai trò | Email | Mật khẩu |
| --- | --- | --- |
| Admin | admin@demo.com | admin123 |
| Khách | demo@demo.com | 123456 |

## Dữ liệu và chuyển đổi từ bản cũ

- `Users`: tài khoản, mật khẩu băm và vai trò.
- `Facilities`: danh mục vị trí và địa chỉ dùng trong thông tin kho.
- `StorageUnits`: kho, diện tích, giá thuê và trạng thái.
- `Rentals`: đơn giữ chỗ và đơn thuê, hạn thanh toán, bản chụp thông tin khách/kho/vị trí, QR, biên bản trả kho, phí phát sinh và đối soát cọc.
- `Payments`: một giao dịch thành công cho mỗi đơn thuê, gồm số tiền, phương thức, thời điểm thanh toán và mã giao dịch duy nhất.
- `StoreState`: phiên bản schema và phiên bản dữ liệu dùng để phát hiện ghi đè từ phiên ứng dụng cũ.

Khóa ngoại bảo vệ quan hệ dữ liệu. Email, tên cơ sở và mã kho trong từng cơ sở có ràng buộc duy nhất. Mỗi kho có tối đa một đơn đang giữ chỗ hoặc chưa trả. Tiền dùng `decimal(18,2)`, ngày giờ dùng `datetime2`, nội dung tiếng Việt dùng `nvarchar`.

Ở lần mở đầu tiên của database trống, ứng dụng tìm dữ liệu cũ tại `%APPDATA%\SelfStorage\data.json`. Có thể chỉ định file khác bằng `SELFSTORAGE_LEGACY_JSON_PATH`. Ứng dụng nhập dữ liệu trong một transaction và giữ nguyên file nguồn. Nếu file hỏng hoặc vi phạm ràng buộc, sửa file rồi mở lại để thử nhập lại. Những lần mở sau dùng dữ liệu đã lưu trong SQL Server.

Ứng dụng tự nâng schema phiên bản 1–3 lên phiên bản 4 khi mở. Quản trị viên cũng có thể chạy trước `database/Schema.sql`. Các khoản thanh toán mới dùng giờ UTC của SQL Server và được quy đổi sang giờ địa phương khi thống kê. Các mốc thanh toán cũ giữ nguyên giá trị giờ địa phương đã lưu. QR, thông tin đơn và biên bản đối soát hiện có được giữ lại.

Nút làm mới, chuyển tab khách hàng và đăng nhập đọc lại dữ liệu SQL Server. Mỗi thao tác ghi tải dữ liệu mới trước, đồng bộ thay đổi trong một transaction và kiểm tra `Revision`. Khi có xung đột, ứng dụng bỏ thay đổi chưa lưu và tải lại dữ liệu. Cửa sổ sửa kho dùng bản sao để phát hiện thay đổi từ phiên khác. Các thay đổi trực tiếp vào dữ liệu nghiệp vụ bằng SQL phải tăng `StoreState.Revision` trong cùng transaction.

Mật khẩu mới dùng PBKDF2-HMAC-SHA256 với salt riêng. Tài khoản cũ được nâng cấp hash khi đăng nhập thành công. Danh sách người dùng bỏ cột hash và phiên Customer chỉ nhận email của chính mình. WPF vẫn dùng kết nối trực tiếp tới SQL Server, vì vậy quyền của tài khoản kết nối SQL là ranh giới truy cập dữ liệu của bản demo.

## Phạm vi WPF

Ứng dụng có hai vai trò **Admin** và **Customer**. Vị trí và địa chỉ là thông tin của kho. Bản demo dùng danh mục vị trí sẵn có. Tiền cọc bằng một tháng tiền thuê.

### Luồng bắt buộc

1. **Đăng ký và đăng nhập.** Customer đăng ký bằng họ tên, email và mật khẩu. Tài khoản đã khóa không đăng nhập hoặc thao tác trên đơn qua phiên đang mở.
2. **Quản lý kho.** Admin thêm, xem, sửa hoặc xóa kho chưa có lịch sử thuê. Kho có mã, loại, diện tích, vị trí, giá theo tháng và trạng thái. Admin có thể mở hoặc đưa kho chưa có đơn hiệu lực vào bảo trì.
3. **Đặt kho.** Customer xem kho trống, lọc theo vị trí, loại, diện tích tối thiểu và giá tối đa. Khách chọn một kho cụ thể, ngày bắt đầu và thời hạn 1–12 tháng. Hệ thống tính phí thuê, cọc và tạo đơn **Chờ thanh toán** giữ kho trong 15 phút. Khách có thể thanh toán ngay, tiếp tục từ **Kho của tôi** hoặc hủy đơn chưa thanh toán.
4. **Thanh toán và cấp QR.** Thanh toán mô phỏng thành công ghi giao dịch và cấp QR của đơn trong cùng transaction. Customer xem đơn, lịch sử và QR trong **Kho của tôi**. QR chứa mã đơn và chuỗi ngẫu nhiên xác minh. Dùng đầu đọc QR nhập dạng bàn phím hoặc dán nội dung mã, có thể tạo QR offline bằng QRCoder.
5. **Bàn giao.** Customer xuất trình QR. Admin chọn đơn, đối chiếu tên khách và kho hiện trên màn hình, quét hoặc nhập mã rồi xác nhận. Đơn phải đã thanh toán, đến hạn nhận và chưa bàn giao. Hệ thống ghi người bàn giao, thời điểm nhận và chuyển kho sang **Đang thuê**.
6. **Trả kho và hoàn cọc.** Customer gửi yêu cầu trả. Admin dùng cùng mã QR để kiểm tra đúng đơn, ghi tình trạng kho, phí phát sinh và lựa chọn bảo trì. Tiền hoàn bằng cọc trừ khoản khấu trừ. Phần phí vượt cọc được ghi lại để xử lý bên ngoài. Sau khi hoàn tiền thực tế, Admin ghi mã đối soát hoặc xác nhận hết số dư cọc. Hệ thống đóng đơn và chuyển kho sang **Còn trống** hoặc **Bảo trì** trong cùng transaction.

### Trạng thái và tình huống ngoại lệ

- **Kho** đi từ **Còn trống** → **Đã đặt** → **Đang thuê** → **Còn trống** hoặc **Bảo trì**.
- **Đơn** đi từ **Chờ thanh toán** → **Chờ nhận kho** (đã thanh toán) → **Đang thuê** → **Chờ kiểm tra trả kho** → **Chờ hoàn cọc** → **Đã trả**. Đơn chưa thanh toán có thể chuyển sang **Đã hủy** hoặc **Hết hạn giữ chỗ**.
- Mỗi đơn giữ riêng một kho từ lúc đặt đến khi hủy, hết hạn giữ chỗ hoặc trả xong, kể cả đơn có ngày bắt đầu trong tương lai. Hệ thống từ chối mọi lượt đặt khác trên kho đang bận, bao gồm các kỳ thuê chồng lấn. SQL Server dùng transaction, kiểm tra phiên bản dữ liệu và chỉ mục duy nhất để phân xử khi nhiều phiên cùng đặt.
- SQL Server dùng giờ UTC để kiểm tra 15 phút. Thao tác đọc/ghi tiếp theo ghi trạng thái hết hạn và mở kho. Màn hình đang hiển thị đơn giữ chỗ tự làm mới sau khi đến hạn.
- Mỗi đơn có tối đa một khoản thanh toán. Gửi lại cùng mã giao dịch cho cùng đơn trả về QR đã cấp. QR không thể bàn giao hai lần. Đơn đang thuê quá ngày kết thúc vẫn đi qua luồng trả kho, với phí phát sinh do Admin ghi trong biên bản.
- Khi trả kho, hệ thống yêu cầu QR trùng với mã đã cấp. Đơn từ dữ liệu cũ chưa từng có QR được kiểm tra theo mã đơn và thông tin khách.
- **Khách không đến nhận kho.** Sau ngày kết thúc, Admin chọn đơn và bấm **Xử lý đơn ngoại lệ**. Đơn chuyển thẳng sang **Chờ hoàn cọc** với toàn bộ tiền cọc. Phí thuê đã thu giữ nguyên. Kho mở lại khi Admin xác nhận đối soát.
- **Khách đang thuê bị khóa tài khoản.** Admin dùng **Xử lý đơn ngoại lệ** để chuyển đơn sang **Chờ kiểm tra trả kho**. Nếu khách không xuất trình được QR, Admin phải đối chiếu danh tính và mã đơn, rồi xác nhận trên biên bản kiểm tra. Đơn tiếp tục qua bước đối soát cọc như bình thường.

Admin có thể xem và khóa/mở khóa Customer. Thống kê hiển thị số kho trống, kho đang thuê và phí thuê đã thu trong tháng chọn theo ngày thanh toán. Tiền cọc và khoản phí phát sinh nằm ngoài chỉ số phí thuê.

## Kiểm tra

```powershell
dotnet build SelfStorage/SelfStorage.csproj --configuration Release
dotnet run --project tests/SelfStorage.IntegrationTests --configuration Release
```

Bộ kiểm tra tích hợp dùng SQL Server LocalDB, tự tạo và xóa các database riêng mang tên `SelfStorage_Test_<guid>`. Kiểm tra dữ liệu mẫu, CRUD, đăng ký, tiền/ngày giờ, khóa ngoại, email trùng, rollback, lưu/đặt kho đồng thời, nhập JSON, nâng schema cũ và toàn bộ trạng thái nhận/trả/đối soát cọc. Kiểm tra WPF chạy các cửa sổ thật trên luồng STA: sửa giá, hủy thanh toán khi đang xử lý, đặt kho qua thanh toán/QR, tải lại dữ liệu từ phiên khác, bàn giao, kiểm tra và xác nhận hoàn cọc.

Có thể đặt `SELFSTORAGE_TEST_CONNECTION_STRING` để dùng SQL Server khác. Tài khoản kiểm tra cần quyền tạo/xóa database thử nghiệm. Các kiểm tra WPF cần phiên Windows desktop và tự đặt đường dẫn JSON riêng để fixture bắt đầu từ dữ liệu mẫu.

Phần thanh toán vẫn là mô phỏng; xác nhận hoàn cọc là ghi nhận tiền đã hoàn bên ngoài, chưa có cổng thanh toán/chuyển tiền thật. Đơn đã thanh toán được kết thúc bằng luồng trả kho.

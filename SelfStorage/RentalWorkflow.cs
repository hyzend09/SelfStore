namespace SelfStorage;

public partial class DataStore
{
    void RequireAdmin()
    {
        if (CurrentUser?.Role != "Admin") throw new InvalidOperationException("Chỉ admin được thực hiện thao tác này.");
    }

    Rental FindRental(int id) => Rentals.FirstOrDefault(r => r.Id == id)
        ?? throw new InvalidOperationException("Đơn thuê không còn tồn tại. Hãy làm mới danh sách.");

    public Rental Book(int unitId, DateTime start, int months, decimal quotedPrice, string method)
    {
        Rental? result = null;
        Change(db =>
        {
            if (db.CurrentUser?.Role != "Customer") throw new InvalidOperationException("Hãy đăng nhập tài khoản khách hàng.");
            if (start.Date < DateTime.Today || start.Date > DateTime.Today.AddYears(5) || !new[] { 1, 3, 6, 12 }.Contains(months))
                throw new InvalidOperationException("Ngày bắt đầu hoặc thời hạn thuê không hợp lệ.");
            var unit = db.Units.SingleOrDefault(u => u.Id == unitId)
                ?? throw new InvalidOperationException("Kho không còn tồn tại. Hãy làm mới danh sách.");
            if (!unit.IsAvailable || db.Rentals.Any(r => r.UnitId == unitId && !r.IsReturned))
                throw new InvalidOperationException("Kho đã được đặt ở phiên khác. Hãy chọn kho khác.");
            if (quotedPrice != unit.PricePerMonth) throw new InvalidOperationException("Giá kho đã thay đổi. Hãy đóng cửa sổ và đặt lại với giá mới.");
            if (!ValidMoney(unit.PricePerMonth * months)) throw new InvalidOperationException("Tổng phí thuê vượt giới hạn số tiền được hỗ trợ.");
            if (!new[] { "Thẻ ngân hàng", "Ví điện tử", "Chuyển khoản" }.Contains(method))
                throw new InvalidOperationException("Phương thức thanh toán không hợp lệ.");
            result = new Rental { Id = db.NextRentalId(), UserId = db.CurrentUser.Id, UnitId = unit.Id,
                StartDate = start.Date, Months = months, Deposit = unit.PricePerMonth, RentFee = unit.PricePerMonth * months,
                PaymentMethod = method, PaidAt = DateTime.Now, QrContent = "SELFSTORAGE|" + Guid.NewGuid().ToString("N") };
            unit.IsAvailable = false;
            db.Rentals.Add(result);
        });
        return result!;
    }

    public Rental VerifyPickup(string qr)
    {
        Reload();
        RequireAdmin();
        return CheckPickup(qr);
    }

    Rental CheckPickup(string qr)
    {
        var rental = Rentals.SingleOrDefault(r => r.QrContent != "" && r.QrContent == qr.Trim())
            ?? throw new InvalidOperationException("QR không hợp lệ hoặc không thuộc đơn thuê nào.");
        if (rental.IsReturned || rental.ReceivedAt != null || rental.ReturnRequestedAt != null)
            throw new InvalidOperationException("Đơn đã nhận kho, đang trả hoặc đã đóng. Không thể bàn giao lại.");
        if (rental.StartDate.Date > DateTime.Today) throw new InvalidOperationException("Chưa đến ngày nhận kho.");
        if (rental.EndDate.Date < DateTime.Today) throw new InvalidOperationException("Đơn đã quá hạn nhận kho.");
        return rental;
    }

    public void ReceiveRental(string qr, int expectedRentalId) => Change(db =>
    {
        db.RequireAdmin();
        var rental = db.CheckPickup(qr);
        if (rental.Id != expectedRentalId) throw new InvalidOperationException("QR không khớp đơn đang chọn.");
        rental.ReceivedAt = DateTime.Now;
        rental.ReceivedByUserId = db.CurrentUser!.Id;
    });

    public void RequestReturn(int rentalId) => Change(db =>
    {
        var rental = db.FindRental(rentalId);
        if (db.CurrentUser?.Id != rental.UserId) throw new InvalidOperationException("Bạn không có quyền trả đơn thuê này.");
        if (rental.IsReturned || rental.ReturnRequestedAt != null) throw new InvalidOperationException("Đơn đã yêu cầu trả hoặc đã hoàn tất.");
        if (rental.ReceivedAt == null && !rental.IsLegacyRental) throw new InvalidOperationException("Bạn chưa nhận kho nên chưa thể yêu cầu trả kho.");
        rental.ReturnRequestedAt = DateTime.Now;
        // The unit stays reserved until inspection and deposit settlement finish.
    });

    public void InspectReturn(int rentalId, decimal deduction, string notes) => Change(db =>
    {
        db.RequireAdmin();
        var rental = db.FindRental(rentalId);
        if (rental.IsReturned || rental.ReturnRequestedAt == null || rental.InspectedAt != null)
            throw new InvalidOperationException("Đơn chưa yêu cầu trả hoặc đã được kiểm tra.");
        if (!ValidMoney(deduction, allowZero: true) || deduction > rental.Deposit)
            throw new InvalidOperationException("Khấu trừ phải từ 0 đến số tiền cọc, tối đa 2 chữ số thập phân.");
        if (string.IsNullOrWhiteSpace(notes) || notes.Trim().Length > 1000)
            throw new InvalidOperationException("Nhập biên bản kiểm tra (tối đa 1000 ký tự), nêu rõ lý do nếu khấu trừ.");
        rental.Deduction = deduction;
        rental.RefundAmount = rental.Deposit - deduction;
        rental.InspectionNotes = notes.Trim();
        rental.InspectedAt = DateTime.Now;
        rental.InspectedByUserId = db.CurrentUser!.Id;
    });

    public void ConfirmRefund(int rentalId, string reference) => Change(db =>
    {
        db.RequireAdmin();
        var rental = db.FindRental(rentalId);
        if (rental.IsReturned || rental.InspectedAt == null || rental.RefundedAt != null)
            throw new InvalidOperationException("Đơn chưa kiểm tra hoặc đã đối soát cọc.");
        if (rental.RefundAmount > 0 && string.IsNullOrWhiteSpace(reference))
            throw new InvalidOperationException("Nhập mã giao dịch hoặc số phiếu chi sau khi đã hoàn tiền thực tế.");
        if (reference.Trim().Length > 200) throw new InvalidOperationException("Mã đối soát tối đa 200 ký tự.");
        rental.RefundReference = rental.RefundAmount == 0 ? "Không còn cọc để hoàn" : reference.Trim();
        rental.RefundedAt = DateTime.Now;
        rental.RefundedByUserId = db.CurrentUser!.Id;
        rental.IsReturned = true;
        rental.ReturnedAt = DateTime.Now;
        db.Units.Single(u => u.Id == rental.UnitId).IsAvailable = true;
    });

    public static bool ValidMoney(decimal amount, bool allowZero = false) =>
        (allowZero ? amount >= 0 : amount > 0) && amount <= 9999999999999999.99m && decimal.Round(amount, 2) == amount;
}

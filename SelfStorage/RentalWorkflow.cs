namespace SelfStorage;

public partial class DataStore
{
    void RequireAdmin()
    {
        if (CurrentUser is not { Role: "Admin", IsActive: true })
            throw new InvalidOperationException("Chỉ admin được thực hiện thao tác này.");
    }

    Rental FindRental(int id) => Rentals.FirstOrDefault(r => r.Id == id)
        ?? throw new InvalidOperationException("Đơn thuê không còn tồn tại. Hãy làm mới danh sách.");

    User RequireCustomer() => CurrentUser is { Role: "Customer", IsActive: true } user
        ? user : throw new InvalidOperationException("Hãy đăng nhập bằng tài khoản khách hàng đang hoạt động.");

    Rental CreateReservation(int unitId, DateTime start, int months, decimal quotedPrice)
    {
        var customer = RequireCustomer();
        if (start.Date < DateTime.Today || start.Date > DateTime.Today.AddYears(5) || months is < 1 or > 12)
            throw new InvalidOperationException("Ngày bắt đầu hoặc thời hạn thuê không hợp lệ.");
        var unit = Units.SingleOrDefault(u => u.Id == unitId)
            ?? throw new InvalidOperationException("Kho không còn tồn tại. Hãy làm mới danh sách.");
        if (!unit.IsAvailable || Rentals.Any(r => r.UnitId == unitId && r.IsOpen))
            throw new InvalidOperationException("Kho hiện không còn trống. Hãy chọn kho khác.");
        if (quotedPrice != unit.PricePerMonth)
            throw new InvalidOperationException("Giá kho đã thay đổi. Hãy chọn lại kho với giá mới.");
        if (!ValidMoney(unit.PricePerMonth * months))
            throw new InvalidOperationException("Tổng phí thuê vượt giới hạn số tiền được hỗ trợ.");

        var rental = new Rental
        {
            Id = NextRentalId(), UserId = customer.Id, UnitId = unit.Id,
            StartDate = start.Date, Months = months, Deposit = unit.PricePerMonth,
            RentFee = unit.PricePerMonth * months, HoldExpiresAt = DatabaseUtcNow.AddMinutes(15)
        };
        unit.IsAvailable = false;
        Rentals.Add(rental);
        return rental;
    }

    public Rental CreateHold(int unitId, DateTime start, int months, decimal quotedPrice)
    {
        Rental result = null!;
        Change(db => result = db.CreateReservation(unitId, start, months, quotedPrice));
        return result;
    }

    Rental FindCustomerRental(int rentalId)
    {
        var customer = RequireCustomer();
        var rental = FindRental(rentalId);
        if (rental.UserId != customer.Id) throw new InvalidOperationException("Đơn thuê không thuộc tài khoản của bạn.");
        return rental;
    }

    public Rental PayHeldRental(int rentalId, string method, string transactionCode)
    {
        if (!Guid.TryParseExact(transactionCode, "N", out var transaction))
            throw new InvalidOperationException("Mã giao dịch mô phỏng không hợp lệ.");
        transactionCode = transaction.ToString("N");
        Rental result = null!;
        try
        {
            Change(db =>
            {
                result = db.FindCustomerRental(rentalId);
                if (MatchesPayment(result)) return;
                if (!result.IsAwaitingPayment)
                    throw new InvalidOperationException("Chỉ có thể thanh toán đơn đang giữ chỗ và còn hạn.");
                if (db.Rentals.Any(r => r.TransactionCode == transactionCode))
                    throw new InvalidOperationException("Mã giao dịch đã được sử dụng.");
                db.PayingRentalId = rentalId;
                RecordPayment(result, method, transactionCode, db.DatabaseUtcNow);
            });
        }
        catch (StoreConcurrencyException)
        {
            Reload();
            result = FindCustomerRental(rentalId);
            if (!MatchesPayment(result)) throw;
        }
        return result;

        bool MatchesPayment(Rental rental) => rental.TransactionCode == transactionCode && rental.PaymentMethod == method;
    }

    static void RecordPayment(Rental rental, string method, string transactionCode, DateTime paidAtUtc)
    {
        if (method is not ("Thẻ ngân hàng" or "Ví điện tử" or "Chuyển khoản"))
            throw new InvalidOperationException("Phương thức thanh toán không hợp lệ.");
        rental.PaymentMethod = method;
        rental.PaidAt = paidAtUtc;
        rental.PaidAtIsUtc = true;
        rental.TransactionCode = transactionCode;
        rental.QrContent = $"SELFSTORAGE|{rental.Id}|{Guid.NewGuid():N}";
    }

    public void CancelHold(int rentalId) => Change(db =>
    {
        var rental = db.FindCustomerRental(rentalId);
        if (!rental.IsAwaitingPayment) throw new InvalidOperationException("Chỉ có thể hủy đơn đang chờ thanh toán.");
        rental.HoldOutcome = "Cancelled";
        db.Units.Single(u => u.Id == rental.UnitId).IsAvailable = true;
    });

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
        if (rental.PaidAt == null || !rental.IsOpen || rental.ReceivedAt != null || rental.ReturnRequestedAt != null)
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
        var rental = db.FindCustomerRental(rentalId);
        if (!rental.CanStartReturn || !rental.WasHandedOver)
            throw new InvalidOperationException("Đơn chưa đủ điều kiện yêu cầu trả kho.");
        rental.ReturnRequestedAt = DateTime.Now;
        // The unit stays reserved until inspection and deposit settlement finish.
    });

    public void StartAdminReturn(int rentalId) => Change(db =>
    {
        db.RequireAdmin();
        var rental = db.FindRental(rentalId);
        if (!rental.CanStartReturn)
            throw new InvalidOperationException("Chỉ có thể xử lý đơn đã bàn giao hoặc đã quá hạn nhận kho.");
        if (rental.WasHandedOver && !db.IsCustomerLocked(rental))
            throw new InvalidOperationException("Khách còn hoạt động có thể gửi yêu cầu trả kho từ tài khoản của mình.");

        var now = DateTime.Now;
        rental.ReturnRequestedAt = now;
        if (!rental.MissedPickup) return;

        rental.InspectedAt = now;
        rental.InspectedByUserId = db.CurrentUser!.Id;
        rental.InspectionNotes = "Quá hạn nhận kho. Kho chưa được bàn giao. Hoàn toàn bộ tiền cọc.";
        rental.RefundAmount = rental.Deposit;
    });

    internal bool IsCustomerLocked(Rental rental) =>
        Users.Any(u => u.Id == rental.UserId && u.Role == "Customer" && !u.IsActive);

    public void InspectReturn(int rentalId, decimal extraFee, string notes, string? qr, bool requiresMaintenance,
        bool identityVerified = false) => Change(db =>
    {
        db.RequireAdmin();
        var rental = db.FindRental(rentalId);
        if (!rental.IsOpen || rental.PaidAt == null || rental.ReturnRequestedAt == null || rental.InspectedAt != null)
            throw new InvalidOperationException("Đơn chưa yêu cầu trả hoặc đã được kiểm tra.");
        if (identityVerified && (!string.IsNullOrWhiteSpace(qr) || !db.IsCustomerLocked(rental)))
            throw new InvalidOperationException("Xác minh thủ công chỉ áp dụng khi tài khoản khách đang bị khóa và khách không xuất trình được QR.");
        if (rental.RequiresReturnQr && !identityVerified && (string.IsNullOrWhiteSpace(qr) || rental.QrContent != qr.Trim()))
            throw new InvalidOperationException("QR không khớp đơn đang trả kho.");
        if (!ValidMoney(extraFee, allowZero: true))
            throw new InvalidOperationException("Phí phát sinh phải từ 0 và có tối đa 2 chữ số thập phân.");
        var report = notes.Trim();
        if (report.Length == 0)
            throw new InvalidOperationException("Nhập biên bản kiểm tra (tối đa 1000 ký tự), nêu rõ lý do nếu khấu trừ.");
        if (identityVerified) report = "Admin đã đối chiếu danh tính khách và mã đơn.\n" + report;
        if (report.Length > 1000) throw new InvalidOperationException("Biên bản kiểm tra tối đa 1000 ký tự.");
        rental.ExtraFee = extraFee;
        rental.RequiresMaintenance = requiresMaintenance;
        rental.Deduction = Math.Min(extraFee, rental.Deposit);
        rental.RefundAmount = rental.Deposit - rental.Deduction;
        rental.InspectionNotes = report;
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
        db.Units.Single(u => u.Id == rental.UnitId).IsAvailable = !rental.RequiresMaintenance;
    });

    public static bool ValidMoney(decimal amount, bool allowZero = false) =>
        (allowZero ? amount >= 0 : amount > 0) && amount <= 9999999999999999.99m && decimal.Round(amount, 2) == amount;
}

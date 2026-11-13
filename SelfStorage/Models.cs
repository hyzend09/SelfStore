using System.Text.Json.Serialization;

namespace SelfStorage;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Customer"; // Customer | Admin
    public bool IsActive { get; set; } = true;
    [JsonIgnore] public string AccountStatus => IsActive ? "Hoạt động" : "Đã khóa";
}

public class Facility
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";

    [JsonIgnore] public int UnitCount => DataStore.Instance.Units.Count(u => u.FacilityId == Id);
    [JsonIgnore] public int AvailableCount => DataStore.Instance.Units.Count(u => u.FacilityId == Id && u.IsAvailable);
}

public class StorageUnit
{
    public int Id { get; set; }
    public int FacilityId { get; set; }
    public string Code { get; set; } = "";
    public string Type { get; set; } = "";      // Nhỏ | Vừa | Lớn | Có điều hòa
    public double SizeM2 { get; set; }
    public decimal PricePerMonth { get; set; }
    public bool IsAvailable { get; set; } = true;

    [JsonIgnore] public string FacilityName =>
        DataStore.Instance.Facilities.FirstOrDefault(f => f.Id == FacilityId)?.Name ?? "";
    [JsonIgnore] public string StatusText { get; internal set; } = "Còn trống";
}

public class Rental
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int UnitId { get; set; }
    public DateTime StartDate { get; set; }
    public int Months { get; set; }
    public decimal Deposit { get; set; }
    public decimal RentFee { get; set; }
    public string PaymentMethod { get; set; } = "";
    public DateTime? PaidAt { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public string? HoldOutcome { get; set; }
    public string? TransactionCode { get; set; }
    public string QrContent { get; set; } = "";
    public bool IsReturned { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string CustomerNameSnapshot { get; set; } = "";
    public string UnitCodeSnapshot { get; set; } = "";
    public string FacilityNameSnapshot { get; set; } = "";
    public string FacilityAddressSnapshot { get; set; } = "";
    public DateTime? ReceivedAt { get; set; }
    public int? ReceivedByUserId { get; set; }
    public DateTime? ReturnRequestedAt { get; set; }
    public DateTime? InspectedAt { get; set; }
    public int? InspectedByUserId { get; set; }
    public string InspectionNotes { get; set; } = "";
    public decimal Deduction { get; set; }
    public decimal RefundAmount { get; set; }
    public DateTime? RefundedAt { get; set; }
    public int? RefundedByUserId { get; set; }
    public string RefundReference { get; set; } = "";
    public bool IsLegacyRental { get; set; }
    public decimal ExtraFee { get; set; }
    public bool RequiresMaintenance { get; set; }
    [JsonIgnore] public bool IsOpen => !IsReturned && HoldOutcome == null;
    [JsonIgnore] public bool IsAwaitingPayment => IsOpen && PaidAt == null;
    [JsonIgnore] public bool RequiresReturnQr => !IsLegacyRental || !string.IsNullOrWhiteSpace(QrContent);
    [JsonIgnore] public decimal? PaidAmount => PaidAt != null ? Total : null;
    [JsonIgnore] public string PaymentStatusText => PaidAt != null ? "Đã thanh toán" : "Chưa thanh toán";
    [JsonIgnore] public DateTime? HoldDeadlineLocal => HoldExpiresAt is DateTime utc
        ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime() : null;

    [JsonIgnore] public string CustomerName =>
        CustomerNameSnapshot;
    [JsonIgnore] public DateTime EndDate => StartDate.AddMonths(Months);
    [JsonIgnore] public decimal Total => Deposit + RentFee;
    [JsonIgnore] public StorageUnit? Unit => DataStore.Instance.Units.FirstOrDefault(u => u.Id == UnitId);
    [JsonIgnore] public string UnitCode => UnitCodeSnapshot;
    [JsonIgnore] public string FacilityName => FacilityNameSnapshot;
    [JsonIgnore] public string StatusText => HoldOutcome == "Cancelled" ? "Đã hủy"
        : HoldOutcome == "Expired" ? "Hết hạn giữ chỗ"
        : IsAwaitingPayment ? "Chờ thanh toán"
        : IsReturned ? (IsLegacyRental && RefundedAt == null ? "Đã trả (dữ liệu cũ)" : "Đã trả")
        : InspectedAt != null ? "Chờ hoàn cọc"
        : ReturnRequestedAt != null ? "Chờ kiểm tra trả kho"
        : EndDate.Date < DateTime.Today ? "Quá hạn"
        : ReceivedAt == null && !IsLegacyRental ? "Chờ nhận kho" : "Đang thuê";
    [JsonIgnore] public string RefundText => PaidAt == null ? "—" : RefundedAt != null ? $"Đã đối soát: {RefundAmount:N2} ₫"
        : IsReturned && IsLegacyRental ? "Chưa có đối soát cọc cũ"
        : InspectedAt != null ? $"Chờ hoàn: {RefundAmount:N2} ₫" : "Chưa kiểm tra";
}

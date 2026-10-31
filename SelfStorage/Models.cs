using System.Text.Json.Serialization;

namespace SelfStorage;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Customer"; // Customer | Admin
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
    [JsonIgnore] public string StatusText => IsAvailable ? "Còn trống" : "Đã thuê";
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
    public DateTime PaidAt { get; set; }
    public string QrContent { get; set; } = "";
    public bool IsReturned { get; set; }
    public DateTime? ReturnedAt { get; set; }

    [JsonIgnore] public string CustomerName =>
        DataStore.Instance.Users.FirstOrDefault(u => u.Id == UserId)?.FullName ?? "";
    [JsonIgnore] public DateTime EndDate => StartDate.AddMonths(Months);
    [JsonIgnore] public decimal Total => Deposit + RentFee;
    [JsonIgnore] public StorageUnit? Unit => DataStore.Instance.Units.FirstOrDefault(u => u.Id == UnitId);
    [JsonIgnore] public string UnitCode => Unit?.Code ?? "";
    [JsonIgnore] public string FacilityName => Unit?.FacilityName ?? "";
    [JsonIgnore] public string StatusText =>
        IsReturned ? "Đã trả" : (EndDate.Date < DateTime.Today ? "Quá hạn" : "Đang thuê");
}

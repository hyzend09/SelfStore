using System.Net.Mail;

namespace SelfStorage;

public partial class DataStore
{
    public void Register(string name, string email, string password) => Change(db =>
    {
        name = name.Trim(); email = email.Trim().ToLowerInvariant();
        if (name.Length is 0 or > 200 || email.Length > 320 || !MailAddress.TryCreate(email, out var address) || address.Address != email || password.Length < 6)
            throw new InvalidOperationException("Nhập họ tên (tối đa 200 ký tự), email hợp lệ và mật khẩu ít nhất 6 ký tự.");
        if (db.Users.Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Email đã tồn tại.");
        db.Users.Add(new User { Id = db.NextUserId(), FullName = name, Email = email, PasswordHash = HashPassword(password) });
    });

    public void SaveUnit(StorageUnit draft, StorageUnit? original) => Change(db =>
    {
        db.RequireAdmin();
        if (!double.IsFinite(draft.SizeM2) || draft.SizeM2 <= 0 || !ValidMoney(draft.PricePerMonth))
            throw new InvalidOperationException("Diện tích phải hữu hạn và lớn hơn 0; giá thuê phải lớn hơn 0, tối đa 2 số thập phân.");
        if (!db.Facilities.Any(f => f.Id == draft.FacilityId) || string.IsNullOrWhiteSpace(draft.Code) || draft.Code.Length > 100 || string.IsNullOrWhiteSpace(draft.Type) || draft.Type.Length > 100)
            throw new InvalidOperationException("Cơ sở, mã kho hoặc loại kho không hợp lệ (mã/loại tối đa 100 ký tự).");
        var current = original == null ? null : db.Units.SingleOrDefault(u => u.Id == original.Id);
        if (original != null && (current == null ||
            (current.FacilityId, current.Code, current.Type, current.SizeM2, current.PricePerMonth, current.IsAvailable) !=
            (original.FacilityId, original.Code, original.Type, original.SizeM2, original.PricePerMonth, original.IsAvailable)))
            throw new StoreConcurrencyException("Kho đã được chỉnh sửa ở phiên khác. Hãy mở lại cửa sổ sửa.");
        if (db.Units.Any(u => u.Id != current?.Id && u.FacilityId == draft.FacilityId && u.Code.Equals(draft.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Mã kho đã tồn tại trong cơ sở này.");
        if (current != null && db.Rentals.Any(r => r.UnitId == current.Id && r.IsOpen) &&
            (draft.FacilityId != current.FacilityId || draft.IsAvailable != current.IsAvailable))
            throw new InvalidOperationException("Kho còn đơn chưa hoàn tất: không được chuyển cơ sở hoặc đổi trạng thái.");
        var target = current ?? new StorageUnit { Id = db.Units.Count == 0 ? 1 : db.Units.Max(u => u.Id) + 1 };
        target.FacilityId = draft.FacilityId; target.Code = draft.Code.Trim(); target.Type = draft.Type.Trim();
        target.SizeM2 = draft.SizeM2; target.PricePerMonth = draft.PricePerMonth; target.IsAvailable = draft.IsAvailable;
        if (current == null) db.Units.Add(target);
    });

    public void SetCustomerActive(int id, bool active) => Change(db =>
    {
        db.RequireAdmin();
        var user = db.Users.SingleOrDefault(u => u.Id == id && u.Role == "Customer")
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản khách hàng.");
        user.IsActive = active;
    });

    public void DeleteUnit(int id) => Change(db =>
    {
        db.RequireAdmin();
        if (db.Rentals.Any(r => r.UnitId == id)) throw new InvalidOperationException("Kho đã có lịch sử thuê nên không thể xóa.");
        db.Units.RemoveAll(u => u.Id == id);
    });

}

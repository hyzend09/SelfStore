using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelfStorage;

/// <summary>Giữ dữ liệu cho UI và lưu nguyên tử vào SQL Server.</summary>
public partial class DataStore
{
    static readonly string LegacyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SelfStorage", "data.json");

    static readonly Lazy<DataStore> Default = new(() => Open(ReadConnectionString(), LegacyFilePath));
    public static DataStore Instance => Default.Value;
    SqlServerStore? _store;
    internal long Revision { get; set; }

    public List<User> Users { get; set; } = new();
    public List<Facility> Facilities { get; set; } = new();
    public List<StorageUnit> Units { get; set; } = new();
    public List<Rental> Rentals { get; set; } = new();

    [JsonIgnore] public User? CurrentUser { get; set; }

    public static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    public int NextRentalId() => Rentals.Count == 0 ? 1 : Rentals.Max(r => r.Id) + 1;
    public int NextUserId() => Users.Count == 0 ? 1 : Users.Max(u => u.Id) + 1;

    public void Save()
    {
        if (_store == null) throw new InvalidOperationException("DataStore chưa kết nối SQL Server.");
        CaptureMissingSnapshots();
        _store.Save(this);
    }

    public void Reload()
    {
        if (_store == null) throw new InvalidOperationException("DataStore chưa kết nối SQL Server.");
        var loaded = _store.Load();
        var userId = CurrentUser?.Id;
        Users = loaded.Users;
        Facilities = loaded.Facilities;
        Units = loaded.Units;
        Rentals = loaded.Rentals;
        Revision = loaded.Revision;
        CurrentUser = userId == null ? null : Users.FirstOrDefault(u => u.Id == userId);
    }

    public void Change(Action<DataStore> mutation)
    {
        Reload();
        try { mutation(this); Save(); }
        catch
        {
            // Discard unsaved edits, keeping the session usable after a conflict or SQL error.
            try { Reload(); } catch { /* Keep the original error for the caller. */ }
            throw;
        }
    }

    void CaptureMissingSnapshots()
    {
        foreach (var rental in Rentals)
        {
            if (rental.UnitCodeSnapshot != "") continue;
            var unit = Units.FirstOrDefault(u => u.Id == rental.UnitId);
            var facility = Facilities.FirstOrDefault(f => f.Id == unit?.FacilityId);
            if (rental.UnitCodeSnapshot == "") rental.UnitCodeSnapshot = unit?.Code ?? "";
            if (rental.FacilityNameSnapshot == "") rental.FacilityNameSnapshot = facility?.Name ?? "";
            if (rental.FacilityAddressSnapshot == "") rental.FacilityAddressSnapshot = facility?.Address ?? "";
            if (rental.CustomerNameSnapshot == "") rental.CustomerNameSnapshot = Users.FirstOrDefault(u => u.Id == rental.UserId)?.FullName ?? "";
        }
    }

    public static DataStore Open(string connectionString, string? legacyFilePath = null)
    {
        var store = new SqlServerStore(connectionString);
        store.Initialize();
        var db = store.Load();
        if (db.Revision == 0)
        {
            if (db.Users.Count != 0 || db.Facilities.Count != 0 || db.Units.Count != 0 || db.Rentals.Count != 0)
            {
                // Preserve data provisioned directly in SQL Server before the first app launch.
            }
            else if (legacyFilePath != null && File.Exists(legacyFilePath))
            {
                // Không seed đè nếu JSON cũ hỏng. Giữ file gốc kể cả sau khi import.
                var json = File.ReadAllText(legacyFilePath);
                using var document = JsonDocument.Parse(json);
                foreach (var name in new[] { "Users", "Facilities", "Units", "Rentals" })
                    if (!document.RootElement.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array)
                        throw new InvalidDataException($"Dữ liệu JSON cũ thiếu danh sách {name}.");
                db = JsonSerializer.Deserialize<DataStore>(json)
                    ?? throw new InvalidDataException("Không đọc được dữ liệu JSON cũ.");
                var jsonRentals = document.RootElement.GetProperty("Rentals");
                for (int i = 0; i < db.Rentals.Count; i++)
                    if (!jsonRentals[i].TryGetProperty("IsLegacyRental", out _)) db.Rentals[i].IsLegacyRental = true;
            }
            else db = Seed();
            db._store = store;
            db.Save();
        }
        db._store = store;
        return db;
    }

    static string ReadConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("SELFSTORAGE_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        return settings.RootElement.GetProperty("ConnectionStrings").GetProperty("SelfStorage").GetString()
            ?? throw new InvalidDataException("Thiếu cấu hình kết nối SelfStorage.");
    }

    static DataStore Seed()
    {
        var db = new DataStore();
        db.Users.Add(new User { Id = 1, FullName = "Admin", Email = "admin@demo.com", PasswordHash = Hash("admin123"), Role = "Admin" });
        db.Users.Add(new User { Id = 2, FullName = "Khách demo", Email = "demo@demo.com", PasswordHash = Hash("123456") });

        db.Facilities.Add(new Facility { Id = 1, Name = "Kho Quận 1", Address = "12 Nguyễn Huệ, Q.1, TP.HCM" });
        db.Facilities.Add(new Facility { Id = 2, Name = "Kho Thủ Đức", Address = "88 Võ Văn Ngân, TP. Thủ Đức" });
        db.Facilities.Add(new Facility { Id = 3, Name = "Kho Bình Thạnh", Address = "45 Điện Biên Phủ, Q. Bình Thạnh" });

        var types = new (string Type, double Size, decimal Price)[]
        {
            ("Nhỏ", 2, 300_000m), ("Vừa", 5, 650_000m), ("Lớn", 10, 1_200_000m), ("Có điều hòa", 6, 1_500_000m)
        };
        int id = 1;
        foreach (var f in db.Facilities)
            foreach (var t in types)
                for (int i = 1; i <= 3; i++)
                    db.Units.Add(new StorageUnit
                    {
                        Id = id++, FacilityId = f.Id, Code = $"F{f.Id}-{t.Type[0]}{i:00}".Replace("Có", "C"),
                        Type = t.Type, SizeM2 = t.Size, PricePerMonth = t.Price, IsAvailable = true
                    });
        return db;
    }
}

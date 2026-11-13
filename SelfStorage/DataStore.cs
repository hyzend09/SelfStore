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

    static readonly Lazy<DataStore> Default = new(() => Open(ReadConnectionString(),
        Environment.GetEnvironmentVariable("SELFSTORAGE_LEGACY_JSON_PATH") ?? LegacyFilePath));
    public static DataStore Instance => Default.Value;
    SqlServerStore? _store;
    internal long Revision { get; set; }
    internal DateTime DatabaseUtcNow { get; set; }
    internal int? PayingRentalId { get; set; }

    public List<User> Users { get; set; } = new();
    public List<Facility> Facilities { get; set; } = new();
    public List<StorageUnit> Units { get; set; } = new();
    public List<Rental> Rentals { get; set; } = new();

    [JsonIgnore] public User? CurrentUser { get; set; }

    const int PasswordIterations = 600_000;

    internal static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${PasswordIterations}${Convert.ToHexString(salt)}${Convert.ToHexString(hash)}";
    }

    internal static bool VerifyPassword(string password, string stored, out bool legacy)
    {
        legacy = stored.Length == 64;
        try
        {
            if (legacy)
                return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(stored),
                    SHA256.HashData(Encoding.UTF8.GetBytes(password)));

            string[] fields = stored.Split('$');
            if (fields.Length != 4 || fields[0] != "pbkdf2-sha256" || !int.TryParse(fields[1], out int iterations) ||
                iterations is < 600_000 or > 2_000_000 || fields[2].Length != 32 || fields[3].Length != 64) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromHexString(fields[2]), iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(fields[3]));
        }
        catch (FormatException) { return false; }
    }

    public bool Login(string email, string password)
    {
        if (_store == null) throw new InvalidOperationException("DataStore chưa kết nối SQL Server.");
        var user = _store.Authenticate(email.Trim(), password);
        if (user == null) return false;
        CurrentUser = user;
        Reload();
        return CurrentUser != null;
    }

    public int NextRentalId() => Rentals.Count == 0 ? 1 : Rentals.Max(r => r.Id) + 1;
    public int NextUserId() => Users.Count == 0 ? 1 : Users.Max(u => u.Id) + 1;

    public void Save()
    {
        if (_store == null) throw new InvalidOperationException("DataStore chưa kết nối SQL Server.");
        CaptureMissingSnapshots();
        _store.Save(this);
        foreach (var user in Users) user.PasswordHash = "";
        UpdateUnitStatuses();
    }

    public void Reload()
    {
        if (_store == null) throw new InvalidOperationException("DataStore chưa kết nối SQL Server.");
        var userId = CurrentUser?.Id;
        var loaded = _store.Load(userId);
        Users = loaded.Users;
        Facilities = loaded.Facilities;
        Units = loaded.Units;
        Rentals = loaded.Rentals;
        Revision = loaded.Revision;
        DatabaseUtcNow = loaded.DatabaseUtcNow;
        CurrentUser = userId == null ? null : Users.FirstOrDefault(u => u.Id == userId && u.IsActive);
        UpdateUnitStatuses();
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
        finally { PayingRentalId = null; }
    }

    void UpdateUnitStatuses()
    {
        var occupied = Rentals.Where(r => r.IsOpen).ToDictionary(r => r.UnitId);
        foreach (var unit in Units)
            unit.StatusText = unit.IsAvailable ? "Còn trống" : !occupied.TryGetValue(unit.Id, out var rental)
                ? "Bảo trì" : rental.ReceivedAt != null || rental.IsLegacyRental ? "Đang thuê" : "Đã đặt";
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
        db.UpdateUnitStatuses();
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
        db.Users.Add(new User { Id = 1, FullName = "Admin", Email = "admin@demo.com", PasswordHash = HashPassword("admin123"), Role = "Admin" });
        db.Users.Add(new User { Id = 2, FullName = "Khách demo", Email = "demo@demo.com", PasswordHash = HashPassword("123456") });

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

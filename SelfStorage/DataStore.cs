using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelfStorage;

/// <summary>Lưu dữ liệu ra file JSON. Sau này có thể thay bằng SQLite/SQL Server mà không đổi UI nhiều.</summary>
public class DataStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SelfStorage", "data.json");

    public static DataStore Instance { get; } = Load();

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
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    static DataStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var d = JsonSerializer.Deserialize<DataStore>(File.ReadAllText(FilePath));
                if (d != null) return d;
            }
        }
        catch { /* file hỏng -> seed lại */ }
        var s = Seed();
        s.Save();
        return s;
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

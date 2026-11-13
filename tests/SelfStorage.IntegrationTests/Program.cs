using Microsoft.Data.SqlClient;
using SelfStorage;
using System.Text.Json;
using System.IO;

// Real SQL Server integration checks. Every database is uniquely named and removed.
var masterString = Environment.GetEnvironmentVariable("SELFSTORAGE_TEST_CONNECTION_STRING")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
var masterBuilder = new SqlConnectionStringBuilder(masterString) { InitialCatalog = "master" };
var databases = new List<string>();
var temporaryDirectory = Path.Combine(Path.GetTempPath(), "SelfStorageTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryDirectory);
try
{
    var connection = NewDatabase();
    var db = DataStore.Open(connection);
    Check(db.Users.Count == 2 && db.Facilities.Count == 3 && db.Units.Count == 36 && db.Rentals.Count == 0, "initial seed");
    Check(db.Users.Single(u => u.Role == "Admin").PasswordHash == DataStore.Hash("admin123"), "demo login hash");
    Check(DataStore.Open(connection).Units.Count == 36, "schema and seed are idempotent");

    db.Users.Add(new User { Id = db.NextUserId(), FullName = "Nguyễn 'Test'; --", Email = "test@example.com", PasswordHash = DataStore.Hash("secret123") });
    db.Facilities.Add(new Facility { Id = 4, Name = "Kho thử nghiệm", Address = "Địa chỉ tiếng Việt" });
    db.Units.Add(new StorageUnit { Id = 37, FacilityId = 4, Code = "TEST-01", Type = "Có điều hòa", SizeM2 = 3.75, PricePerMonth = 123456.78m });
    db.Save();
    db = DataStore.Open(connection);
    Check(db.Users.Last().FullName == "Nguyễn 'Test'; --" && db.Facilities.Last().Address == "Địa chỉ tiếng Việt", "registration and Unicode/SQL parameter roundtrip");
    Check(db.Units.Last().PricePerMonth == 123456.78m && db.Units.Last().SizeM2 == 3.75, "exact currency and area roundtrip");

    db.Facilities.Last().Address = "Đã chỉnh sửa";
    db.Units.Last().PricePerMonth = 987654.32m;
    db.Save();
    Check(DataStore.Open(connection).Units.Last().PricePerMonth == 987654.32m, "admin update");

    var unit = db.Units.Last();
    var rental = new Rental { Id = db.NextRentalId(), UserId = db.Users.Last().Id, UnitId = unit.Id,
        StartDate = new DateTime(2026, 10, 31), Months = 3, Deposit = unit.PricePerMonth,
        RentFee = unit.PricePerMonth * 3, PaymentMethod = "Chuyển khoản", PaidAt = DateTime.Now,
        QrContent = "SELFSTORAGE|R1|TEST-01|4|20261031|20270131" };
    unit.IsAvailable = false;
    db.Rentals.Add(rental);
    db.Save();
    db = DataStore.Open(connection);
    var persisted = db.Rentals.Single();
    Check(!db.Units.Last().IsAvailable && persisted.QrContent == rental.QrContent && persisted.PaidAt == rental.PaidAt
        && persisted.Deposit == rental.Deposit && persisted.RentFee == rental.RentFee && persisted.Months == 3
        && persisted.PaymentMethod == "Chuyển khoản" && persisted.ReturnedAt == null, "booking/payment/QR survive reopening");

    var stale = DataStore.Open(connection);
    persisted.IsReturned = true;
    persisted.IsLegacyRental = true; // Legacy return history has no settlement audit.
    persisted.ReturnedAt = DateTime.Now;
    db.Units.Last().IsAvailable = true;
    db.Save();
    Check(DataStore.Open(connection).Rentals.Single().ReturnedAt == persisted.ReturnedAt, "return and nullable datetime2 roundtrip");
    stale.Facilities[0].Name = "stale overwrite";
    Expect<StoreConcurrencyException>(() => stale.Save(), "stale writer rejected");
    Check(DataStore.Open(connection).Facilities[0].Name != "stale overwrite", "stale save preserves committed data");

    db = DataStore.Open(connection);
    db.Users.Add(new User { Id = 100, FullName = "Duplicate", Email = "TEST@EXAMPLE.COM", PasswordHash = "hash" });
    db.Facilities[0].Address = "must roll back";
    Expect<SqlException>(() => db.Save(), "case insensitive unique email");
    Check(DataStore.Open(connection).Facilities[0].Address != "must roll back", "constraint failure rolls back entire save");
    db = DataStore.Open(connection);
    db.Units.Add(new StorageUnit { Id = 100, FacilityId = 9999, Code = "bad-fk", Type = "Nhỏ", SizeM2 = 1, PricePerMonth = 1 });
    Expect<SqlException>(() => db.Save(), "foreign key rejects orphan unit");

    db = DataStore.Open(connection);
    db.Rentals.Single().IsReturned = false;
    db.Rentals.Single().ReturnedAt = null;
    Expect<SqlException>(() => db.Save(), "active rental cannot leave unit available");
    db.Units.Last().IsAvailable = false;
    db.Rentals.Add(new Rental { Id = 2, UserId = 2, UnitId = unit.Id, StartDate = DateTime.Today,
        Months = 1, PaidAt = DateTime.Now, Deposit = 1, RentFee = 1 });
    Expect<SqlException>(() => db.Save(), "one active rental per unit");

    db = DataStore.Open(connection);
    db.Units.Add(new StorageUnit { Id = 101, FacilityId = 4, Code = "DELETE-ME", Type = "Nhỏ", SizeM2 = 1, PricePerMonth = 10 });
    db.Save();
    db.Units.RemoveAll(u => u.Id == 101);
    db.Facilities.Add(new Facility { Id = 5, Name = "Cơ sở mới", Address = "Test" });
    db.Units.Last().FacilityId = 5;
    db.Facilities.RemoveAll(f => f.Id == 4);
    db.Save();
    Check(!DataStore.Open(connection).Facilities.Any(f => f.Id == 4) && !DataStore.Open(connection).Units.Any(u => u.Id == 101), "admin delete and move unit");

    var jsonPath = Path.Combine(temporaryDirectory, "data.json");
    var json = JsonSerializer.Serialize(DataStore.Open(connection));
    File.WriteAllText(jsonPath, json);
    var migratedConnection = NewDatabase();
    var migrated = DataStore.Open(migratedConnection, jsonPath);
    Check(migrated.Users.Count == 3 && migrated.Rentals.Count == 1 && migrated.Units.Last().FacilityId == 5, "JSON import preserves users and rental history");
    Check(File.ReadAllText(jsonPath) == json, "JSON source preserved");
    File.WriteAllText(jsonPath, "broken JSON");
    Check(DataStore.Open(migratedConnection, jsonPath).Users.Count == 3, "JSON imported only once");
    var malformedConnection = NewDatabase();
    Expect<JsonException>(() => DataStore.Open(malformedConnection, jsonPath), "malformed JSON does not silently seed");
    File.WriteAllText(jsonPath, json);
    Check(DataStore.Open(malformedConnection, jsonPath).Rentals.Count == 1, "failed import can be retried");

    var invalidConnection = NewDatabase();
    var invalidLegacy = JsonSerializer.Deserialize<DataStore>(json)!;
    invalidLegacy.Units[0].FacilityId = 9999;
    File.WriteAllText(jsonPath, JsonSerializer.Serialize(invalidLegacy));
    Expect<SqlException>(() => DataStore.Open(invalidConnection, jsonPath), "invalid JSON relations rejected atomically");
    File.WriteAllText(jsonPath, json);
    Check(DataStore.Open(invalidConnection, jsonPath).Users.Count == 3, "constraint failure leaves import retryable");

    var first = DataStore.Open(connection);
    var second = DataStore.Open(connection);
    first.Facilities[0].Address = "writer 1";
    second.Facilities[0].Address = "writer 2";
    using var barrier = new Barrier(2);
    var results = await Task.WhenAll(Task.Run(() => ConcurrentSave(first)), Task.Run(() => ConcurrentSave(second)));
    Check(results.Count(r => r) == 1, "simultaneous writers: exactly one commit without lost updates");

    var provisionedConnection = NewDatabase();
    using (var sql = new SqlConnection(provisionedConnection))
    {
        sql.Open();
        using var schema = new SqlCommand(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schema.sql")), sql);
        schema.ExecuteNonQuery();
        using var insert = new SqlCommand("INSERT dbo.Users (Id, FullName, Email, PasswordHash, Role) VALUES (7, N'Existing SQL user', N'existing@example.com', 'hash', 'Customer')", sql);
        insert.ExecuteNonQuery();
    }
    Check(DataStore.Open(provisionedConnection).Users.Single().Id == 7, "pre-provisioned SQL data is not replaced by demo seed");
    var empty = DataStore.Open(provisionedConnection);
    empty.Users.Clear();
    empty.Save();
    Check(DataStore.Open(provisionedConnection).Users.Count == 0, "initialized empty database is not reseeded");
    await WorkflowChecks.Run(NewDatabase);
    WpfChecks.Run(NewDatabase());
    Console.WriteLine("All SQL Server and WPF workflow checks passed.");

    bool ConcurrentSave(DataStore data)
    {
        barrier.SignalAndWait();
        try { data.Save(); return true; }
        catch (StoreConcurrencyException) { return false; }
    }
}
finally
{
    SqlConnection.ClearAllPools();
    foreach (var name in databases)
        Master($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
    File.Delete(Path.Combine(temporaryDirectory, "data.json"));
    Directory.Delete(temporaryDirectory);
}

string NewDatabase()
{
    var name = "SelfStorage_Test_" + Guid.NewGuid().ToString("N");
    Master($"CREATE DATABASE [{name}]");
    databases.Add(name);
    var builder = new SqlConnectionStringBuilder(masterBuilder.ConnectionString) { InitialCatalog = name };
    return builder.ConnectionString;
}

void Master(string sql)
{
    using var connection = new SqlConnection(masterBuilder.ConnectionString);
    connection.Open();
    using var command = new SqlCommand(sql, connection);
    command.ExecuteNonQuery();
}

void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}

void Expect<T>(Action action, string label) where T : Exception
{
    try { action(); }
    catch (T) { Console.WriteLine("PASS: " + label); return; }
    throw new Exception("FAIL (expected " + typeof(T).Name + "): " + label);
}

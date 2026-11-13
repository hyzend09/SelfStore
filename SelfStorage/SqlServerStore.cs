using System.Data;
using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace SelfStorage;

/// <summary>SQL Server persistence for the existing disconnected UI model.</summary>
internal sealed class SqlServerStore(string connectionString)
{
    SqlConnection Connect()
    {
        var connection = new SqlConnection(connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    internal void Initialize()
    {
        using var connection = Connect();
        using var stream = typeof(SqlServerStore).Assembly.GetManifestResourceStream("SelfStorage.Schema.sql")
            ?? throw new InvalidOperationException("Missing SQL Server schema resource.");
        using var reader = new StreamReader(stream);
        using var command = new SqlCommand(reader.ReadToEnd(), connection);
        command.ExecuteNonQuery();
    }

    internal DataStore Load()
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var db = new DataStore();
        bool hasState = false;
        using (var expiry = new SqlCommand("""
            DECLARE @units TABLE (Id int);
            DECLARE @revision bigint;
            SELECT @revision = Revision FROM dbo.StoreState WITH (UPDLOCK, HOLDLOCK) WHERE Id = 1;
            UPDATE dbo.Rentals SET HoldOutcome = 'Expired'
                OUTPUT inserted.UnitId INTO @units
                WHERE PaidAt IS NULL AND HoldOutcome IS NULL AND HoldExpiresAt <= SYSUTCDATETIME();
            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.StorageUnits SET IsAvailable = 1 WHERE Id IN (SELECT Id FROM @units);
                UPDATE dbo.StoreState SET Revision = Revision + 1 WHERE Id = 1;
            END;
            """, connection, transaction)) expiry.ExecuteNonQuery();
        Read("SELECT SchemaVersion, Revision, SYSUTCDATETIME() FROM dbo.StoreState WHERE Id = 1", r =>
        {
            hasState = true;
            if (r.GetInt32(0) != 3) throw new InvalidDataException("Phiên bản database chưa được hỗ trợ.");
            db.Revision = r.GetInt64(1);
            db.DatabaseUtcNow = DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc);
        });
        if (!hasState) throw new InvalidDataException("Thiếu thông tin phiên bản database (StoreState).");
        Read("SELECT Id, FullName, Email, PasswordHash, Role, IsActive FROM dbo.Users ORDER BY Id", r =>
            db.Users.Add(new User { Id = r.GetInt32(0), FullName = r.GetString(1), Email = r.GetString(2),
                PasswordHash = r.GetString(3), Role = r.GetString(4), IsActive = r.GetBoolean(5) }));
        Read("SELECT Id, Name, Address FROM dbo.Facilities ORDER BY Id", r =>
            db.Facilities.Add(new Facility { Id = r.GetInt32(0), Name = r.GetString(1), Address = r.GetString(2) }));
        Read("SELECT Id, FacilityId, Code, Type, SizeM2, PricePerMonth, IsAvailable FROM dbo.StorageUnits ORDER BY Id", r =>
            db.Units.Add(new StorageUnit { Id = r.GetInt32(0), FacilityId = r.GetInt32(1), Code = r.GetString(2),
                Type = r.GetString(3), SizeM2 = r.GetDouble(4), PricePerMonth = r.GetDecimal(5), IsAvailable = r.GetBoolean(6) }));
        Read("""
            SELECT r.Id, r.UserId, r.UnitId, r.StartDate, r.Months, r.Deposit, r.RentFee, r.PaymentMethod, r.PaidAt,
                r.QrContent, r.IsReturned, r.ReturnedAt, r.CustomerNameSnapshot, r.UnitCodeSnapshot,
                r.FacilityNameSnapshot, r.FacilityAddressSnapshot, r.ReceivedAt, r.ReceivedByUserId, r.ReturnRequestedAt,
                r.InspectedAt, r.InspectedByUserId, r.InspectionNotes, r.Deduction, r.RefundAmount, r.RefundedAt,
                r.RefundedByUserId, r.RefundReference, r.IsLegacyRental, r.HoldExpiresAt, r.HoldOutcome,
                r.ExtraFee, r.RequiresMaintenance, p.TransactionCode
            FROM dbo.Rentals r LEFT JOIN dbo.Payments p ON p.Id = r.Id ORDER BY r.Id
            """, r =>
            db.Rentals.Add(new Rental { Id = r.GetInt32(0), UserId = r.GetInt32(1), UnitId = r.GetInt32(2),
                StartDate = r.GetDateTime(3), Months = r.GetInt32(4), Deposit = r.GetDecimal(5), RentFee = r.GetDecimal(6),
                PaymentMethod = r.GetString(7), PaidAt = Date(r, 8), QrContent = r.GetString(9),
                IsReturned = r.GetBoolean(10), ReturnedAt = Date(r, 11),
                CustomerNameSnapshot = r.GetString(12), UnitCodeSnapshot = r.GetString(13), FacilityNameSnapshot = r.GetString(14),
                FacilityAddressSnapshot = r.GetString(15), ReceivedAt = Date(r, 16), ReceivedByUserId = Id(r, 17),
                ReturnRequestedAt = Date(r, 18), InspectedAt = Date(r, 19), InspectedByUserId = Id(r, 20),
                InspectionNotes = r.GetString(21), Deduction = r.GetDecimal(22), RefundAmount = r.GetDecimal(23),
                RefundedAt = Date(r, 24), RefundedByUserId = Id(r, 25), RefundReference = r.GetString(26), IsLegacyRental = r.GetBoolean(27),
                HoldExpiresAt = Date(r, 28), HoldOutcome = r.IsDBNull(29) ? null : r.GetString(29),
                ExtraFee = r.GetDecimal(30), RequiresMaintenance = r.GetBoolean(31),
                TransactionCode = r.IsDBNull(32) ? null : r.GetString(32) }));
        transaction.Commit();
        return db;

        void Read(string sql, Action<SqlDataReader> consume)
        {
            using var command = new SqlCommand(sql, connection, transaction);
            using var reader = command.ExecuteReader();
            while (reader.Read()) consume(reader);
        }

    }

    internal void Save(DataStore db)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        // The UI edits lists in memory. Lock and compare the revision before saving
        // the snapshot so a stale client cannot overwrite another client's changes.
        using (var check = new SqlCommand("SELECT Revision FROM dbo.StoreState WITH (UPDLOCK, HOLDLOCK) WHERE Id = 1", connection, transaction))
        {
            if (Convert.ToInt64(check.ExecuteScalar()) != db.Revision)
                throw new StoreConcurrencyException("Dữ liệu đã thay đổi ở phiên khác. Danh sách sẽ được tải lại; hãy kiểm tra và thử thao tác lại.");
        }

        if (db.PayingRentalId is int payingId)
            Execute("""
                IF NOT EXISTS (SELECT 1 FROM dbo.Rentals WHERE Id = @p0 AND PaidAt IS NULL
                    AND HoldOutcome IS NULL AND HoldExpiresAt > SYSUTCDATETIME())
                    THROW 50004, 'Booking hold expired before payment.', 1;
                """, [payingId]);

        DeleteMissing("Rentals", db.Rentals.Select(r => r.Id));
        foreach (var u in db.Users)
            Upsert("Users", ["Id", "FullName", "Email", "PasswordHash", "Role", "IsActive"],
                u.Id, u.FullName, u.Email, u.PasswordHash, u.Role, u.IsActive);
        foreach (var f in db.Facilities)
            Upsert("Facilities", ["Id", "Name", "Address"], f.Id, f.Name, f.Address);
        foreach (var u in db.Units)
            Upsert("StorageUnits", ["Id", "FacilityId", "Code", "Type", "SizeM2", "PricePerMonth", "IsAvailable"],
                u.Id, u.FacilityId, u.Code, u.Type, u.SizeM2, u.PricePerMonth, u.IsAvailable);
        foreach (var r in db.Rentals.OrderBy(r => r.IsOpen).ThenBy(r => r.Id))
        {
            Upsert("Rentals", ["Id", "UserId", "UnitId", "StartDate", "Months", "Deposit", "RentFee", "PaymentMethod", "PaidAt", "QrContent", "IsReturned", "ReturnedAt", "CustomerNameSnapshot", "UnitCodeSnapshot", "FacilityNameSnapshot", "FacilityAddressSnapshot", "ReceivedAt", "ReceivedByUserId", "ReturnRequestedAt", "InspectedAt", "InspectedByUserId", "InspectionNotes", "Deduction", "RefundAmount", "RefundedAt", "RefundedByUserId", "RefundReference", "IsLegacyRental", "HoldExpiresAt", "HoldOutcome", "ExtraFee", "RequiresMaintenance"],
                r.Id, r.UserId, r.UnitId, r.StartDate, r.Months, r.Deposit, r.RentFee, r.PaymentMethod, r.PaidAt,
                r.QrContent, r.IsReturned, r.ReturnedAt, r.CustomerNameSnapshot, r.UnitCodeSnapshot, r.FacilityNameSnapshot,
                r.FacilityAddressSnapshot, r.ReceivedAt, r.ReceivedByUserId, r.ReturnRequestedAt, r.InspectedAt,
                r.InspectedByUserId, r.InspectionNotes, r.Deduction, r.RefundAmount, r.RefundedAt, r.RefundedByUserId,
                r.RefundReference, r.IsLegacyRental, r.HoldExpiresAt, r.HoldOutcome, r.ExtraFee, r.RequiresMaintenance);
            if (r.PaidAt != null && r.TransactionCode != null)
                Execute("""
                    IF NOT EXISTS (SELECT 1 FROM dbo.Payments WHERE Id = @p0)
                        INSERT dbo.Payments (Id, Amount, TransactionCode, Method, PaidAt) VALUES (@p0, @p1, @p2, @p3, @p4);
                    """, [r.Id, r.Total, r.TransactionCode, r.PaymentMethod, r.PaidAt]);
        }

        DeleteMissing("StorageUnits", db.Units.Select(u => u.Id));
        DeleteMissing("Facilities", db.Facilities.Select(f => f.Id));
        DeleteMissing("Users", db.Users.Select(u => u.Id));

        // An active rental must keep its unit unavailable (including during JSON import).
        Execute("IF EXISTS (SELECT 1 FROM dbo.Rentals r JOIN dbo.StorageUnits u ON u.Id = r.UnitId WHERE r.IsReturned = 0 AND r.HoldOutcome IS NULL AND u.IsAvailable = 1) THROW 50002, 'An active rental cannot have an available unit.', 1;");
        Execute("UPDATE dbo.StoreState SET Revision = Revision + 1 WHERE Id = 1");
        transaction.Commit();
        db.Revision++;

        void DeleteMissing(string table, IEnumerable<int> ids) => Execute(
            $"DELETE FROM dbo.[{table}] WHERE Id NOT IN (SELECT CAST([value] AS int) FROM OPENJSON(@p0))",
            [JsonSerializer.Serialize(ids)]);

        void Upsert(string table, string[] columns, params object?[] values)
        {
            // Identifiers come only from the fixed mappings above; all values are parameters.
            var assignments = string.Join(",", columns.Skip(1).Select((c, i) => $"[{c}]=@p{i + 1}"));
            var names = string.Join(",", columns.Select(c => $"[{c}]"));
            var parameters = string.Join(",", columns.Select((_, i) => $"@p{i}"));
            Execute($"UPDATE dbo.[{table}] SET {assignments} WHERE Id=@p0; " +
                $"IF @@ROWCOUNT=0 INSERT dbo.[{table}] ({names}) VALUES ({parameters});", values, columns);
        }

        void Execute(string sql, object?[]? values = null, string[]? columns = null)
        {
            values ??= [];
            using var command = new SqlCommand(sql, connection, transaction);
            for (int i = 0; i < values.Length; i++)
            {
                var parameter = command.Parameters.AddWithValue($"@p{i}", values[i] ?? DBNull.Value);
                // Preserve the full datetime2 precision instead of inferring datetime.
                if (values[i] is DateTime || columns?[i].EndsWith("At") == true) parameter.SqlDbType = SqlDbType.DateTime2;
                else if (columns?[i].EndsWith("UserId") == true) parameter.SqlDbType = SqlDbType.Int;
            }
            command.ExecuteNonQuery();
        }
    }

    static DateTime? Date(SqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetDateTime(column);
    static int? Id(SqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetInt32(column);
}

public sealed class StoreConcurrencyException(string message) : Exception(message);

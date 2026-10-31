using Microsoft.Data.SqlClient;
using SelfStorage;
using System.Text.Json;
using System.IO;

internal static class WorkflowChecks
{
    public static async Task Run(Func<string> newDatabase)
    {
        var connection = newDatabase();
        var customer = DataStore.Open(connection);
        var admin = DataStore.Open(connection);
        customer.CurrentUser = customer.Users.Single(u => u.Id == 2);
        admin.CurrentUser = admin.Users.Single(u => u.Id == 1);
        Expect(() => customer.Register("Test", "@", "123456"), "registration rejects invalid email");
        Expect(() => customer.SaveFacility(new Facility { Name = "unauthorized" }, null), "customer cannot administer catalog");
        var draft = Clone(admin.Units[0]);
        draft.PricePerMonth = 123456.78m;
        admin.SaveUnit(draft, admin.Units[0]);
        Expect(() => customer.Book(1, DateTime.Today, 1, 300000, "Thẻ ngân hàng"), "booking rechecks quoted price");
        var rental = customer.Book(1, DateTime.Today, 1, 123456.78m, "Thẻ ngân hàng");
        Check(rental.StatusText == "Chờ nhận kho" && rental.UnitCodeSnapshot != "" && rental.CustomerNameSnapshot != "", "booking reserves unit and freezes history");
        Check(!DataStore.Open(connection).Units.Single(u => u.Id == 1).IsAvailable, "reserved unit cannot be booked again");
        Expect(() => customer.Book(1, DateTime.Today, 1, rental.Deposit, "Thẻ ngân hàng"), "duplicate booking rejected");
        Expect(() => customer.RequestReturn(rental.Id), "cannot return before pickup");
        Expect(() => customer.ReceiveRental(rental.QrContent, rental.Id), "pickup requires admin");
        Expect(() => admin.ReceiveRental("SELFSTORAGE|forged", rental.Id), "forged QR rejected");
        Expect(() => admin.ReceiveRental(rental.QrContent, rental.Id + 1), "QR must match selected rental");
        Check(admin.VerifyPickup(rental.QrContent).Id == rental.Id, "valid QR lookup");
        admin.ReceiveRental(rental.QrContent, rental.Id);
        var received = DataStore.Open(connection).Rentals.Single(r => r.Id == rental.Id);
        Check(received.ReceivedAt != null && received.ReceivedByUserId == 1 && received.StatusText == "Đang thuê", "pickup audit survives reopening");
        Expect(() => admin.ReceiveRental(rental.QrContent, rental.Id), "QR cannot be used twice");

        customer.Register("Người khác", "other@example.com", "123456");
        var other = DataStore.Open(connection);
        other.CurrentUser = other.Users.Single(u => u.Email == "other@example.com");
        Expect(() => other.RequestReturn(rental.Id), "cannot return another customer's rental");
        Expect(() => admin.InspectReturn(rental.Id, 0, "OK"), "cannot inspect before return request");
        customer.RequestReturn(rental.Id);
        var requested = DataStore.Open(connection);
        Check(requested.Rentals.Single().ReturnRequestedAt != null && !requested.Units.Single(u => u.Id == 1).IsAvailable
            && !requested.Rentals.Single().IsReturned, "request does not release the unit");
        Expect(() => customer.RequestReturn(rental.Id), "duplicate return request rejected");
        Expect(() => customer.InspectReturn(rental.Id, 0, "OK"), "inspection requires admin");
        Expect(() => admin.InspectReturn(rental.Id, -1, "bad"), "negative deduction rejected");
        Expect(() => admin.InspectReturn(rental.Id, rental.Deposit + 1, "bad"), "deduction cannot exceed deposit");
        Expect(() => admin.InspectReturn(rental.Id, 0.001m, "bad"), "deduction precision validated");
        Expect(() => admin.InspectReturn(rental.Id, 10, ""), "inspection report is required");
        Expect(() => admin.ConfirmRefund(rental.Id, "premature"), "cannot settle before inspection");
        admin.InspectReturn(rental.Id, 10.12m, "Khấu trừ vệ sinh: 10,12 đồng");
        var inspected = DataStore.Open(connection);
        var pending = inspected.Rentals.Single();
        Check(pending.RefundAmount == 123446.66m && pending.InspectedByUserId == 1 && pending.InspectedAt != null
            && pending.InspectionNotes.Contains("vệ sinh") && pending.StatusText == "Chờ hoàn cọc"
            && !inspected.Units.Single(u => u.Id == 1).IsAvailable, "inspection persists exact refund and keeps unit held");
        Expect(() => admin.InspectReturn(rental.Id, 0, "again"), "inspection cannot be overwritten");
        Expect(() => customer.ConfirmRefund(rental.Id, "unauthorized"), "refund requires admin");
        Expect(() => admin.ConfirmRefund(rental.Id, ""), "refund requires payment reference");
        admin.ConfirmRefund(rental.Id, "BANK-0001");
        var completed = DataStore.Open(connection);
        var closed = completed.Rentals.Single();
        Check(closed.IsReturned && closed.ReturnedAt != null && closed.RefundedAt != null && closed.RefundedByUserId == 1
            && closed.RefundReference == "BANK-0001" && completed.Units.Single(u => u.Id == 1).IsAvailable, "settlement audit closes rental and releases unit atomically");
        var next = customer.Book(1, DateTime.Today, 1, 123456.78m, "Ví điện tử");
        Expect(() => admin.ConfirmRefund(rental.Id, "duplicate"), "duplicate refund rejected after rebooking");
        Check(!DataStore.Open(connection).Units.Single(u => u.Id == 1).IsAvailable, "old refund cannot release a new booking");

        foreach (var fullDeduction in new[] { false, true })
        {
            var unitId = fullDeduction ? 3 : 2;
            customer.Reload();
            var price = customer.Units.Single(u => u.Id == unitId).PricePerMonth;
            var item = customer.Book(unitId, DateTime.Today, 1, price, "Chuyển khoản");
            admin.ReceiveRental(item.QrContent, item.Id);
            customer.RequestReturn(item.Id);
            admin.InspectReturn(item.Id, fullDeduction ? item.Deposit : 0, fullDeduction ? "Hư hỏng bằng tiền cọc" : "Kho tốt");
            admin.ConfirmRefund(item.Id, fullDeduction ? "" : "CASH-0001");
            var saved = DataStore.Open(connection).Rentals.Single(r => r.Id == item.Id);
            Check(saved.RefundAmount == (fullDeduction ? 0 : item.Deposit) && saved.RefundedAt != null,
                fullDeduction ? "zero refund still requires explicit settlement" : "full deposit refund");
        }

        var future = customer.Book(4, DateTime.Today.AddDays(5), 1, customer.Units.Single(u => u.Id == 4).PricePerMonth, "Thẻ ngân hàng");
        Expect(() => admin.ReceiveRental(future.QrContent, future.Id), "pickup before start date rejected");
        Expect(() => customer.Book(5, DateTime.Today.AddYears(6), 1, 650000, "Thẻ ngân hàng"), "booking date bounded");
        var originalUnit = Clone(admin.Units.Single(u => u.Id == 2));
        var changedUnit = Clone(originalUnit);
        changedUnit.Code = "RENAMED"; changedUnit.PricePerMonth = 345678.90m;
        admin.SaveUnit(changedUnit, originalUnit);
        var originalFacility = Clone(admin.Facilities.Single(f => f.Id == originalUnit.FacilityId));
        var changedFacility = Clone(originalFacility);
        changedFacility.Name = "Cơ sở đổi tên"; changedFacility.Address = "Địa chỉ đổi";
        admin.SaveFacility(changedFacility, originalFacility);
        var history = DataStore.Open(connection).Rentals.Single(r => r.UnitId == 2);
        Check(history.UnitCode == originalUnit.Code && history.FacilityName == originalFacility.Name
            && history.FacilityAddressSnapshot == originalFacility.Address, "history does not follow later unit/facility edits");
        Expect(() => admin.SaveUnit(originalUnit, originalUnit), "stale catalog edit rejected without overwrite");
        var badUnit = Clone(admin.Units.Single(u => u.Id == 5));
        badUnit.SizeM2 = double.NaN;
        Expect(() => admin.SaveUnit(badUnit, admin.Units.Single(u => u.Id == 5)), "non-finite area rejected");
        badUnit.SizeM2 = 5; badUnit.PricePerMonth = 0.001m;
        Expect(() => admin.SaveUnit(badUnit, admin.Units.Single(u => u.Id == 5)), "money precision rejected before SQL");
        var stale = DataStore.Open(connection);
        stale.CurrentUser = stale.Users.Single(u => u.Id == 2);
        customer.Register("Fresh", "fresh@example.com", "123456");
        stale.Reload();
        Check(stale.Users.Any(u => u.Email == "fresh@example.com") && stale.CurrentUser?.Id == 2, "refresh reads SQL and preserves session");

        var left = DataStore.Open(connection); left.CurrentUser = left.Users.Single(u => u.Id == 2);
        var right = DataStore.Open(connection); right.CurrentUser = right.Users.Single(u => u.Id == 2);
        using var barrier = new Barrier(2);
        var race = await Task.WhenAll(Task.Run(() => Race(left)), Task.Run(() => Race(right)));
        Check(race.Count(x => x) == 1 && DataStore.Open(connection).Rentals.Count(r => r.UnitId == 6 && !r.IsReturned) == 1,
            "simultaneous booking commits once");
        Check(left.CurrentUser != null && right.CurrentUser != null, "conflicting client stays usable");

        var legacyConnection = newDatabase();
        var schema = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schema.sql"));
        var versionOne = schema[..schema.IndexOf("IF EXISTS (SELECT 1 FROM dbo.StoreState WHERE SchemaVersion NOT", StringComparison.Ordinal)] + "COMMIT;";
        Sql(legacyConnection, versionOne);
        Sql(legacyConnection, """
            INSERT dbo.Users VALUES (1,N'Admin','admin@demo.com','hash','Admin'),(2,N'Khách cũ','legacy@example.com','hash','Customer');
            INSERT dbo.Facilities VALUES (1,N'Cơ sở cũ',N'Địa chỉ cũ');
            INSERT dbo.StorageUnits VALUES (1,1,N'OLD',N'Nhỏ',2,300000,0),(2,1,N'OLD-RETURNED',N'Nhỏ',2,300000,1);
            INSERT dbo.Rentals VALUES (1,2,1,GETDATE(),1,300000,300000,N'Thẻ ngân hàng',GETDATE(),N'OLD-QR',0,NULL),
                (2,2,2,GETDATE(),1,300000,300000,N'Thẻ ngân hàng',GETDATE(),N'CLOSED-QR',1,GETDATE());
            UPDATE dbo.StoreState SET Revision=7;
            """);
        var migrated = DataStore.Open(legacyConnection);
        Check(migrated.Rentals.Count == 2 && migrated.Rentals[0].QrContent == "OLD-QR" && migrated.Rentals[0].UnitCode == "OLD"
            && migrated.Rentals.All(r => r.IsLegacyRental), "version 1 migration preserves history and QR");
        Check(migrated.Rentals[1].RefundedAt == null && migrated.Rentals[1].RefundText.Contains("Chưa có"), "migration does not invent old refund records");
        migrated.CurrentUser = migrated.Users.Single(u => u.Id == 2);
        migrated.RequestReturn(1);
        Check(DataStore.Open(legacyConnection).Rentals[0].ReturnRequestedAt != null, "legacy active rental can request return");
        Check((int)Scalar(legacyConnection, "SELECT SchemaVersion FROM dbo.StoreState") == 2, "schema upgraded to version 2");

        bool Race(DataStore data)
        {
            barrier.SignalAndWait();
            try { data.Book(6, DateTime.Today, 1, 650000, "Thẻ ngân hàng"); return true; }
            catch (Exception ex) when (ex is InvalidOperationException or StoreConcurrencyException) { return false; }
        }
    }

    static T Clone<T>(T item) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(item))!;
    static void Expect(Action action, string label)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or StoreConcurrencyException) { Check(true, label); return; }
        throw new Exception("FAIL: " + label);
    }
    static void Check(bool result, string label)
    {
        if (!result) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
    }
    static void Sql(string connectionString, string sql) { using var connection = new SqlConnection(connectionString); connection.Open(); using var command = new SqlCommand(sql, connection); command.ExecuteNonQuery(); }
    static object Scalar(string connectionString, string sql) { using var connection = new SqlConnection(connectionString); connection.Open(); using var command = new SqlCommand(sql, connection); return command.ExecuteScalar()!; }
}

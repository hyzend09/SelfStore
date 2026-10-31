using SelfStorage;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class WpfChecks
{
    public static void Run(string connection)
    {
        Environment.SetEnvironmentVariable("SELFSTORAGE_CONNECTION_STRING", connection);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var unhandled = new List<Exception>();
            application.DispatcherUnhandledException += (_, e) => { unhandled.Add(e.Exception); e.Handled = true; };
            application.Startup += async (_, _) =>
            {
                try
                {
                    var db = DataStore.Instance;
                    db.CurrentUser = db.Users.Single(u => u.Id == 1);
                    var original = db.Units[0];
                    var draft = new StorageUnit { Id = original.Id, FacilityId = original.FacilityId, Code = original.Code,
                        Type = original.Type, SizeM2 = original.SizeM2, PricePerMonth = 123456.78m, IsAvailable = true };
                    db.SaveUnit(draft, original);
                    original = db.Units[0];
                    var edit = new UnitEditWindow(original);
                    Check(decimal.Parse(Field<TextBox>(edit, "txtPrice").Text) == 123456.78m, "WPF editor keeps fractional price");
                    Field<TextBox>(edit, "txtSize").Text = "NaN";
                    Click(edit, "Save_Click");
                    Check(Field<TextBlock>(edit, "lblError").Text.Contains("hữu hạn"), "WPF rejects NaN before save");
                    edit.Close();
                    var canceledEdit = new UnitEditWindow(original);
                    Dialog(canceledEdit, () => { Field<TextBox>(canceledEdit, "txtPrice").Text = "999"; canceledEdit.DialogResult = false; });
                    Check(original.PricePerMonth == 123456.78m, "canceling edit does not mutate live catalog");
                    var savedEdit = new UnitEditWindow(original);
                    Check(Dialog(savedEdit, () => Click(savedEdit, "Save_Click")) == true && savedEdit.Unit.PricePerMonth == 123456.78m,
                        "saving unchanged price preserves decimals");

                    var canceled = new PaymentWindow(new Rental { Deposit = 10, RentFee = 20 });
                    var result = Dialog(canceled, () =>
                    {
                        Click(canceled, "Confirm_Click");
                        _ = canceled.Dispatcher.InvokeAsync(async () => { await Task.Delay(100); canceled.DialogResult = false; });
                    });
                    await Task.Delay(1200);
                    Check(result == false && canceled.SelectedMethod == "" && unhandled.Count == 0, "cancel during payment no longer throws or confirms");
                    var paid = new PaymentWindow(new Rental { Deposit = 10, RentFee = 20 });
                    Check(Dialog(paid, () => Click(paid, "Confirm_Click")) == true && paid.SelectedMethod == "Thẻ ngân hàng",
                        "payment success closes once without cancellation error");
                    Check(unhandled.Count == 0, "payment windows have no unhandled dispatcher errors");

                    var adminWindow = new AdminWindow();
                    var external = DataStore.Open(connection);
                    external.CurrentUser = external.Users.Single(u => u.Id == 1);
                    external.SaveFacility(new Facility { Name = "Cơ sở từ máy khác", Address = "Test" }, null);
                    Click(adminWindow, "Refresh_Click");
                    var facilityGrid = Field<DataGrid>(adminWindow, "gridFacilities");
                    Check(facilityGrid.Items.Cast<Facility>().Any(f => f.Name == "Cơ sở từ máy khác"), "admin refresh reads external SQL changes");
                    adminWindow.Close();

                    db.CurrentUser = db.Users.Single(u => u.Id == 2);
                    var rental = db.Book(1, DateTime.Today, 1, 123456.78m, "Thẻ ngân hàng");
                    db.CurrentUser = db.Users.Single(u => u.Id == 1);
                    var pickup = new RentalOperationsWindow(rental.Id, RentalOperation.Receive);
                    Check(Dialog(pickup, () => { Field<TextBox>(pickup, "txtQr").Text = rental.QrContent; Click(pickup, "Confirm_Click"); }) == true,
                        "WPF admin pickup form confirms valid QR");
                    db.CurrentUser = db.Users.Single(u => u.Id == 2);
                    db.RequestReturn(rental.Id);
                    db.CurrentUser = db.Users.Single(u => u.Id == 1);
                    var inspection = new RentalOperationsWindow(rental.Id, RentalOperation.Inspect);
                    Check(Dialog(inspection, () =>
                    {
                        Field<TextBox>(inspection, "txtDeduction").Text = "10";
                        Field<TextBox>(inspection, "txtNotes").Text = "Kiểm tra giao diện: vệ sinh";
                        Click(inspection, "Confirm_Click");
                    }) == true, "WPF inspection form saves report");
                    var refund = new RentalOperationsWindow(rental.Id, RentalOperation.Refund);
                    Check(Dialog(refund, () =>
                    {
                        Field<TextBox>(refund, "txtReference").Text = "UI-REFUND-01";
                        Field<CheckBox>(refund, "chkConfirmed").IsChecked = true;
                        Click(refund, "Confirm_Click");
                    }) == true, "WPF refund form records manual settlement");
                    db.CurrentUser = db.Users.Single(u => u.Id == 2);
                    var mine = new MyRentalsView();
                    var grid = Field<DataGrid>(mine, "grid");
                    grid.SelectedIndex = 0;
                    Check(Field<TextBlock>(mine, "lblDetails").Text.Contains("UI-REFUND-01"), "customer sees settlement reference");
                    var browse = new BrowseView();
                    var customerWindow = new MainWindow();
                    customerWindow.Close();
                    Check(unhandled.Count == 0, "customer and admin WPF views initialize without errors");
                    var booking = new BookingWindow(db.Units.Single(u => u.Id == 2));
                    Check(DriveBooking(booking, confirm: true) == true, "WPF booking flows through payment and QR");
                    var uiBooked = DataStore.Open(connection).Rentals.Single(r => r.UnitId == 2);
                    Check(uiBooked.ReceivedAt == null && uiBooked.StatusText == "Chờ nhận kho", "WPF booking persists pending pickup");
                    var canceledBooking = new BookingWindow(db.Units.Single(u => u.Id == 3));
                    DriveBooking(canceledBooking, confirm: false);
                    Check(!DataStore.Open(connection).Rentals.Any(r => r.UnitId == 3), "canceling payment creates no rental");
                    external.ReceiveRental(uiBooked.QrContent, uiBooked.Id);
                    mine.Reload();
                    Check(Field<DataGrid>(mine, "grid").Items.Cast<Rental>().Single(r => r.Id == uiBooked.Id).ReceivedAt != null,
                        "customer refresh sees pickup from another SQL session");
                    Check(unhandled.Count == 0, "booking/payment/QR flow has no dispatcher errors");
                }
                catch (Exception ex) { failure = ex; }
                finally { application.Shutdown(); }
            };
            application.Run();
            if (unhandled.Count > 0) failure ??= unhandled[0];
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90))) throw new Exception("WPF checks timed out.");
        if (failure != null) throw new Exception("WPF checks failed.", failure);
    }

    static bool? Dialog(Window window, Action action)
    {
        window.Opacity = 0;
        window.ShowInTaskbar = false;
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(action);
        return window.ShowDialog();
    }

    static bool? DriveBooking(BookingWindow booking, bool confirm)
    {
        return Dialog(booking, () =>
        {
            bool clickedPayment = false;
            var driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            driver.Tick += (_, _) =>
            {
                var payment = Application.Current.Windows.OfType<PaymentWindow>().FirstOrDefault(w => w.IsVisible);
                if (payment != null && !clickedPayment)
                {
                    clickedPayment = true;
                    if (confirm) Click(payment, "Confirm_Click");
                    else payment.DialogResult = false;
                }
                var qr = Application.Current.Windows.OfType<QrWindow>().FirstOrDefault(w => w.IsVisible);
                if (qr != null) qr.DialogResult = true;
            };
            driver.Start();
            try { Click(booking, "Pay_Click"); }
            finally { driver.Stop(); }
            if (!confirm) booking.DialogResult = false;
        });
    }
    static T Field<T>(FrameworkElement control, string name) => (T)control.FindName(name);
    static void Click(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(target, [target, new RoutedEventArgs()]);
    static void Check(bool result, string label)
    {
        if (!result) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
    }
}

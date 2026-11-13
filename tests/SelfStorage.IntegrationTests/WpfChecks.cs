using SelfStorage;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class WpfChecks
{
    public static void Run(string connection, string isolatedLegacyPath)
    {
        Environment.SetEnvironmentVariable("SELFSTORAGE_CONNECTION_STRING", connection);
        Environment.SetEnvironmentVariable("SELFSTORAGE_LEGACY_JSON_PATH", isolatedLegacyPath);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/SelfStorage;component/Theme.xaml", UriKind.Relative)
            });
            var unhandled = new List<Exception>();
            application.DispatcherUnhandledException += (_, e) => { unhandled.Add(e.Exception); e.Handled = true; };
            application.Startup += async (_, _) =>
            {
                try
                {
                    var db = DataStore.Instance;
                    Check(db.Rentals.Count == 0 && db.Units.Count == 36 && db.Units[0].IsAvailable,
                        "WPF fixture uses isolated JSON path and starts with seeded units");
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
                    external.Facilities.Add(new Facility { Id = 4, Name = "Cơ sở từ máy khác", Address = "Test" });
                    external.Save();
                    Click(adminWindow, "Refresh_Click");
                    var locationFilter = Field<ComboBox>(adminWindow, "cbFilter");
                    Check(locationFilter.Items.Cast<Facility>().Any(f => f.Name == "Cơ sở từ máy khác"), "admin refresh reads external location data");
                    Check(Field<DataGrid>(adminWindow, "gridCustomers").Items.Cast<User>().All(u => u.Role == "Customer"),
                        "admin customer grid excludes administrators");
                    adminWindow.Close();

                    db.CurrentUser = db.Users.Single(u => u.Id == 2);
                    var rental = WorkflowChecks.BookPaid(db, 1, DateTime.Today, 1, 123456.78m, "Thẻ ngân hàng");
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
                        Check(inspection.IsVisible && Field<TextBlock>(inspection, "lblValidation").Text.Contains("QR") &&
                            DataStore.Open(connection).Rentals.Single(r => r.Id == rental.Id).InspectedAt == null,
                            "WPF inspection stays open and explains missing QR");
                        Field<TextBox>(inspection, "txtQr").Text = rental.QrContent;
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
                    Field<TextBox>(browse, "txtMinArea").Text = "5";
                    Field<TextBox>(browse, "txtMaxPrice").Text = "700000";
                    Click(browse, "Filter_Changed");
                    var visibleUnits = Field<DataGrid>(browse, "grid").Items.Cast<StorageUnit>().ToList();
                    Check(visibleUnits.Count > 0 && visibleUnits.All(u => u.SizeM2 >= 5 && u.PricePerMonth <= 700000),
                        "customer combines size and price filters");
                    var customerWindow = new MainWindow();
                    customerWindow.Close();
                    Check(unhandled.Count == 0, "customer and admin WPF views initialize without errors");
                    var booking = new BookingWindow(db.Units.Single(u => u.Id == 2));
                    Check(DriveBooking(booking, confirm: true) == true, "WPF booking flows through payment and QR");
                    var uiBooked = DataStore.Open(connection).Rentals.Single(r => r.UnitId == 2);
                    Check(uiBooked.ReceivedAt == null && uiBooked.StatusText == "Chờ nhận kho", "WPF booking persists pending pickup");
                    var canceledBooking = new BookingWindow(db.Units.Single(u => u.Id == 3));
                    DriveBooking(canceledBooking, confirm: false);
                    var unpaid = DataStore.Open(connection).Rentals.Single(r => r.UnitId == 3);
                    Check(unpaid.IsAwaitingPayment && unpaid.QrContent == "", "closing payment preserves the unpaid hold without issuing a QR");
                    mine.Reload();
                    grid.SelectedItem = grid.Items.Cast<Rental>().Single(r => r.Id == unpaid.Id);
                    DrivePayment(mine.Dispatcher, () => Click(mine, "PayHold_Click"), confirm: true);
                    var resumed = DataStore.Open(connection).Rentals.Single(r => r.Id == unpaid.Id);
                    Check(resumed.PaidAt != null && resumed.QrContent != "", "customer resumes payment for the same hold and receives QR");
                    db.CurrentUser = db.Users.Single(u => u.Id == 1);
                    var monthly = new AdminWindow();
                    var monthPicker = Field<DatePicker>(monthly, "dpRevenueMonth");
                    var summary = Field<TextBlock>(monthly, "lblStats");
                    decimal paidRent = db.Rentals.Where(r => r.PaidAt != null).Sum(r => r.RentFee);
                    Check(summary.Text.Contains($"{paidRent:N2} ₫"), "admin monthly revenue sums rent without deposits");
                    monthPicker.SelectedDate = DateTime.Today.AddMonths(-1);
                    Check(summary.Text.Contains($"{0m:N2} ₫"), "admin monthly revenue follows the selected month");
                    monthly.Close();
                    db.CurrentUser = db.Users.Single(u => u.Id == 2);
                    external.ReceiveRental(uiBooked.QrContent, uiBooked.Id);
                    mine.Reload();
                    Check(Field<DataGrid>(mine, "grid").Items.Cast<Rental>().Single(r => r.Id == uiBooked.Id).ReceivedAt != null,
                        "customer refresh sees pickup from another SQL session");
                    external.SetCustomerActive(uiBooked.UserId, false);
                    db.CurrentUser = db.Users.Single(u => u.Id == 1);
                    db.Reload();
                    var exceptionalReturn = new RentalOperationsWindow(uiBooked.Id, RentalOperation.StartReturn);
                    Check(Dialog(exceptionalReturn, () => Click(exceptionalReturn, "Confirm_Click")) == true,
                        "WPF admin can start a return for a locked customer");
                    var manualInspection = new RentalOperationsWindow(uiBooked.Id, RentalOperation.Inspect);
                    Check(Dialog(manualInspection, () =>
                    {
                        var identity = Field<CheckBox>(manualInspection, "chkIdentityVerified");
                        Check(identity.IsVisible, "WPF offers manual identity verification for the locked customer");
                        identity.IsChecked = true;
                        Field<TextBox>(manualInspection, "txtNotes").Text = "Kho đã trả nguyên trạng.";
                        Click(manualInspection, "Confirm_Click");
                    }) == true, "WPF saves a return with explicit manual verification");
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
        => Dialog(booking, () => DrivePayment(booking.Dispatcher, () => Click(booking, "Pay_Click"), confirm));

    static void DrivePayment(Dispatcher dispatcher, Action openPayment, bool confirm)
    {
        bool clickedPayment = false;
        var driver = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(20) };
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
        try { openPayment(); }
        finally { driver.Stop(); }
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

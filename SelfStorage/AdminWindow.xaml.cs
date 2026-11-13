using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SelfStorage;

public partial class AdminWindow : Window
{
    readonly DataStore _db = DataStore.Instance;
    bool _ready;
    readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public AdminWindow()
    {
        InitializeComponent();
        dpRevenueMonth.SelectedDate = DateTime.Today;
        lblUser.Text = $"Admin: {_db.CurrentUser?.FullName}";
        ReloadAll();
        _ready = true;
        _holdTimer.Tick += (_, _) =>
        {
            if (!_db.Rentals.Any(r => r.IsAwaitingPayment && r.HoldDeadlineLocal <= DateTime.Now)) return;
            RefreshData();
        };
        _holdTimer.Start();
        Closed += (_, _) => _holdTimer.Stop();
    }

    // ---------- LOAD ----------
    void ReloadAll()
    {
        LoadFilter();
        LoadUnits();
        LoadRentals();
        LoadCustomers();
        LoadStats();
    }

    void RefreshData()
    {
        UiActions.Try(_db.Reload);
        ReloadAll();
    }

    void LoadFilter()
    {
        var keepId = (cbFilter.SelectedItem as Facility)?.Id ?? 0;
        var wasReady = _ready; _ready = false;
        var list = new List<Facility> { new() { Id = 0, Name = "Tất cả" } };
        list.AddRange(_db.Facilities);
        cbFilter.ItemsSource = list;
        cbFilter.SelectedItem = list.FirstOrDefault(f => f.Id == keepId) ?? list[0];
        _ready = wasReady;
    }

    void LoadUnits()
    {
        IEnumerable<StorageUnit> q = _db.Units;
        if (cbFilter.SelectedItem is Facility f && f.Id != 0) q = q.Where(u => u.FacilityId == f.Id);
        gridUnits.ItemsSource = q.OrderBy(u => u.FacilityId).ThenBy(u => u.Code).ToList();
    }

    void LoadRentals()
    {
        var id = (gridRentals.SelectedItem as Rental)?.Id;
        var rentals = _db.Rentals.OrderByDescending(r => r.Id).ToList();
        gridRentals.ItemsSource = rentals;
        gridRentals.SelectedItem = rentals.FirstOrDefault(r => r.Id == id);
    }

    void LoadCustomers()
    {
        var id = (gridCustomers.SelectedItem as User)?.Id;
        var customers = _db.Users.Where(u => u.Role == "Customer").OrderBy(u => u.FullName).ToList();
        gridCustomers.ItemsSource = customers;
        gridCustomers.SelectedItem = customers.FirstOrDefault(u => u.Id == id);
    }

    void LoadStats()
    {
        var month = dpRevenueMonth.SelectedDate ?? DateTime.Today;
        int free = _db.Units.Count(u => u.IsAvailable);
        int rented = _db.Rentals.Count(r => r.IsOpen && r.PaidAt != null && r.WasHandedOver);
        decimal revenue = _db.Rentals.Where(r => r.PaidAtLocal is DateTime paid && paid.Year == month.Year && paid.Month == month.Month)
            .Sum(r => r.RentFee);
        lblStats.Text = $"Còn trống: {free}  |  Đang thuê: {rented}  |  Phí thuê thu trong tháng {month:MM/yyyy}: {revenue:N2} ₫";
    }

    void RevenueMonth_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) LoadStats(); }

    void ToggleCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (gridCustomers.SelectedItem is not User user) { MessageBox.Show("Hãy chọn tài khoản khách hàng."); return; }
        var action = user.IsActive ? "Khóa" : "Mở khóa";
        if (MessageBox.Show($"{action} tài khoản {user.Email}?", "Tài khoản khách hàng",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        UiActions.Try(() => _db.SetCustomerActive(user.Id, !user.IsActive));
        ReloadAll();
    }

    void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) LoadUnits(); }
    void Refresh_Click(object sender, RoutedEventArgs e) => RefreshData();

    // ---------- KHO ----------
    void AddUnit_Click(object sender, RoutedEventArgs e)
    {
        if (!UiActions.Try(_db.Reload)) return;
        if (_db.Facilities.Count == 0) { MessageBox.Show("Danh mục chưa có vị trí kho. Hãy liên hệ người quản trị dữ liệu."); return; }
        var w = new UnitEditWindow(null) { Owner = this };
        if (w.ShowDialog() != true) return;
        UiActions.Try(() => _db.SaveUnit(w.Unit, null));
        ReloadAll();
    }

    void EditUnit_Click(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs && gridUnits.SelectedItem == null) return;
        if (gridUnits.SelectedItem is not StorageUnit u) { MessageBox.Show("Hãy chọn một kho."); return; }
        if (!UiActions.Try(_db.Reload)) return;
        u = _db.Units.FirstOrDefault(x => x.Id == u.Id)!;
        if (u == null) { ReloadAll(); return; }
        var w = new UnitEditWindow(u) { Owner = this };
        if (w.ShowDialog() != true) return;
        UiActions.Try(() => _db.SaveUnit(w.Unit, u));
        ReloadAll();
    }

    void DeleteUnit_Click(object sender, RoutedEventArgs e)
    {
        if (gridUnits.SelectedItem is not StorageUnit u) { MessageBox.Show("Hãy chọn một kho."); return; }
        if (_db.Rentals.Any(r => r.UnitId == u.Id))
        { MessageBox.Show("Kho này đã có lịch sử thuê nên không thể xóa."); return; }
        if (MessageBox.Show($"Xóa kho {u.Code}?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        UiActions.Try(() => _db.DeleteUnit(u.Id));
        ReloadAll();
    }

    void Logout_Click(object sender, RoutedEventArgs e)
    {
        _db.CurrentUser = null;
        new LoginWindow().Show();
        Close();
    }

    void Receive_Click(object sender, RoutedEventArgs e) => OpenOperation(RentalOperation.Receive);
    void Inspect_Click(object sender, RoutedEventArgs e) => OpenOperation(RentalOperation.Inspect);
    void Refund_Click(object sender, RoutedEventArgs e) => OpenOperation(RentalOperation.Refund);

    void StartReturn_Click(object sender, RoutedEventArgs e) => OpenOperation(RentalOperation.StartReturn);

    void OpenOperation(RentalOperation operation)
    {
        if (gridRentals.SelectedItem is not Rental rental) { MessageBox.Show("Hãy chọn một đơn thuê."); return; }
        UiActions.Try(() =>
        {
            _db.Reload();
            new RentalOperationsWindow(rental.Id, operation) { Owner = this }.ShowDialog();
        });
        ReloadAll();
    }
}

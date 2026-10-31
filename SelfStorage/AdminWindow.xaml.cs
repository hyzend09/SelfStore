using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SelfStorage;

public partial class AdminWindow : Window
{
    readonly DataStore _db = DataStore.Instance;
    bool _ready;

    public AdminWindow()
    {
        InitializeComponent();
        lblUser.Text = $"Admin: {_db.CurrentUser?.FullName}";
        ReloadAll();
        _ready = true;
    }

    // ---------- LOAD ----------
    void ReloadAll()
    {
        LoadFilter();
        LoadUnits();
        LoadFacilities();
        LoadRentals();
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

    void LoadFacilities() => gridFacilities.ItemsSource = _db.Facilities.ToList();
    void LoadRentals() => gridRentals.ItemsSource = _db.Rentals.OrderByDescending(r => r.Id).ToList();

    void LoadStats()
    {
        int total = _db.Units.Count, free = _db.Units.Count(u => u.IsAvailable);
        decimal revenue = _db.Rentals.Sum(r => r.RentFee);
        lblStats.Text = $"Tổng kho: {total}  |  Còn trống: {free}  |  Bận/tạm ngưng: {total - free}  |  Phí thuê: {revenue:N2} ₫";
    }

    void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) LoadUnits(); }
    void Refresh_Click(object sender, RoutedEventArgs e) => RefreshData();

    // ---------- KHO ----------
    void AddUnit_Click(object sender, RoutedEventArgs e)
    {
        if (!UiActions.Try(_db.Reload)) return;
        if (_db.Facilities.Count == 0) { MessageBox.Show("Hãy tạo cơ sở trước khi thêm kho."); return; }
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

    // ---------- CƠ SỞ ----------
    void AddFacility_Click(object sender, RoutedEventArgs e)
    {
        if (!UiActions.Try(_db.Reload)) return;
        var w = new FacilityEditWindow(null) { Owner = this };
        if (w.ShowDialog() != true) return;
        UiActions.Try(() => _db.SaveFacility(w.Facility, null));
        ReloadAll();
    }

    void EditFacility_Click(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs && gridFacilities.SelectedItem == null) return;
        if (gridFacilities.SelectedItem is not Facility f) { MessageBox.Show("Hãy chọn một cơ sở."); return; }
        if (!UiActions.Try(_db.Reload)) return;
        f = _db.Facilities.FirstOrDefault(x => x.Id == f.Id)!;
        if (f == null) { ReloadAll(); return; }
        var w = new FacilityEditWindow(f) { Owner = this };
        if (w.ShowDialog() != true) return;
        UiActions.Try(() => _db.SaveFacility(w.Facility, f));
        ReloadAll();
    }

    void DeleteFacility_Click(object sender, RoutedEventArgs e)
    {
        if (gridFacilities.SelectedItem is not Facility f) { MessageBox.Show("Hãy chọn một cơ sở."); return; }
        if (_db.Units.Any(u => u.FacilityId == f.Id))
        { MessageBox.Show("Cơ sở còn kho bên trong, hãy xóa/chuyển kho trước."); return; }
        if (MessageBox.Show($"Xóa cơ sở {f.Name}?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        UiActions.Try(() => _db.DeleteFacility(f.Id));
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

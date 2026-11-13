using System.Windows;
using System.Windows.Controls;

namespace SelfStorage;

public partial class BrowseView : UserControl
{
    readonly DataStore _db = DataStore.Instance;
    bool _ready;
    public event Action? Booked;

    public BrowseView()
    {
        InitializeComponent();

        var facilities = new List<Facility> { new() { Id = 0, Name = "Tất cả" } };
        facilities.AddRange(_db.Facilities);
        cbFacility.ItemsSource = facilities;
        cbFacility.SelectedIndex = 0;

        var types = new List<string> { "Tất cả" };
        types.AddRange(_db.Units.Select(u => u.Type).Distinct());
        cbType.ItemsSource = types;
        cbType.SelectedIndex = 0;

        _ready = true;
        LoadUnits();
    }

    void Filter_Changed(object sender, RoutedEventArgs e) { if (_ready) LoadUnits(); }

    public void LoadUnits()
    {
        if (!_ready) return;
        if (!UiActions.Try(_db.Reload)) return;
        ShowUnits();
    }

    internal void ShowUnits()
    {
        var selectedId = (grid.SelectedItem as StorageUnit)?.Id;
        var facilityId = (cbFacility.SelectedItem as Facility)?.Id ?? 0;
        var type = cbType.SelectedItem as string ?? "Tất cả";
        _ready = false;
        var facilities = new List<Facility> { new() { Id = 0, Name = "Tất cả" } };
        facilities.AddRange(_db.Facilities);
        cbFacility.ItemsSource = facilities;
        cbFacility.SelectedItem = facilities.FirstOrDefault(f => f.Id == facilityId) ?? facilities[0];
        var types = new[] { "Tất cả" }.Concat(_db.Units.Select(u => u.Type).Distinct()).ToList();
        cbType.ItemsSource = types;
        cbType.SelectedItem = types.Contains(type) ? type : types[0];
        _ready = true;
        IEnumerable<StorageUnit> q = _db.Units;

        if (cbFacility.SelectedItem is Facility f && f.Id != 0) q = q.Where(u => u.FacilityId == f.Id);
        if (cbType.SelectedItem is string t && t != "Tất cả") q = q.Where(u => u.Type == t);
        if (chkAvail.IsChecked == true) q = q.Where(u => u.IsAvailable);

        lblFilterError.Text = "";
        if (!string.IsNullOrWhiteSpace(txtMinArea.Text))
        {
            if (double.TryParse(txtMinArea.Text, out var minArea) && double.IsFinite(minArea) && minArea > 0)
                q = q.Where(u => u.SizeM2 >= minArea);
            else lblFilterError.Text = "Diện tích tối thiểu phải là số lớn hơn 0.";
        }
        if (!string.IsNullOrWhiteSpace(txtMaxPrice.Text))
        {
            if (decimal.TryParse(txtMaxPrice.Text, out var maxPrice) && DataStore.ValidMoney(maxPrice))
                q = q.Where(u => u.PricePerMonth <= maxPrice);
            else lblFilterError.Text += (lblFilterError.Text.Length == 0 ? "" : " ") + "Giá tối đa phải là số tiền hợp lệ.";
        }

        var units = lblFilterError.Text.Length == 0 ? q.OrderBy(u => u.FacilityId).ThenBy(u => u.PricePerMonth).ToList() : [];
        grid.ItemsSource = units;
        grid.SelectedItem = units.FirstOrDefault(u => u.Id == selectedId);
    }

    void Book_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not StorageUnit unit)
        { MessageBox.Show("Hãy chọn một kho trong danh sách."); return; }
        if (!UiActions.Try(_db.Reload)) return;
        unit = _db.Units.FirstOrDefault(u => u.Id == unit.Id)!;
        if (unit == null) { LoadUnits(); return; }
        if (!unit.IsAvailable)
        { ShowUnits(); MessageBox.Show($"Kho {unit.Code} chưa thể đặt. Trạng thái hiện tại: {unit.StatusText}."); return; }

        var win = new BookingWindow(unit) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true) Booked?.Invoke();
        LoadUnits();
    }
}

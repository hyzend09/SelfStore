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
        IEnumerable<StorageUnit> q = _db.Units;

        if (cbFacility.SelectedItem is Facility f && f.Id != 0) q = q.Where(u => u.FacilityId == f.Id);
        if (cbType.SelectedItem is string t && t != "Tất cả") q = q.Where(u => u.Type == t);
        if (chkAvail.IsChecked == true) q = q.Where(u => u.IsAvailable);

        grid.ItemsSource = q.OrderBy(u => u.FacilityId).ThenBy(u => u.PricePerMonth).ToList();
    }

    void Book_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not StorageUnit unit)
        { MessageBox.Show("Hãy chọn một kho trong danh sách."); return; }
        if (!unit.IsAvailable)
        { MessageBox.Show("Kho này đã được thuê."); return; }

        var win = new BookingWindow(unit) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true) Booked?.Invoke();
        LoadUnits();
    }
}

using System.Windows;

namespace SelfStorage;

public partial class FacilityEditWindow : Window
{
    readonly DataStore _db = DataStore.Instance;
    public Facility Facility { get; private set; }

    public FacilityEditWindow(Facility? existing)
    {
        InitializeComponent();
        Facility = existing ?? new Facility { Id = _db.Facilities.Count == 0 ? 1 : _db.Facilities.Max(f => f.Id) + 1 };
        Title = existing == null ? "Thêm cơ sở" : "Sửa cơ sở";
        txtName.Text = Facility.Name;
        txtAddress.Text = Facility.Address;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = txtName.Text.Trim();
        if (name == "") { lblError.Text = "Nhập tên cơ sở."; return; }
        if (_db.Facilities.Any(f => f.Id != Facility.Id && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        { lblError.Text = "Tên cơ sở đã tồn tại."; return; }

        Facility.Name = name;
        Facility.Address = txtAddress.Text.Trim();
        DialogResult = true;
    }
}

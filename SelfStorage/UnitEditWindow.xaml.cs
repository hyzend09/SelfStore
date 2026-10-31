using System.Windows;

namespace SelfStorage;

public partial class UnitEditWindow : Window
{
    readonly DataStore _db = DataStore.Instance;
    readonly bool _isNew;
    public StorageUnit Unit { get; private set; }

    public UnitEditWindow(StorageUnit? existing)
    {
        InitializeComponent();
        _isNew = existing == null;
        Unit = existing == null ? new StorageUnit() : System.Text.Json.JsonSerializer.Deserialize<StorageUnit>(System.Text.Json.JsonSerializer.Serialize(existing))!;
        Title = _isNew ? "Thêm kho" : $"Sửa kho {Unit.Code}";

        cbFacility.ItemsSource = _db.Facilities;
        var types = new List<string> { "Nhỏ", "Vừa", "Lớn", "Có điều hòa" };
        types.AddRange(_db.Units.Select(u => u.Type).Where(t => !types.Contains(t)).Distinct());
        cbType.ItemsSource = types;

        if (_isNew)
        {
            cbFacility.SelectedIndex = 0;
            cbType.SelectedIndex = 0;
        }
        else
        {
            cbFacility.SelectedItem = _db.Facilities.FirstOrDefault(f => f.Id == Unit.FacilityId);
            txtCode.Text = Unit.Code;
            cbType.Text = Unit.Type;
            txtSize.Text = Unit.SizeM2.ToString();
            txtPrice.Text = Unit.PricePerMonth.ToString("0.##");
            chkAvail.IsChecked = Unit.IsAvailable;

            // Kho đang có khách thuê thì không cho đổi trạng thái / cơ sở
            if (_db.Rentals.Any(r => r.UnitId == Unit.Id && !r.IsReturned))
            {
                chkAvail.IsEnabled = false;
                cbFacility.IsEnabled = false;
                lblNote.Text = "Kho đang có khách thuê: không đổi được trạng thái và cơ sở.";
            }
        }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var code = txtCode.Text.Trim();
        var type = cbType.Text.Trim();

        if (cbFacility.SelectedItem is not Facility fac) { lblError.Text = "Hãy chọn cơ sở."; return; }
        if (code == "") { lblError.Text = "Nhập mã kho."; return; }
        if (_db.Units.Any(u => u.Id != Unit.Id && u.FacilityId == fac.Id &&
                               u.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        { lblError.Text = "Mã kho đã tồn tại trong cơ sở này."; return; }
        if (type == "") { lblError.Text = "Nhập loại kho."; return; }
        if (!double.TryParse(txtSize.Text, out var size) || !double.IsFinite(size) || size <= 0) { lblError.Text = "Diện tích phải là số hữu hạn > 0."; return; }
        if (!decimal.TryParse(txtPrice.Text, out var price) || !DataStore.ValidMoney(price)) { lblError.Text = "Giá thuê phải > 0, tối đa 2 chữ số thập phân."; return; }
        if (code.Length > 100 || type.Length > 100) { lblError.Text = "Mã/loại kho tối đa 100 ký tự."; return; }

        Unit.FacilityId = fac.Id;
        Unit.Code = code;
        Unit.Type = type;
        Unit.SizeM2 = size;
        Unit.PricePerMonth = price;
        if (chkAvail.IsEnabled) Unit.IsAvailable = chkAvail.IsChecked == true;
        DialogResult = true;
    }
}

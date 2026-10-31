using System.Windows;
using System.Windows.Controls;

namespace SelfStorage;

public partial class MyRentalsView : UserControl
{
    readonly DataStore _db = DataStore.Instance;

    public MyRentalsView()
    {
        InitializeComponent();
        Reload();
    }

    public void Reload()
    {
        var uid = _db.CurrentUser?.Id ?? -1;
        grid.ItemsSource = _db.Rentals.Where(r => r.UserId == uid).OrderByDescending(r => r.Id).ToList();
    }

    void Qr_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Rental r) { MessageBox.Show("Hãy chọn một đơn thuê."); return; }
        if (r.IsReturned) { MessageBox.Show("Kho này đã được trả."); return; }
        new QrWindow(r) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    void Return_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Rental r) { MessageBox.Show("Hãy chọn một đơn thuê."); return; }
        if (r.IsReturned) { MessageBox.Show("Kho này đã được trả."); return; }

        var ok = MessageBox.Show(
            $"Trả kho {r.UnitCode}?\nTiền cọc {r.Deposit:N0} ₫ sẽ được hoàn lại sau khi nhân viên kiểm tra kho.",
            "Xác nhận trả kho", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        r.IsReturned = true;
        r.ReturnedAt = DateTime.Now;
        if (r.Unit != null) r.Unit.IsAvailable = true;
        _db.Save();
        Reload();
    }
}

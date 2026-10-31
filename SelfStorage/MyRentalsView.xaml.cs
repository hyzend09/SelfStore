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
        UiActions.Try(_db.Reload);
        var uid = _db.CurrentUser?.Id ?? -1;
        grid.ItemsSource = _db.Rentals.Where(r => r.UserId == uid).OrderByDescending(r => r.Id).ToList();
    }

    void Qr_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Rental r) { MessageBox.Show("Hãy chọn một đơn thuê."); return; }
        if (!UiActions.Try(_db.Reload)) return;
        r = _db.Rentals.FirstOrDefault(x => x.Id == r.Id && x.UserId == _db.CurrentUser?.Id)!;
        if (r == null || r.IsReturned) { MessageBox.Show("Đơn không còn hiệu lực."); Reload(); return; }
        new QrWindow(r) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    void Return_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Rental r) { MessageBox.Show("Hãy chọn một đơn thuê."); return; }
        if (r.IsReturned) { MessageBox.Show("Kho này đã được trả."); return; }

        var ok = MessageBox.Show(
            $"Yêu cầu trả kho {r.UnitCode}?\nNhân viên sẽ kiểm tra, ghi nhận khấu trừ nếu có và hoàn cọc. Kho chưa được mở cho thuê lại ở bước này.",
            "Gửi yêu cầu trả kho", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        UiActions.Try(() => _db.RequestReturn(r.Id));
        Reload();
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    void Rental_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (lblDetails == null) return;
        lblDetails.Text = grid.SelectedItem is Rental r
            ? $"Nhận kho: {r.ReceivedAt:dd/MM/yyyy HH:mm} • Yêu cầu trả: {r.ReturnRequestedAt:dd/MM/yyyy HH:mm}\n" +
              $"Cọc: {r.Deposit:N2} ₫ • Khấu trừ: {r.Deduction:N2} ₫ • {r.RefundText}\n" +
              $"Biên bản: {r.InspectionNotes}\nMã đối soát: {r.RefundReference} • Lúc: {r.RefundedAt:dd/MM/yyyy HH:mm}"
            : "Chọn đơn để xem bàn giao và đối soát cọc.";
    }
}

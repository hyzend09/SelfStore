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
        if (!UiActions.Try(_db.Reload)) return;
        ShowRentals();
    }

    internal void ShowRentals()
    {
        var selectedId = (grid.SelectedItem as Rental)?.Id;
        var uid = _db.CurrentUser?.Id ?? -1;
        var rentals = _db.Rentals.Where(r => r.UserId == uid).OrderByDescending(r => r.Id).ToList();
        grid.ItemsSource = rentals;
        grid.SelectedItem = rentals.FirstOrDefault(r => r.Id == selectedId);
    }

    void PayHold_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Rental selected || !UiActions.Try(_db.Reload)) return;
        var rental = _db.Rentals.SingleOrDefault(r => r.Id == selected.Id && r.UserId == _db.CurrentUser?.Id);
        if (rental?.IsAwaitingPayment != true) { ShowRentals(); return; }
        var owner = Window.GetWindow(this);
        var payment = new PaymentWindow(rental, _db) { Owner = owner };
        if (payment.ShowDialog() == true && payment.PaidRental is Rental paid)
            new QrWindow(paid) { Owner = owner }.ShowDialog();
        Reload();
    }

    void CancelHold_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Rental rental || !rental.IsAwaitingPayment) return;
        if (MessageBox.Show($"Hủy giữ chỗ kho {rental.UnitCode}?", "Hủy giữ chỗ",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        UiActions.Try(() => _db.CancelHold(rental.Id));
        ShowRentals();
    }

    void Qr_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Rental r) { MessageBox.Show("Hãy chọn một đơn thuê."); return; }
        if (!UiActions.Try(_db.Reload)) return;
        r = _db.Rentals.FirstOrDefault(x => x.Id == r.Id && x.UserId == _db.CurrentUser?.Id)!;
        if (r == null || r.PaidAt == null || !r.IsOpen || r.QrContent == "")
        { MessageBox.Show("Đơn hiện không có QR nhận/trả kho."); ShowRentals(); return; }
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
        var r = grid.SelectedItem as Rental;
        btnPayHold.IsEnabled = btnCancelHold.IsEnabled = r?.IsAwaitingPayment == true;
        btnQr.IsEnabled = r is { IsOpen: true, PaidAt: not null } && r.QrContent != "";
        btnReturn.IsEnabled = r is { CanStartReturn: true, WasHandedOver: true };
        lblDetails.Text = r == null ? "Chọn đơn để xem chi tiết."
            : r.IsAwaitingPayment ? $"Giữ kho đến {r.HoldDeadlineLocal:HH:mm:ss dd/MM/yyyy} • Tổng cần thanh toán: {r.Total:N2} ₫"
            : r.PaidAt == null ? r.StatusText
            : $"Nhận kho: {r.ReceivedAt:dd/MM/yyyy HH:mm} • Yêu cầu trả: {r.ReturnRequestedAt:dd/MM/yyyy HH:mm}\n" +
              $"Cọc: {r.Deposit:N2} ₫ • Phí phát sinh: {r.ExtraFee:N2} ₫ • Khấu trừ: {r.Deduction:N2} ₫ • {r.RefundText}\n" +
              $"Biên bản: {r.InspectionNotes}\nMã đối soát: {r.RefundReference} • Lúc: {r.RefundedAt:dd/MM/yyyy HH:mm}"
            + (r is { MissedPickup: true, CanStartReturn: true } ? "\nLiên hệ Admin để đối soát cọc cho đơn quá hạn nhận kho." : "")
            + (r.ExtraFee > r.Deduction ? $"\nPhí vượt cọc: {r.ExtraFee - r.Deduction:N2} ₫" : "");
    }
}

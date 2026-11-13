using System.Windows;
using System.Windows.Controls;

namespace SelfStorage;

public partial class BookingWindow : Window
{
    readonly StorageUnit _unit;
    readonly DataStore _db = DataStore.Instance;

    public BookingWindow(StorageUnit unit)
    {
        _unit = unit;
        InitializeComponent();
        lblInfo.Text = $"{unit.FacilityName} - {unit.Code}";
        lblPrice.Text = $"{unit.Type}, {unit.SizeM2} m² • {unit.PricePerMonth:N2} ₫/tháng";
        dpStart.DisplayDateStart = DateTime.Today;
        dpStart.DisplayDateEnd = DateTime.Today.AddYears(5);
        dpStart.SelectedDate = DateTime.Today;
        cbMonths.ItemsSource = Enumerable.Range(1, 12);
        cbMonths.SelectedIndex = 0;
    }

    // Quy tắc đơn giản: cọc = 1 tháng tiền thuê; phí thuê = giá * số tháng
    decimal Deposit => _unit.PricePerMonth;
    decimal RentFee => cbMonths.SelectedItem is int m ? _unit.PricePerMonth * m : 0;

    void Recalc(object? sender, EventArgs e)
    {
        if (cbMonths.SelectedItem is not int m || dpStart.SelectedDate is not DateTime start) return;
        lblEnd.Text = $"Hạn thuê đến: {start.AddMonths(m):dd/MM/yyyy}";
        lblRent.Text = $"Phí thuê ({m} tháng): {RentFee:N2} ₫";
        lblDeposit.Text = $"Tiền cọc (hoàn lại khi trả kho): {Deposit:N2} ₫";
        lblTotal.Text = $"Tổng thanh toán: {Deposit + RentFee:N2} ₫";
    }

    void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (dpStart.SelectedDate is not DateTime start || start.Date < DateTime.Today)
        { MessageBox.Show("Ngày bắt đầu không hợp lệ."); return; }
        if (start.Date > DateTime.Today.AddYears(5) || cbMonths.SelectedItem is not int months)
        { MessageBox.Show("Chọn ngày trong vòng 5 năm và thời hạn thuê hợp lệ."); return; }
        Rental rental = null!;
        if (!UiActions.Try(() => rental = _db.CreateHold(_unit.Id, start, months, _unit.PricePerMonth))) return;
        var pay = new PaymentWindow(rental, _db) { Owner = this };
        if (pay.ShowDialog() == true && pay.PaidRental is Rental paid)
            new QrWindow(paid) { Owner = this }.ShowDialog();
        DialogResult = true;
    }
}

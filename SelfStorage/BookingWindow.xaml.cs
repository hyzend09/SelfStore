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
        lblBookingError.Text = "";
        if (cbMonths.SelectedItem is not int m || dpStart.SelectedDate is not DateTime start)
        {
            lblEnd.Text = lblRent.Text = lblDeposit.Text = lblTotal.Text = "—";
            lblRentLabel.Text = "Phí thuê";
            return;
        }
        lblEnd.Text = $"{start.AddMonths(m):dd/MM/yyyy}";
        lblRentLabel.Text = $"Phí thuê ({m} tháng)";
        lblRent.Text = $"{RentFee:N2} ₫";
        lblDeposit.Text = $"{Deposit:N2} ₫";
        lblTotal.Text = $"{Deposit + RentFee:N2} ₫";
    }

    void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (dpStart.SelectedDate is not DateTime start || start.Date < DateTime.Today)
        {
            lblBookingError.Text = "Chọn ngày bắt đầu từ hôm nay trở đi.";
            dpStart.Focus();
            lblBookingError.BringIntoView();
            return;
        }
        if (start.Date > DateTime.Today.AddYears(5) || cbMonths.SelectedItem is not int months)
        {
            lblBookingError.Text = "Chọn ngày trong vòng 5 năm và thời hạn thuê hợp lệ.";
            lblBookingError.BringIntoView();
            return;
        }
        Rental rental = null!;
        if (!UiActions.Try(() => rental = _db.CreateHold(_unit.Id, start, months, _unit.PricePerMonth))) return;
        var pay = new PaymentWindow(rental, _db) { Owner = this };
        if (pay.ShowDialog() == true && pay.PaidRental is Rental paid)
            new QrWindow(paid) { Owner = this }.ShowDialog();
        DialogResult = true;
    }
}

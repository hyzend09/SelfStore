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
        lblPrice.Text = $"{unit.Type}, {unit.SizeM2} m² • {unit.PricePerMonth:N0} ₫/tháng";
        dpStart.DisplayDateStart = DateTime.Today;
        dpStart.SelectedDate = DateTime.Today;
        cbMonths.ItemsSource = new[] { 1, 3, 6, 12 };
        cbMonths.SelectedIndex = 0;
    }

    // Quy tắc đơn giản: cọc = 1 tháng tiền thuê; phí thuê = giá * số tháng
    decimal Deposit => _unit.PricePerMonth;
    decimal RentFee => cbMonths.SelectedItem is int m ? _unit.PricePerMonth * m : 0;

    void Recalc(object? sender, EventArgs e)
    {
        if (cbMonths.SelectedItem is not int m || dpStart.SelectedDate is not DateTime start) return;
        lblEnd.Text = $"Hạn thuê đến: {start.AddMonths(m):dd/MM/yyyy}";
        lblRent.Text = $"Phí thuê ({m} tháng): {RentFee:N0} ₫";
        lblDeposit.Text = $"Tiền cọc (hoàn lại khi trả kho): {Deposit:N0} ₫";
        lblTotal.Text = $"Tổng thanh toán: {Deposit + RentFee:N0} ₫";
    }

    void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (dpStart.SelectedDate is not DateTime start || start.Date < DateTime.Today)
        { MessageBox.Show("Ngày bắt đầu không hợp lệ."); return; }
        if (!_unit.IsAvailable)
        { MessageBox.Show("Kho vừa được người khác thuê."); DialogResult = false; return; }

        var rental = new Rental
        {
            Id = _db.NextRentalId(),
            UserId = _db.CurrentUser!.Id,
            UnitId = _unit.Id,
            StartDate = start.Date,
            Months = (int)cbMonths.SelectedItem,
            Deposit = Deposit,
            RentFee = RentFee
        };

        var pay = new PaymentWindow(rental) { Owner = this };
        if (pay.ShowDialog() != true) return;

        // Thanh toán thành công -> chốt đơn
        rental.PaymentMethod = pay.SelectedMethod;
        rental.PaidAt = DateTime.Now;
        rental.QrContent = $"SELFSTORAGE|R{rental.Id}|{_unit.Code}|{_unit.FacilityId}|{rental.StartDate:yyyyMMdd}|{rental.EndDate:yyyyMMdd}";
        _unit.IsAvailable = false;
        _db.Rentals.Add(rental);
        _db.Save();

        new QrWindow(rental) { Owner = Owner }.ShowDialog();
        DialogResult = true;
    }
}

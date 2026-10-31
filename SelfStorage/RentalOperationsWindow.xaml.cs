using System.Windows;

namespace SelfStorage;

public enum RentalOperation { Receive, Inspect, Refund }

public partial class RentalOperationsWindow : Window
{
    readonly DataStore _db = DataStore.Instance;
    readonly int _rentalId;
    readonly RentalOperation _operation;

    public RentalOperationsWindow(int rentalId, RentalOperation operation)
    {
        _rentalId = rentalId; _operation = operation;
        InitializeComponent();
        var rental = _db.Rentals.Single(r => r.Id == rentalId);
        Title = lblHeading.Text = operation switch { RentalOperation.Receive => "Bàn giao nhận kho", RentalOperation.Inspect => "Kiểm tra trả kho", _ => "Xác nhận hoàn cọc" };
        lblRental.Text = $"Đơn #{rental.Id} • {rental.CustomerName}\n{rental.FacilityName} • {rental.UnitCode}\n{rental.StartDate:dd/MM/yyyy} – {rental.EndDate:dd/MM/yyyy}\nTrạng thái: {rental.StatusText}";
        lblAmounts.Text = $"Cọc: {rental.Deposit:N2} ₫ • Khấu trừ: {rental.Deduction:N2} ₫\nTiền cọc được hoàn sau kiểm tra: {rental.RefundAmount:N2} ₫";
        lblInspection.Text = $"Biên bản: {rental.InspectionNotes}\nKiểm tra lúc: {rental.InspectedAt:dd/MM/yyyy HH:mm}";
        pickupPanel.Visibility = operation == RentalOperation.Receive ? Visibility.Visible : Visibility.Collapsed;
        inspectionPanel.Visibility = operation == RentalOperation.Inspect ? Visibility.Visible : Visibility.Collapsed;
        refundPanel.Visibility = operation == RentalOperation.Refund ? Visibility.Visible : Visibility.Collapsed;
        btnConfirm.Content = operation switch { RentalOperation.Receive => "Xác nhận đã nhận kho", RentalOperation.Inspect => "Lưu biên bản", _ => "Đã đối soát và đóng đơn" };
    }

    void Verify_Click(object sender, RoutedEventArgs e)
    {
        lblVerified.Text = "";
        UiActions.Try(() =>
        {
            var rental = _db.VerifyPickup(txtQr.Text);
            if (rental.Id != _rentalId) throw new InvalidOperationException("QR thuộc đơn khác. Hãy chọn đúng đơn thuê.");
            lblVerified.Text = $"QR hợp lệ • {rental.CustomerName} • {rental.UnitCode}";
        });
    }

    void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_operation == RentalOperation.Inspect && !decimal.TryParse(txtDeduction.Text, out _))
        { MessageBox.Show("Nhập số tiền khấu trừ hợp lệ."); return; }
        if (_operation == RentalOperation.Refund && chkConfirmed.IsChecked != true)
        { MessageBox.Show("Hãy xác nhận đã thực hiện hoàn tiền / đối soát cọc."); return; }
        var succeeded = UiActions.Try(() =>
        {
            switch (_operation)
            {
                case RentalOperation.Receive: _db.ReceiveRental(txtQr.Text, _rentalId); break;
                case RentalOperation.Inspect: _db.InspectReturn(_rentalId, decimal.Parse(txtDeduction.Text), txtNotes.Text); break;
                case RentalOperation.Refund: _db.ConfirmRefund(_rentalId, txtReference.Text); break;
            }
        });
        if (succeeded) DialogResult = true;
    }
}

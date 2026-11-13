using System.Windows;

namespace SelfStorage;

public partial class PaymentWindow : Window
{
    readonly CancellationTokenSource _lifetime = new();
    bool _closed;
    readonly Rental _rental;
    readonly DataStore? _store;
    readonly string _transactionCode = Guid.NewGuid().ToString("N");
    public string SelectedMethod { get; private set; } = "";
    public Rental? PaidRental { get; private set; }

    public PaymentWindow(Rental rental, DataStore? store = null)
    {
        _rental = rental;
        _store = store;
        InitializeComponent();
        lblAmount.Text = $"{rental.Total:N2} ₫";
        if (rental.HoldDeadlineLocal is DateTime deadline)
            lblDeadline.Text = $"Giữ kho đến {deadline:HH:mm:ss dd/MM/yyyy}.";
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!btnPay.IsEnabled || _closed) return;
        var method = rbCard.IsChecked == true ? "Thẻ ngân hàng"
            : rbWallet.IsChecked == true ? "Ví điện tử" : "Chuyển khoản";
        btnPay.IsEnabled = rbCard.IsEnabled = rbWallet.IsEnabled = rbTransfer.IsEnabled = false;
        btnPay.Content = "Đang xử lý...";
        try { await Task.Delay(1000, _lifetime.Token); } // giả lập gọi cổng thanh toán
        catch (OperationCanceledException) { return; }
        if (_closed) return;
        if (_store != null && !UiActions.Try(() => PaidRental = _store.PayHeldRental(_rental.Id, method, _transactionCode)))
        {
            btnPay.IsEnabled = rbCard.IsEnabled = rbWallet.IsEnabled = rbTransfer.IsEnabled = true;
            btnPay.Content = "Xác nhận thanh toán";
            return;
        }
        SelectedMethod = method;
        DialogResult = true;
    }
}

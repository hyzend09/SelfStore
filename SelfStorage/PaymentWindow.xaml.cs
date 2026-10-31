using System.Windows;

namespace SelfStorage;

public partial class PaymentWindow : Window
{
    readonly CancellationTokenSource _lifetime = new();
    bool _closed;
    public string SelectedMethod { get; private set; } = "";

    public PaymentWindow(Rental rental)
    {
        InitializeComponent();
        lblAmount.Text = $"{rental.Total:N2} ₫";
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
        SelectedMethod = method;
        DialogResult = true;
    }
}

using System.Windows;

namespace SelfStorage;

public partial class PaymentWindow : Window
{
    public string SelectedMethod { get; private set; } = "";

    public PaymentWindow(Rental rental)
    {
        InitializeComponent();
        lblAmount.Text = $"{rental.Total:N0} ₫";
    }

    async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        btnPay.IsEnabled = false;
        btnPay.Content = "Đang xử lý...";
        await Task.Delay(1000); // giả lập gọi cổng thanh toán

        SelectedMethod = rbCard.IsChecked == true ? "Thẻ ngân hàng"
                       : rbWallet.IsChecked == true ? "Ví điện tử" : "Chuyển khoản";
        DialogResult = true;
    }
}

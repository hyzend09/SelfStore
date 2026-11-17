using System.Windows;

namespace SelfStorage;

public partial class QrWindow : Window
{
    public QrWindow(Rental r)
    {
        InitializeComponent();
        lblTitle.Text = $"Kho {r.UnitCode} - {r.FacilityName}";
        lblSub.Text = $"Mã thuê #{r.Id}\n{r.StartDate:dd/MM/yyyy} → {r.EndDate:dd/MM/yyyy}";
        imgQr.Source = QrHelper.Create(r.QrContent);
        txtQrContent.Text = r.QrContent;
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(txtQrContent.Text);
            lblCopy.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Accent");
            lblCopy.Text = "Đã sao chép mã QR.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            lblCopy.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Danger");
            lblCopy.Text = "Chưa sao chép được. Hãy thử lại.";
        }
    }
}

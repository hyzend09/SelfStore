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
    }
}

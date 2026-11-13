using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SelfStorage;

public partial class MainWindow : Window
{
    readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public MainWindow()
    {
        InitializeComponent();
        lblUser.Text = $"Xin chào, {DataStore.Instance.CurrentUser?.FullName}";
        browse.Booked += () => { mine.Reload(); Tabs.SelectedIndex = 1; };
        _holdTimer.Tick += (_, _) =>
        {
            var db = DataStore.Instance;
            if (!db.Rentals.Any(r => r.IsAwaitingPayment && r.HoldDeadlineLocal <= DateTime.Now)) return;
            if (!UiActions.Try(db.Reload)) return;
            mine.ShowRentals();
            browse.ShowUnits();
        };
        _holdTimer.Start();
        Closed += (_, _) => _holdTimer.Stop();
    }

    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Tabs || mine == null) return;
        mine.Reload();
        browse.LoadUnits();
    }

    void Logout_Click(object sender, RoutedEventArgs e)
    {
        DataStore.Instance.CurrentUser = null;
        new LoginWindow().Show();
        Close();
    }
}

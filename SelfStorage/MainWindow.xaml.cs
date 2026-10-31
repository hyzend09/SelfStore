using System.Windows;
using System.Windows.Controls;

namespace SelfStorage;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        lblUser.Text = $"Xin chào, {DataStore.Instance.CurrentUser?.FullName}";
        browse.Booked += () => { mine.Reload(); Tabs.SelectedIndex = 1; };
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

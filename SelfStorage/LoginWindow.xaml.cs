using System.Windows;

namespace SelfStorage;

public partial class LoginWindow : Window
{
    readonly DataStore _db = DataStore.Instance;

    public LoginWindow() => InitializeComponent();

    void Login_Click(object sender, RoutedEventArgs e)
    {
        lblMsg.Foreground = System.Windows.Media.Brushes.Red;
        bool loggedIn = false;
        if (!UiActions.Try(() => loggedIn = _db.Login(txtLoginEmail.Text, txtLoginPass.Password))) return;
        if (!loggedIn) { lblMsg.Text = "Sai email hoặc mật khẩu."; return; }
        var user = _db.CurrentUser!;
        Window next = user.Role == "Admin" ? new AdminWindow() : new MainWindow();
        next.Show();
        Close();
    }

    void Register_Click(object sender, RoutedEventArgs e)
    {
        var name = txtRegName.Text.Trim();
        var email = txtRegEmail.Text.Trim().ToLower();
        var pass = txtRegPass.Password;

        lblMsg.Foreground = System.Windows.Media.Brushes.Red;
        if (!UiActions.Try(() => _db.Register(name, email, pass))) return;
        lblMsg.Foreground = System.Windows.Media.Brushes.Green;
        lblMsg.Text = "Đăng ký thành công! Hãy chuyển sang tab Đăng nhập.";
    }
}

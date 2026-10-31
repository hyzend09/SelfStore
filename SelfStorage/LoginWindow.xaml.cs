using System.Windows;

namespace SelfStorage;

public partial class LoginWindow : Window
{
    readonly DataStore _db = DataStore.Instance;

    public LoginWindow() => InitializeComponent();

    void Login_Click(object sender, RoutedEventArgs e)
    {
        var email = txtLoginEmail.Text.Trim().ToLower();
        var hash = DataStore.Hash(txtLoginPass.Password);
        var user = _db.Users.FirstOrDefault(u => u.Email.ToLower() == email && u.PasswordHash == hash);

        if (user == null) { lblMsg.Text = "Sai email hoặc mật khẩu."; return; }
        _db.CurrentUser = user;
        Window next = user.Role == "Admin" ? new AdminWindow() : new MainWindow();
        next.Show();
        Close();
    }

    void Register_Click(object sender, RoutedEventArgs e)
    {
        var name = txtRegName.Text.Trim();
        var email = txtRegEmail.Text.Trim().ToLower();
        var pass = txtRegPass.Password;

        if (name == "" || !email.Contains('@') || pass.Length < 6)
        { lblMsg.Text = "Nhập đủ họ tên, email hợp lệ, mật khẩu ≥ 6 ký tự."; return; }
        if (_db.Users.Any(u => u.Email.ToLower() == email))
        { lblMsg.Text = "Email đã tồn tại."; return; }

        _db.Users.Add(new User { Id = _db.NextUserId(), FullName = name, Email = email, PasswordHash = DataStore.Hash(pass) });
        _db.Save();
        lblMsg.Foreground = System.Windows.Media.Brushes.Green;
        lblMsg.Text = "Đăng ký thành công! Hãy chuyển sang tab Đăng nhập.";
    }
}

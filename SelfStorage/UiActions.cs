using System.Windows;
using Microsoft.Data.SqlClient;

namespace SelfStorage;

internal static class UiActions
{
    public static bool Try(Action action)
    {
        try { action(); return true; }
        catch (Exception ex) when (ex is SqlException or StoreConcurrencyException or InvalidOperationException or ArgumentException)
        {
            var message = ex is SqlException
                ? "Không thể lưu/tải dữ liệu. Kiểm tra kết nối, thông tin trùng hoặc dữ liệu không hợp lệ rồi thử lại."
                : ex.Message;
            MessageBox.Show(message, "SelfStorage", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}

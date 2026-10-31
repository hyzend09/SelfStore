using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace SelfStorage;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            if (args.Exception is not (SqlException or StoreConcurrencyException or IOException or JsonException)) return;
            args.Handled = true;
            ShowDatabaseError(args.Exception);
            Shutdown(1);
        };
        try { _ = DataStore.Instance; }
        catch (Exception ex)
        {
            ShowDatabaseError(ex);
            Shutdown(1);
            return;
        }
        // Hiển thị tiền/ngày theo định dạng máy (vi-VN)
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        base.OnStartup(e);
    }

    static void ShowDatabaseError(Exception ex) => MessageBox.Show(
        "Không thể đọc/lưu dữ liệu SQL Server. Ứng dụng sẽ đóng để tránh dùng dữ liệu chưa lưu.\n" +
        "Kiểm tra SQL Server, chạy database/CreateDatabase.sql và cấu hình kết nối trong appsettings.json " +
        "hoặc SELFSTORAGE_CONNECTION_STRING.\n\n" +
        (ex is SqlException ? "Kết nối hoặc ràng buộc dữ liệu không hợp lệ. Dữ liệu chưa commit được giữ nguyên." : ex.Message),
        "SelfStorage - Database", MessageBoxButton.OK, MessageBoxImage.Error);
}

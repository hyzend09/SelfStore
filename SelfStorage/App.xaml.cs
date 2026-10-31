using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace SelfStorage;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Hiển thị tiền/ngày theo định dạng máy (vi-VN)
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        base.OnStartup(e);
    }
}

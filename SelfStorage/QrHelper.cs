using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace SelfStorage;

public static class QrHelper
{
    public static BitmapImage Create(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(12);

        var bmp = new BitmapImage();
        using var ms = new MemoryStream(png);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}

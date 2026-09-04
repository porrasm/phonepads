using System.IO;
using Avalonia.Media.Imaging;
using QRCoder;

namespace Phonepads.App.Services;

/// <summary>Renders the join URL as a QR code for players to scan (SESSION-1).</summary>
public static class QrRenderer
{
    public static Bitmap Render(string url, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);

        using var stream = new MemoryStream(png);
        return new Bitmap(stream);
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using AutoBIMFusion.Common.Mist.Helpers;
using Image = System.Drawing.Image;

namespace AutoBIMFusion.Common.Extensions;

[SupportedOSPlatform("windows")]
public static class BitmapExtensions
{
    public static Image RotateImage(this Image image, double angleRadians, Color backgroundColor)
    {
        ArgumentNullException.ThrowIfNull(image);

        angleRadians = -angleRadians % (2 * PI);
        var sin = Abs(Sin(angleRadians));
        var cos = Abs(Cos(angleRadians));
        var newWidth = (int)Round(image.Width * cos + image.Height * sin);
        var newHeight = (int)Round(image.Width * sin + image.Height * cos);

        var rotatedImage = new Bitmap(newWidth, newHeight);

        using var g = Graphics.FromImage(rotatedImage);
        g.Clear(backgroundColor);
        g.TranslateTransform(newWidth / 2, newHeight / 2);
        g.RotateTransform((float)(angleRadians * (180 / PI)));
        g.DrawImage(image, new Rectangle(-image.Width / 2, -image.Height / 2, image.Width, image.Height));

        return rotatedImage;
    }


    public static string GetImageFileSize(this Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Jpeg);

        return Files.FormatFileSizeFromByte(ms.Length, 2);
    }
}

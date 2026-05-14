using AutoBIMFusion.Common.Helpers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Image = System.Drawing.Image;

namespace AutoBIMFusion.Common.Extensions;

[SupportedOSPlatform("windows")]
public static class BitmapExtensions
{
    public static Image RotateImage(this Image image, double angleRadians, Color backgroundColor)
    {
        ArgumentNullException.ThrowIfNull(image);

        angleRadians = -angleRadians % (2 * PI);
        double sin = Abs(Sin(angleRadians));
        double cos = Abs(Cos(angleRadians));
        int newWidth = (int)Round((image.Width * cos) + (image.Height * sin));
        int newHeight = (int)Round((image.Width * sin) + (image.Height * cos));

        Bitmap rotatedImage = new(newWidth, newHeight);

        using Graphics g = Graphics.FromImage(rotatedImage);
        g.Clear(backgroundColor);
        g.TranslateTransform(newWidth / 2, newHeight / 2);
        g.RotateTransform((float)(angleRadians * (180 / PI)));
        g.DrawImage(image, new Rectangle(-image.Width / 2, -image.Height / 2, image.Width, image.Height));

        return rotatedImage;
    }


    public static string GetImageFileSize(this Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using MemoryStream ms = new();
        image.Save(ms, ImageFormat.Jpeg);

        return FileUtil.FormatFileSizeFromByte(ms.Length, 2);
    }
}

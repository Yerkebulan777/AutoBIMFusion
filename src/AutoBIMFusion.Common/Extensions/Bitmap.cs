using SioForgeCAD.Commun.Mist;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Image = System.Drawing.Image;

namespace SioForgeCAD.Commun.Extensions
{
    [SupportedOSPlatform("windows")]
    public static class BitmapExtensions
    {
        public static Image RotateImage(this Image image, double angleRadians, System.Drawing.Color backgroundColor)
        {
            ArgumentNullException.ThrowIfNull(image);

            angleRadians = (-angleRadians) % (2 * Math.PI);
            double sin = Math.Abs(Math.Sin(angleRadians));
            double cos = Math.Abs(Math.Cos(angleRadians));
            int newWidth = (int)Math.Round((image.Width * cos) + (image.Height * sin));
            int newHeight = (int)Math.Round((image.Width * sin) + (image.Height * cos));

            Bitmap rotatedImage = new Bitmap(newWidth, newHeight);

            using var g = Graphics.FromImage(rotatedImage);
            g.Clear(backgroundColor);
            g.TranslateTransform(newWidth / 2, newHeight / 2);
            g.RotateTransform((float)(angleRadians * (180 / Math.PI)));
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
}

using SioForgeCAD.Commun.Mist;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Image = System.Drawing.Image;

namespace SioForgeCAD.Commun.Extensions
{
    public static class BitmapWpfExtensions
    {
        public static BitmapSource ToBitmapSource(this Image Image)
        {
            using (var ms = new MemoryStream())
            {
                Image.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                ms.Seek(0, SeekOrigin.Begin);

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = ms;
                bitmapImage.EndInit();

                return bitmapImage;
            }
        }

        public static BitmapSource? CreateBitmapSourceFromBitmap(this Bitmap bitmap)
        {
            return CreateBitmapSourceFromBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        public static BitmapSource? CreateBitmapSourceFromBitmap(this Bitmap bitmap, IntPtr palette, Int32Rect sourceRect, BitmapSizeOptions sizeOptions)
        {
            if (bitmap == null)
            {
                return null;
            }

            IntPtr hbitmap = bitmap.GetHbitmap();
            if (hbitmap == IntPtr.Zero)
            {
                return null;
            }

            BitmapSource result = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, palette, sourceRect, sizeOptions);
            DeleteObject(hbitmap);
            return result;
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);
    }
}

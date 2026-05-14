using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Image = System.Drawing.Image;

namespace AutoBIMFusion.Common.Extensions;

public static class BitmapWpfExtensions
{
    public static BitmapSource ToBitmapSource(this Image Image)
    {
        using MemoryStream ms = new();
        Image.Save(ms, ImageFormat.Bmp);
        _ = ms.Seek(0, SeekOrigin.Begin);

        BitmapImage bitmapImage = new();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = ms;
        bitmapImage.EndInit();

        return bitmapImage;
    }

    public static BitmapSource? CreateBitmapSourceFromBitmap(this Bitmap bitmap)
    {
        return bitmap.CreateBitmapSourceFromBitmap(IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    public static BitmapSource? CreateBitmapSourceFromBitmap(this Bitmap bitmap, IntPtr palette, Int32Rect sourceRect,
        BitmapSizeOptions sizeOptions)
    {
        if (bitmap == null)
        {
            return null;
        }

        nint hbitmap = bitmap.GetHbitmap();
        if (hbitmap == IntPtr.Zero)
        {
            return null;
        }

        BitmapSource result = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, palette, sourceRect, sizeOptions);
        _ = DeleteObject(hbitmap);
        return result;
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}

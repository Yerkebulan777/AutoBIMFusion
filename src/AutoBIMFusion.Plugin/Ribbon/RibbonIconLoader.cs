using System.Diagnostics;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace AutoBIMFusion.Plugin.Ribbon;

/// <summary>
///     Загружает иконки для кнопок Ribbon из ресурсов bundle.
/// </summary>
internal static class RibbonIconLoader
{
    /// <summary>
    ///     Загружает иконку из файла PNG относительно директории bundle.
    /// </summary>
    /// <param name="fileName">Имя файла иконки (например, "icon-merge-dwg-16.png").</param>
    /// <returns>BitmapImage иконки или null при ошибке.</returns>
    public static BitmapImage? Load(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains("..")
            || Path.IsPathRooted(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\'))
        {
            Debug.WriteLine($"Invalid icon file name: {fileName}");
            return null;
        }

        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string path = Path.Combine(dir, "Resources", fileName);

        if (!File.Exists(path))
        {
            return null;
        }

        BitmapImage image = new();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

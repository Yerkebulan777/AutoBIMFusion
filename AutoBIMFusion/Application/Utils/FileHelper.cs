namespace AutoBIMFusion.Application.Utils;

/// <summary>
/// Вспомогательные методы для работы с приложением.
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Проверяет доступность файла для чтения.
    /// </summary>
    /// <param name="path">Полный путь к файлу.</param>
    /// <param name="warn">Предупреждение при ошибке (null при успехе).</param>
    /// <returns>true, если файл доступен для чтения.</returns>
    internal static bool TryValidateFile(string path, FileShare share, out string warn)
    {
        ArgumentNullException.ThrowIfNull(path);

        warn = "Файл не найден";

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, share);

            if (fs.Length == 0)
            {
                warn = "Пустой файл";
                return false;
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            warn = "Нет доступа к файлу";
        }
        catch (IOException)
        {
            warn = "Не удалось прочитать файл";
        }

        return false;
    }

    /// <summary>
    /// Проверяет структуру DWG-файла, чтобы убедиться, что он не поврежден и соответствует формату DWG.
    /// </summary>
    public static bool TryValidateDwgStructure(string filePath, out string warn)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        try
        {
            using Database db = new(false, true);
            db.ReadDwgFile(filePath, FileOpenMode.OpenForReadAndReadShare, true, string.Empty);
            warn = string.Empty;
            return true;
        }
        catch (System.Exception ex)
        {
            warn = ex.Message;
            return false;
        }
    }
}

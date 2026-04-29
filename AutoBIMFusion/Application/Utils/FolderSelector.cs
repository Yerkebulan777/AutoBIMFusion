using System.Runtime.Versioning;

namespace AutoBIMFusion.Application.Utils;

/// <summary>
/// Предоставляет диалог выбора папки с DWG файлами.
/// </summary>
[SupportedOSPlatform("Windows")]
internal static class FolderSelector
{
    /// <summary>
    /// Показывает диалог выбора папки и возвращает путь.
    /// </summary>
    /// <param name="folderPath">Выбранный путь или пустая строка при отмене.</param>
    /// <returns>true, если папка выбрана успешно.</returns>
    internal static bool TrySelectFolder(out string folderPath)
    {
        return UiDialogService.TrySelectFolder("Выберите папку с файлами DWG для объединения", out folderPath);
    }
}

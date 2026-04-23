using System.Runtime.Versioning;
using System.Windows.Forms;

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
        using FolderBrowserDialog dialog = new()
        {
            Description = "Выберите папку с файлами DWG для объединения",
            RootFolder = Environment.SpecialFolder.Desktop,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            folderPath = dialog.SelectedPath;
            return true;
        }

        _ = MessageBox.Show("Отменено пользователем.", "MERGEDWG");
        folderPath = string.Empty;
        return false;
    }
}

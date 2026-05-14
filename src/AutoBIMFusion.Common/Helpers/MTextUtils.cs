namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Утилиты для работы с MText-форматированием и экранированием строк.
/// </summary>
public static class MTextUtils
{
    /// <summary>
    ///     Экранирует текст для безопасной вставки в MText.
    ///     Обратный слэш → \\\\, фигурные скобки → \\{ \\}, перевод строки → \\P, возврат каретки удаляется.
    /// </summary>
    public static string EscapeMTextContent(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Экранируем обратный слэш первым, иначе последующие замены его задвоят
        return text
            .Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("\n", "\\P")
            .Replace("\r", "");
    }
}

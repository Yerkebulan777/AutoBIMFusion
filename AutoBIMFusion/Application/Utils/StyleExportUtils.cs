using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AutoBIMFusion.Application.Utils;

/// <summary>
/// Общий утилитарный класс для экспорта данных AutoCAD в файлы.
/// Выполняет всю "грязную" работу, исключая дублирование кода.
/// </summary>
public static class StyleExportUtils
{
    /// <summary>
    /// Универсальный метод для выгрузки любой SymbolTable (таблицы стилей/слоев) в Markdown.
    /// </summary>
    public static void ExportSymbolTableToMd(Database db, ObjectId tableId, string fileName, string title, string itemLabel, Editor ed)
    {
        // Формируем путь к рабочему столу
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string filePath = Path.Combine(desktopPath, fileName);

        try
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Открываем переданную таблицу как общую SymbolTable
                SymbolTable symTable = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);

                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    writer.WriteLine($"# {title}");
                    writer.WriteLine($"**Дата выгрузки:** {DateTime.Now}\n");

                    // Перебираем все записи в таблице
                    foreach (ObjectId id in symTable)
                    {
                        // Читаем запись как общую SymbolTableRecord
                        SymbolTableRecord record = (SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead);

                        writer.WriteLine($"## {itemLabel}: `{record.Name}`");

                        // Выгружаем свойства с помощью рефлексии
                        DumpProperties(record, writer);

                        writer.WriteLine("---\n");
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nГотово! Отчет '{title}' сохранен: {filePath}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nОшибка при экспорте '{title}': {ex.Message}");
        }
    }

    /// <summary>
    /// Извлекает все свойства объекта и пишет их в формате Markdown-таблицы.
    /// </summary>
    private static void DumpProperties(object obj, StreamWriter writer)
    {
        PropertyInfo[] props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        writer.WriteLine("| Название свойства | Значение |");
        writer.WriteLine("|---|---|");

        foreach (PropertyInfo prop in props)
        {
            try
            {
                object? value = prop.GetValue(obj, null);
                string strVal = value?.ToString() ?? "null";

                // Экранируем символ `|` и убираем переносы строк `\n`, 
                // чтобы не сломать форматирование таблицы в Markdown.
                strVal = strVal.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");

                writer.WriteLine($"| {prop.Name} | {strVal} |");
            }
            catch (System.Exception)
            {
                writer.WriteLine($"| {prop.Name} | *ошибка чтения* |");
            }
        }
    }
}

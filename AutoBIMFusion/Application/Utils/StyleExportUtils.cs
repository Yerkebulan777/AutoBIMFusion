using System.ComponentModel;
using System.Reflection;

namespace AutoBIMFusion.Application.Utils;

internal static class StyleExportUtils
{
    internal static void ExportSymbolTableToMd(Database db, ObjectId tableId, string fileName, string title, string itemLabel, Editor ed, params string[] additionalStyleNamesToSkip)
    {
        HashSet<string> stylesToSkip = new(StringComparer.OrdinalIgnoreCase)
        {
            "ISO-25",
            "Standard",
            "Annotative"
        };

        foreach (string styleName in additionalStyleNamesToSkip)
        {
            if (!string.IsNullOrWhiteSpace(styleName))
            {
                stylesToSkip.Add(styleName);
            }
        }

        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string filePath = Path.Combine(desktopPath, fileName);

        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nНе удалось удалить существующий файл '{filePath}': {ex.Message}");
                return;
            }
        }

        try
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                SymbolTable symTable = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);

                using (StreamWriter writer = new(filePath))
                {
                    writer.WriteLine($"# {title}");
                    writer.WriteLine($"**Дата выгрузки:** {DateTime.Now}\n");

                    foreach (ObjectId id in symTable)
                    {
                        SymbolTableRecord record = (SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead);

                        if (stylesToSkip.Contains(record.Name))
                        {
                            continue;
                        }

                        writer.WriteLine($"## {itemLabel}: `{record.Name}`");

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

                // Экранируем | и \n чтобы не сломать Markdown-таблицу.
                strVal = strVal.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");

                string displayName = GetPropertyDisplayName(prop);

                writer.WriteLine($"| {displayName} | {strVal} |");
            }
            catch (System.Exception)
            {
                writer.WriteLine($"| {prop.Name} | *ошибка чтения* |");
            }
        }
    }

    private static string GetPropertyDisplayName(PropertyInfo prop)
    {
        DisplayNameAttribute? displayNameAttr = prop.GetCustomAttribute<DisplayNameAttribute>();
        return displayNameAttr != null && !string.IsNullOrEmpty(displayNameAttr.DisplayName) ? displayNameAttr.DisplayName : prop.Name;
    }
}

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoBIMFusion.AutoCAD.Helpers;

/// <summary>
/// Утилиты для работы со стилями текста и размеров.
/// </summary>
public static class StyleUtils
{
    /// <summary>
    /// Создаёт (или возвращает существующий) текстовый стиль с заданным шрифтом и высотой 0.
    /// </summary>
    public static ObjectId GetOrCreateTextStyle(Database db, Transaction trx, string fontName)
    {
        TextStyleTable tt = (TextStyleTable)trx.GetObject(db.TextStyleTableId, OpenMode.ForRead);

        if (tt.Has(fontName))
        {
            TextStyleTableRecord existing = (TextStyleTableRecord)trx.GetObject(tt[fontName], OpenMode.ForRead);
            if (existing.TextSize > 0.0)
            {
                existing.UpgradeOpen();
                existing.TextSize = 0.0;
            }

            return tt[fontName];
        }

        if (!tt.IsWriteEnabled)
        {
            tt.UpgradeOpen();
        }

        TextStyleTableRecord ts = new()
        {
            Name = fontName,
            FileName = fontName + ".shx",
            TextSize = 0.0
        };

        ObjectId id = tt.Add(ts);
        trx.AddNewlyCreatedDBObject(ts, true);
        return id;
    }

    /// <summary>
    /// Гарантированно получает ObjectId системного блока стрелки.
    /// </summary>
    public static ObjectId GetArrowBlockId(Database db, Transaction trx, string arrowName = "_ArchTick")
    {
        ObjectId arrObjId = db.Dimblk;

        object obj = Application.GetSystemVariable("DIMBLK");

        if (obj is string oldArrName && !string.IsNullOrEmpty(oldArrName))
        {
            try
            {
                Application.SetSystemVariable("DIMBLK", arrowName);
            }
            catch
            {
                return arrObjId;
            }

            if (!string.IsNullOrEmpty(oldArrName))
            {
                Application.SetSystemVariable("DIMBLK", oldArrName);
            }

            BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

            if (bt.Has(arrowName))
            {
                arrObjId = bt[arrowName];
            }
        }

        return arrObjId;
    }

    /// <summary>
    /// Формирует имя стиля на основе шрифта, высоты и модификаторов.
    /// </summary>
    public static string BuildStyleName(TextStyleTableRecord ts)
    {
        string fontBase = ResolveBaseFontName(ts);
        FontDescriptor font = ts.Font;

        string heightPart = ts.TextSize > 0 
            ? ts.TextSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) 
            : string.Empty;

        string modifiers = (font.Bold ? "B" : string.Empty) + (font.Italic ? "I" : string.Empty);

        string name = fontBase;

        if (heightPart.Length > 0)
        {
            name += "-" + heightPart;
        }

        if (modifiers.Length > 0)
        {
            name += "-" + modifiers;
        }

        return string.IsNullOrWhiteSpace(name) ? "TextStyle" : name;
    }

    /// <summary>
    /// Разрешает базовое имя шрифта.
    /// </summary>
    public static string ResolveBaseFontName(TextStyleTableRecord ts)
    {
        return !string.IsNullOrWhiteSpace(ts.FileName)
            ? Path.GetFileNameWithoutExtension(ts.FileName)
            : !string.IsNullOrWhiteSpace(ts.Font.TypeFace) ? ts.Font.TypeFace : ts.Name;
    }

    /// <summary>
    /// Делает имя уникальным в рамках коллекции.
    /// </summary>
    public static string MakeUnique(string candidate, HashSet<string> existing, string currentName)
    {
        if (!existing.Contains(candidate) || StringComparer.OrdinalIgnoreCase.Equals(candidate, currentName))
        {
            return candidate;
        }

        for (int i = 2; i < 1000; i++)
        {
            string suffixed = $"{candidate}_{i}";
            if (!existing.Contains(suffixed))
            {
                return suffixed;
            }
        }

        return candidate;
    }
}

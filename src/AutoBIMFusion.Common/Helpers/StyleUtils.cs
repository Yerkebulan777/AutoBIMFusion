using Autodesk.AutoCAD.GraphicsInterface;
using System.Globalization;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Утилиты для работы со стилями текста и размеров.
/// </summary>
public static class StyleUtils
{
    /// <summary>
    ///     Создаёт (или возвращает существующий) текстовый стиль с заданным шрифтом и высотой.
    /// </summary>
    public static ObjectId GetOrCreateTextStyle(Database db, Transaction trx, string fontName, double xScale = 1.0, bool isItalic = false)
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

            if (Abs(existing.XScale - xScale) > 1e-6)
            {
                if (!existing.IsWriteEnabled)
                {
                    existing.UpgradeOpen();
                }

                existing.XScale = xScale;
            }

            if (existing.Font.Italic != isItalic)
            {
                if (!existing.IsWriteEnabled)
                {
                    existing.UpgradeOpen();
                }

                FontDescriptor oldFont = existing.Font;

                existing.Font = new FontDescriptor(oldFont.TypeFace, oldFont.Bold, isItalic, oldFont.CharacterSet, oldFont.PitchAndFamily);
            }

            return tt[fontName];
        }

        if (!tt.IsWriteEnabled)
        {
            tt.UpgradeOpen();
        }

        // Side-database guard: new DBObject internally binds to WorkingDatabase.
        // Without this, tt.Add throws eWrongDatabase when db != active document.
        Database prevWorking = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;
        try
        {
            TextStyleTableRecord ts = new()
            {
                Name = fontName,
                TextSize = 0.0,
                XScale = xScale
            };

            if (isItalic)
            {
                ts.Font = new FontDescriptor(fontName, false, true, 0, 34);
            }
            else
            {
                ts.FileName = fontName + ".shx";
            }

            ObjectId id = tt.Add(ts);
            trx.AddNewlyCreatedDBObject(ts, true);
            return id;
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = prevWorking;
        }
    }

    /// <summary>
    ///  Гарантированно получает ObjectId системного блока стрелки.
    /// </summary>
    public static ObjectId GetArrowBlockId(Database db, Transaction trx, string arrowName = "_ArchTick")
    {
        ObjectId arrObjId = db.Dimblk;

        var obj = Application.GetSystemVariable("DIMBLK");

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
    ///  Формирует имя стиля на основе шрифта, высоты и модификаторов.
    /// </summary>
    public static string BuildStyleName(TextStyleTableRecord ts)
    {
        var fontBase = ResolveBaseFontName(ts);
        FontDescriptor font = ts.Font;

        var heightPart = ts.TextSize > 0
            ? ts.TextSize.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;

        var modifiers = (font.Bold ? "B" : string.Empty) + (font.Italic ? "I" : string.Empty);

        var name = fontBase;

        if (heightPart.Length > 0)
        {
            name += "-" + heightPart;
        }

        if (modifiers.Length > 0)
        {
            name += "-" + modifiers;
        }

        return name;
    }

    /// <summary>
    ///  Разрешает базовое имя шрифта.
    /// </summary>
    public static string ResolveBaseFontName(TextStyleTableRecord ts)
    {
        return !string.IsNullOrWhiteSpace(ts.FileName)
            ? Path.GetFileNameWithoutExtension(ts.FileName)
            : !string.IsNullOrWhiteSpace(ts.Font.TypeFace)
            ? ts.Font.TypeFace
            : ts.Name;
    }

    /// <summary>
    ///  Делает имя уникальным в рамках коллекции.
    /// </summary>
    public static string MakeUnique(string candidate, HashSet<string> existing, string currentName)
    {
        if (!existing.Contains(candidate) || StringComparer.OrdinalIgnoreCase.Equals(candidate, currentName))
        {
            return candidate;
        }

        for (var i = 2; i < 1000; i++)
        {
            var suffixed = $"{candidate}_{i}";
            if (!existing.Contains(suffixed))
            {
                return suffixed;
            }
        }

        return candidate;
    }
}

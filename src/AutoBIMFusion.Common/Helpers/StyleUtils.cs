using Autodesk.AutoCAD.GraphicsInterface;
using System.Globalization;
using System.Diagnostics;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;

namespace AutoBIMFusion.Common.Helpers;

/// <summary>
/// Утилиты для работы со стилями текста и размеров.
/// </summary>
public static class StyleUtils
{
    /// <summary>
    ///  Создаёт (или возвращает существующий) текстовый стиль с заданным шрифтом и высотой.
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

        // Защита side-database: новый DBObject internally привязывается к WorkingDatabase.
        // Без этого tt.Add выбрасывает eWrongDatabase, когда db != active document.
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

        if (obj is not string oldArrName || string.IsNullOrEmpty(oldArrName))
        {
            return arrObjId;
        }

        var dimblkChanged = false;

        try
        {
            Application.SetSystemVariable("DIMBLK", arrowName);
            dimblkChanged = true;
        }
        catch (AcadException ex)
        {
            Debug.WriteLine($"Не удалось установить DIMBLK: {ex.Message}");
            return arrObjId;
        }
        finally
        {
            if (dimblkChanged)
            {
                try
                {
                    Application.SetSystemVariable("DIMBLK", oldArrName);
                }
                catch (AcadException ex)
                {
                    Debug.WriteLine($"Не удалось восстановить DIMBLK: {ex.Message}");
                }
            }
        }

        BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

        if (bt.Has(arrowName))
        {
            arrObjId = bt[arrowName];
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

        int len = candidate.Length;
        // Allocate a few extra characters just in case the loop bound increases in the future
        Span<char> buffer = stackalloc char[len + 16];
        candidate.AsSpan().CopyTo(buffer);
        buffer[len] = '_';

        for (var i = 2; i < 1000; i++)
        {
            i.TryFormat(buffer.Slice(len + 1), out int charsWritten);
            var suffixed = new string(buffer.Slice(0, len + 1 + charsWritten));
            if (!existing.Contains(suffixed))
            {
                return suffixed;
            }
        }

        return candidate;
    }
}

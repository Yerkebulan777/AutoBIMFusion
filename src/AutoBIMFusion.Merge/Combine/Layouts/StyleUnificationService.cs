using AutoBIMFusion.Common.Helpers;

namespace AutoBIMFusion.Merge.Combine.Layouts;

internal static class StyleUnificationService
{
    /// <summary>
    ///     Переименовывает текстовые стили в исходной БД по схеме {шрифт}-{высота}-{модификаторы}.
    ///     Защищает целевую БД от коллизий имён при WblockCloneObjects.
    ///     TextSize стилей не трогаем — всё фиксируется постфактум в целевой БД.
    /// </summary>
    internal static void NormalizeTextStyleNames(Database sourceDb, Transaction trx)
    {
        TextStyleTable tt = (TextStyleTable)trx.GetObject(sourceDb.TextStyleTableId, OpenMode.ForRead);

        HashSet<string> allNames = new(StringComparer.OrdinalIgnoreCase);

        List<ObjectId> toRename = [];

        foreach (ObjectId tsId in tt)
        {
            TextStyleTableRecord ts = (TextStyleTableRecord)trx.GetObject(tsId, OpenMode.ForRead);

            if (ts.IsErased || ts.IsDependent)
            {
                continue;
            }

            _ = allNames.Add(ts.Name);

            if (ts.Name == "Standard" || ts.Name.StartsWith('*'))
            {
                continue;
            }

            toRename.Add(tsId);
        }

        foreach (ObjectId tsId in toRename)
        {
            TextStyleTableRecord ts = (TextStyleTableRecord)trx.GetObject(tsId, OpenMode.ForRead);

            string newName = StyleUtils.BuildStyleName(ts);

            newName = StyleUtils.MakeUnique(newName, allNames, ts.Name);

            if (StringComparer.OrdinalIgnoreCase.Equals(newName, ts.Name))
            {
                continue;
            }

            if (!ts.IsWriteEnabled)
            {
                ts.UpgradeOpen();
            }

            _ = allNames.Remove(ts.Name);
            ts.Name = newName;
            _ = allNames.Add(newName);
        }
    }

    /// <summary>
    ///     Применяет параметры ГОСТ (высота текста, стрелки, отступы) ко всем размерным стилям
    ///     в ИСХОДНОЙ базе данных. Это заставляет AutoCAD автоматически пересчитать
    ///     свойство TextPosition (DXF 11) у всех размеров до того, как они будут клонированы.
    /// </summary>
    internal static void ApplyGostToAllStyles(Database sourceDb, Transaction trx, string fontName = "Gost Common")
    {
        DimStyleTable dst = (DimStyleTable)trx.GetObject(sourceDb.DimStyleTableId, OpenMode.ForRead);

        // Gost Common: XScale = 0.85, Italic = true
        ObjectId textStyleId = StyleUtils.GetOrCreateTextStyle(sourceDb, trx, fontName, 0.85, 0.0, true);
        ObjectId arrowBlockId = StyleUtils.GetArrowBlockId(sourceDb, trx);

        foreach (ObjectId dsId in dst)
        {
            DimStyleTableRecord ds = (DimStyleTableRecord)trx.GetObject(dsId, OpenMode.ForRead);
            if (ds.IsErased || ds.IsDependent)
            {
                continue;
            }

            if (!ds.IsWriteEnabled)
            {
                ds.UpgradeOpen();
            }

            ApplyGostDimensionStyle(ds, textStyleId, arrowBlockId);
        }
    }

    /// <summary>
    ///     Создаёт (или возвращает существующий) эталонный размерный стиль AutoBIM в целевой БД.
    ///     TextSize связанного текстового стиля строго = 0.0 — иначе AutoCAD игнорирует Dimtxt.
    /// </summary>
    internal static ObjectId GetOrCreateStandardDimensionStyle(Database targetDb, Transaction trx, string fontName = "Gost Common")
    {
        string dimStyleName = $"AutoBIM-{fontName}";

        DimStyleTable dst = (DimStyleTable)trx.GetObject(targetDb.DimStyleTableId, OpenMode.ForRead);

        // Gost Common: XScale = 0.85, Italic = true
        ObjectId textStyleId = StyleUtils.GetOrCreateTextStyle(targetDb, trx, fontName, 0.85, 0.0, true);

        ObjectId arrowBlockId = StyleUtils.GetArrowBlockId(targetDb, trx);

        if (dst.Has(dimStyleName))
        {
            DimStyleTableRecord existing = (DimStyleTableRecord)trx.GetObject(dst[dimStyleName], OpenMode.ForWrite);
            ApplyGostDimensionStyle(existing, textStyleId, arrowBlockId);
            return existing.ObjectId;
        }

        if (!dst.IsWriteEnabled)
        {
            dst.UpgradeOpen();
        }

        DimStyleTableRecord dsr = new()
        {
            Name = dimStyleName
        };

        ApplyGostDimensionStyle(dsr, textStyleId, arrowBlockId);

        ObjectId id = dst.Add(dsr);
        trx.AddNewlyCreatedDBObject(dsr, true);
        return id;
    }

    private static void ApplyGostDimensionStyle(DimStyleTableRecord dsr, ObjectId textStyleId, ObjectId arrowBlockId)
    {
        // 1. ТЕКСТ
        dsr.Dimtxsty = textStyleId; // стиль текста размера
        dsr.Dimtxt = 2.5; // высота текста
        dsr.Dimtad = 1; // положение текста над размерной линией
        dsr.Dimjust = 0; // выравнивание текста по центру
        dsr.Dimgap = 0.25; // отступ текста от линии
        dsr.Dimtih = false; // текст внутри выносных линий НЕ горизонтально (выравнивается вдоль линии)
        dsr.Dimtoh = false; // текст снаружи выносных линий НЕ горизонтально (выравнивается вдоль линии)
        dsr.Dimtfill = 0; // фон текста: без заливки

        // 2. СИМВОЛЫ И СТРЕЛКИ
        dsr.Dimasz = 1.25; // размер стрелок
        dsr.Dimtsz = 1.25; // размер засечки
        dsr.Dimsah = true; // отдельные стрелки для концов
        dsr.Dimblk1 = arrowBlockId; // стрелка на 1-м конце
        dsr.Dimblk2 = arrowBlockId; // стрелка на 2-м конце

        // 3. ЛИНИИ
        dsr.Dimexe = 1.5; // вылет выносной линии за размерную
        dsr.Dimexo = 1.5; // отступ выносной от объекта
        dsr.Dimdli = 5.0; // шаг базовых размеров
        dsr.Dimdle = 3.0; // удлинение размерной линии
        dsr.Dimfxlen = 0; // фиксированная длина выносной
        dsr.DimfxlenOn = false; // фиксированная длина

        // 4. РАЗМЕЩЕНИЕ
        dsr.Dimatfit = 3; // что выносить, если не хватает места (3 = текст или стрелки — лучший вариант)
        dsr.Dimtmove = 1; // поведение текста при перемещении (1 = строить выноску)
        dsr.Dimtofl = true; // рисовать линию при тексте снаружи
        dsr.Dimtix = false; // принудительно внутри
        dsr.Dimsoxd = true; // подавление вне-размерных линий
        dsr.Dimupt = false; // позицию текста рассчитывает AutoCAD
        dsr.Dimscale = 1.0; // общий масштаб размера

        // 5. ОСНОВНЫЕ ЕДИНИЦЫ
        dsr.Dimlunit = 2; // формат единиц: десятичный
        dsr.Dimdec = 0; // точность (знаков после запятой)
        dsr.Dimdsep = ','; // десятичный разделитель
        dsr.Dimrnd = 0.5; // округление значения
        dsr.Dimlfac = 1.0; // коэффициент масштаба единиц
        dsr.Dimzin = 8; // подавление нулей
        dsr.Dimgap = 0.5; // отступ текста от линии (повтор, но для надежности)
        dsr.Dimaunit = 1; // формат угловых единиц
        dsr.Dimadec = 0; // точность углов
        dsr.Dimazin = 2; // подавление нулей в углах

        // 6. ДОПУСКИ
        dsr.Dimtol = false; // допуски
        dsr.Dimlim = false; // предельные отклонения
        dsr.Dimtp = 0.0; // верхний допуск
        dsr.Dimtm = 0.0; // нижний допуск
        dsr.Dimtdec = 2; // точность допусков
        dsr.Dimtzin = 8; // подавление нулей в допусках
        dsr.Dimtolj = 1; // выравнивание допусков
        dsr.Dimtfac = 1.0; // масштаб высоты допусков

        // 8. АННОТАТИВНОСТЬ
        dsr.Annotative = AnnotativeStates.False;
    }
}

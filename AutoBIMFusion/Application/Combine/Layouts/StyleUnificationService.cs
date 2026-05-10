namespace AutoBIMFusion.Application.Combine.Layouts;

internal static class StyleUnificationService
{
    /// <summary>
    /// Переименовывает текстовые стили в исходной БД по схеме {шрифт}-{высота}-{модификаторы}.
    /// Защищает целевую БД от коллизий имён при WblockCloneObjects.
    /// TextSize стилей не трогаем — всё фиксируется постфактум в целевой БД.
    /// </summary>
    internal static void NormalizeTextStyleNames(Database sourceDb, Transaction trx)
    {
        TextStyleTable tt = (TextStyleTable)trx.GetObject(sourceDb.TextStyleTableId, OpenMode.ForRead);

        List<ObjectId> toRename = [];
        HashSet<string> allNames = new(StringComparer.OrdinalIgnoreCase);

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
            string newName = BuildStyleName(ts);
            newName = MakeUnique(newName, allNames, ts.Name);

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
    /// Создаёт (или возвращает существующий) эталонный размерный стиль AutoBIM в целевой БД.
    /// TextSize связанного текстового стиля строго = 0.0 — иначе AutoCAD игнорирует Dimtxt.
    /// </summary>
    internal static ObjectId GetOrCreateStandardDimensionStyle(Database targetDb, Transaction trx, string fontName = "ISOCPEUR")
    {
        string dimStyleName = $"AutoBIM-{fontName}";

        DimStyleTable dst = (DimStyleTable)trx.GetObject(targetDb.DimStyleTableId, OpenMode.ForRead);
        ObjectId textStyleId = GetOrCreateTextStyle(targetDb, trx, fontName);

        ObjectId arrowBlockId = GetArrowBlockId(targetDb, trx);

        if (dst.Has(dimStyleName))
        {
            DimStyleTableRecord existing = (DimStyleTableRecord)trx.GetObject(dst[dimStyleName], OpenMode.ForWrite);
            ApplyGostDimensionStyleDefaults(existing, textStyleId, arrowBlockId);
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

        ApplyGostDimensionStyleDefaults(dsr, textStyleId, arrowBlockId);

        ObjectId id = dst.Add(dsr);
        trx.AddNewlyCreatedDBObject(dsr, true);
        return id;
    }

    private static void ApplyGostDimensionStyleDefaults(DimStyleTableRecord dsr, ObjectId textStyleId, ObjectId arrowBlockId)
    {
        // 1. ТЕКСТ
        dsr.Dimtxsty = textStyleId;  // стиль текста размера
        dsr.Dimtxt = 2.5;            // высота текста
        dsr.Dimtad = 1;              // положение текста над размерной линией
        dsr.Dimjust = 0;             // выравнивание текста по центру
        dsr.Dimgap = 0.5;            // отступ текста от линии
        dsr.Dimtih = false;          // текст внутри горизонтально
        dsr.Dimtoh = false;          // текст снаружи горизонтально
        dsr.Dimtfill = 0;            // фон текста: без заливки

        // 2. СИМВОЛЫ И СТРЕЛКИ
        dsr.Dimasz = 1.25;           // размер стрелок
        dsr.Dimtsz = 1.25;           // размер засечки
        dsr.Dimsah = true;           // отдельные стрелки для концов
        dsr.Dimblk1 = arrowBlockId;  // стрелка на 1-м конце
        dsr.Dimblk2 = arrowBlockId;  // стрелка на 2-м конце

        // 3. ЛИНИИ
        dsr.Dimexe = 1.5;            // вылет выносной линии за размерную
        dsr.Dimexo = 1.5;            // отступ выносной от объекта
        dsr.Dimdli = 5.0;            // шаг базовых размеров
        dsr.Dimdle = 3.0;            // удлинение размерной линии
        dsr.Dimfxlen = 0;            // фиксированная длина выносной
        dsr.DimfxlenOn = false;      // фиксированная длина

        // 4. РАЗМЕЩЕНИЕ
        dsr.Dimatfit = 3;            // алгоритм подгонки текста/стрелок
        dsr.Dimtmove = 1;            // поведение при переносе текста
        dsr.Dimtofl = true;          // рисовать линию при тексте снаружи
        dsr.Dimtix = false;          // принудительно внутри
        dsr.Dimsoxd = true;          // подавление вне-размерных линий
        dsr.Dimupt = true;           // пользовательское положение текста
        dsr.Dimscale = 1.0;          // общий масштаб размера

        // 5. ОСНОВНЫЕ ЕДИНИЦЫ
        dsr.Dimlunit = 2;            // формат единиц: десятичный
        dsr.Dimdec = 0;              // точность (знаков после запятой)
        dsr.Dimdsep = ',';           // десятичный разделитель
        dsr.Dimrnd = 0.5;            // округление значения
        dsr.Dimlfac = 1.0;           // коэффициент масштаба единиц
        dsr.Dimzin = 8;              // подавление нулей
        dsr.Dimaunit = 1;            // формат угловых единиц
        dsr.Dimadec = 0;             // точность углов
        dsr.Dimazin = 2;             // подавление нулей в углах

        // 6. ДОПУСКИ
        dsr.Dimtol = false;          // допуски
        dsr.Dimlim = false;          // предельные отклонения
        dsr.Dimtp = 0.0;             // верхний допуск
        dsr.Dimtm = 0.0;             // нижний допуск
        dsr.Dimtdec = 2;             // точность допусков
        dsr.Dimtzin = 8;             // подавление нулей в допусках
        dsr.Dimtolj = 1;             // выравнивание допусков
        dsr.Dimtfac = 1.0;           // масштаб высоты допусков

        // 8. АННОТАТИВНОСТЬ
        dsr.Annotative = AnnotativeStates.False;
    }

    private static ObjectId GetOrCreateTextStyle(Database targetDb, Transaction trx, string fontName)
    {
        TextStyleTable tt = (TextStyleTable)trx.GetObject(targetDb.TextStyleTableId, OpenMode.ForRead);

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

    private static ObjectId GetArrowBlockId(Database db, Transaction trx)
    {
        BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

        if (bt.Has("_ArchTick"))
        {
            Console.WriteLine("_ArchTick");
            return bt["_ArchTick"];
        }

        if (bt.Has("Двойная засечка"))
        {
            Console.WriteLine("Двойная засечка");
            return bt["Двойная засечка"];
        }

        return db.Dimblk;
    }

    private static string BuildStyleName(TextStyleTableRecord ts)
    {
        string fontBase = ResolveBaseFontName(ts);

        string heightPart;

        if (ts.TextSize > 0)
        {
            heightPart = ts.TextSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            heightPart = string.Empty;
        }

        Autodesk.AutoCAD.GraphicsInterface.FontDescriptor font = ts.Font;
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

    private static string ResolveBaseFontName(TextStyleTableRecord ts)
    {
        return !string.IsNullOrWhiteSpace(ts.FileName)
            ? Path.GetFileNameWithoutExtension(ts.FileName)
            : !string.IsNullOrWhiteSpace(ts.Font.TypeFace) ? ts.Font.TypeFace : ts.Name;
    }

    private static string MakeUnique(string candidate, HashSet<string> existing, string currentName)
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

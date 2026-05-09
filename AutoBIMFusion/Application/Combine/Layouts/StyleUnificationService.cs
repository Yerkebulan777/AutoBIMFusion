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

            allNames.Add(ts.Name);

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

            allNames.Remove(ts.Name);
            ts.Name = newName;
            allNames.Add(newName);
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

        if (dst.Has(dimStyleName))
        {
            DimStyleTableRecord existing = (DimStyleTableRecord)trx.GetObject(dst[dimStyleName], OpenMode.ForWrite);
            ApplyGostDimensionStyleDefaults(existing, textStyleId);
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

        ApplyGostDimensionStyleDefaults(dsr, textStyleId);

        ObjectId id = dst.Add(dsr);
        trx.AddNewlyCreatedDBObject(dsr, true);
        return id;
    }

    private static void ApplyGostDimensionStyleDefaults(DimStyleTableRecord dsr, ObjectId textStyleId)
    {
        dsr.Dimtxsty = textStyleId; // Ссылка на текстовый стиль
        dsr.Dimtxt = 2.5;  // Высота текста размерных надписей
        dsr.Dimtsz = 2.5;  // Высота текста размерных линий
        dsr.Dimasz = 1.5;  // Размер стрелок
        dsr.Dimexe = 1.25; // Длина выносной линии
        dsr.Dimexo = 0.5; // Смещение выносной линии
        dsr.Dimgap = 0.5;  // Промежуток между текстом и линией размерной надписи
        dsr.Dimtad = 1; // Выравнивание текста размерной надписи
        dsr.Dimtih = false; // Вписать текст в размерную линию
        dsr.Dimtoh = false; // Вписать текст в выносную линию
        dsr.Dimtmove = 1; // Перемещение текста размерной надписи
        dsr.Dimtfill = 0; // Заполнение текста размерной надписи
        dsr.Dimscale = 1.0; // Масштаб размерной надписи
        dsr.Dimlfac = 1.0; // Коэффициент длины линии
        dsr.Dimdec = 0; // Количество знаков после запятой
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

    private static string BuildStyleName(TextStyleTableRecord ts)
    {
        string fontBase = ResolveBaseFontName(ts);

        string heightPart = ts.TextSize > 1e-9
            ? ts.TextSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

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
        if (!string.IsNullOrWhiteSpace(ts.FileName))
        {
            return Path.GetFileNameWithoutExtension(ts.FileName);
        }

        if (!string.IsNullOrWhiteSpace(ts.Font.TypeFace))
        {
            return ts.Font.TypeFace;
        }

        return ts.Name;
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

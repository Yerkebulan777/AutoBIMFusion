namespace AutoBIMFusion.Application.Combine.Layouts;

internal static class DimensionStyleNormalizer
{
    private const string AcadRegAppName = "ACAD";
    private const string LegacyDstyleRegAppName = "DSTYLE";
    private const string DimensionStyleOverrideMarker = "DSTYLE";
    private const string AcadDimensionStyleDictionaryPrefix = "ACAD_DSTYLE";

    /// <summary>
    /// Назначает эталонный стиль всем скопированным размерам и удаляет DSTYLE overrides.
    /// Вызывать после TransformBy, чтобы RecomputeDimensionBlock использовал финальные координаты.
    /// </summary>
    internal static void NormalizeClonedDimensions(
        IdMapping idMap,
        Transaction trx,
        ObjectId targetDimStyleId,
        double targetVisualScale,
        double linearScaleMultiplier)
    {
        HashSet<ObjectId> normalizedDimensions = [];

        foreach (IdPair pair in idMap)
        {
            if (!pair.IsCloned || pair.Value.IsNull || pair.Value.IsErased)
            {
                continue;
            }

            if (!normalizedDimensions.Add(pair.Value))
            {
                continue;
            }

            if (trx.GetObject(pair.Value, OpenMode.ForWrite, false) is not Dimension dim)
            {
                continue;
            }

            // 1. Назначаем стиль до очистки — AutoCAD не подтягивает значения старого стиля при очистке оверрайдов.
            dim.DimensionStyle = targetDimStyleId;
            ClearDimensionStyleOverrides(dim, trx);

            // 2. Сбрасываем правило перемещения текста и его поворот.
            dim.Dimtmove = 0;
            dim.TextRotation = 0.0;

            // 3. Масштабы.
            dim.Dimscale = targetVisualScale;
            dim.Dimlfac = linearScaleMultiplier;

            // 4. Toggle hack: AutoCAD не обновляет позицию при простом true — нужно передёрнуть.
            dim.UsingDefaultTextPosition = false;
            dim.UsingDefaultTextPosition = true;
            if (dim.Elevation != 0.0) dim.Elevation = 0.0;

            dim.RecomputeDimensionBlock(true);
        }
    }

    /// <summary>
    /// Полный сброс визуального состояния размера с применением нового стиля.
    /// Не удаляет и не пересоздаёт объект — ObjectId и Handle сохраняются.
    /// </summary>
    internal static void HardResetDimensionStyle(ObjectId dimId, ObjectId newStyleId)
    {
        Database db = dimId.Database;
        using Transaction trx = db.TransactionManager.StartTransaction();

        if (trx.GetObject(dimId, OpenMode.ForWrite, false) is not Dimension dim)
        {
            trx.Commit();
            return;
        }

        // Шаг 1: Возврат текста на место ДО смены стиля.
        // Обнуляет вектор пользовательского смещения текста в текущем масштабе стиля.
        // TextRotation должен обнуляться первым — иначе UsingDefaultTextPosition не сработает.
        dim.TextRotation = 0.0;
        dim.UsingDefaultTextPosition = true;

        // Шаг 2: Очистка всех переопределений стиля (XData и словарь расширений).
        ClearDimensionStyleOverrides(dim, trx);

        // Шаг 3: Сброс поворота текста и правила его перемещения.
        dim.TextRotation = 0.0;
        dim.Dimtmove = 0;

        // Шаг 4: Назначение нового стиля.
        dim.DimensionStyle = newStyleId;

        // Шаг 5: Toggle hack — AutoCAD не обновляет позицию при простом true, нужно передёрнуть.
        dim.UsingDefaultTextPosition = false;
        dim.UsingDefaultTextPosition = true;
        // Явный сброс Z-координаты текста (Elevation bug).
        Point3d textPos = dim.TextPosition;
        if (textPos.Z != 0)
        {
            dim.TextPosition = new Point3d(textPos.X, textPos.Y, 0);
        }

        // Шаг 6: Перестроение блока размера с финальными координатами.
        dim.RecomputeDimensionBlock(true);

        trx.Commit();
    }

    private static void ClearDimensionStyleOverrides(Dimension dim, Transaction trx)
    {
        // Overrides от WblockCloneObjects могут хранить неверный масштаб 304.8.
        _ = RemoveDimensionStyleXDataOverrides(dim);
        _ = RemoveDimensionStyleDictionaryOverrides(dim, trx);
    }

    private static bool RemoveDimensionStyleXDataOverrides(Dimension dim)
    {
        ResultBuffer? xdata = dim.XData;
        if (xdata is null)
        {
            return false;
        }

        TypedValue[] values;
        using (xdata)
        {
            values = xdata.AsArray();
        }

        if (!RemoveDimensionStyleOverrideSections(values, out List<TypedValue> cleanedValues))
        {
            return false;
        }

        if (cleanedValues.Count == 0)
        {
            dim.XData = null;
            return true;
        }

        using ResultBuffer cleaned = new(cleanedValues.ToArray());
        dim.XData = cleaned;
        return true;
    }

    private static bool RemoveDimensionStyleDictionaryOverrides(Dimension dim, Transaction trx)
    {
        if (dim.ExtensionDictionary.IsNull || dim.ExtensionDictionary.IsErased)
        {
            return false;
        }

        DBDictionary extensionDictionary = (DBDictionary)trx.GetObject(dim.ExtensionDictionary, OpenMode.ForRead, false);
        List<ObjectId> overrideIds = [];

        foreach (DBDictionaryEntry entry in extensionDictionary)
        {
            if (entry.Key.StartsWith(AcadDimensionStyleDictionaryPrefix, StringComparison.OrdinalIgnoreCase)
                && !entry.Value.IsNull
                && !entry.Value.IsErased)
            {
                overrideIds.Add(entry.Value);
            }
        }

        foreach (ObjectId overrideId in overrideIds)
        {
            DBObject overrideObject = trx.GetObject(overrideId, OpenMode.ForWrite, false);
            if (!overrideObject.IsErased)
            {
                overrideObject.Erase();
            }
        }

        return overrideIds.Count > 0;
    }

    private static void RemoveAcadDstyleDictionaryEntry(Dimension dim, Transaction trx)
    {
        if (dim.ExtensionDictionary.IsNull || dim.ExtensionDictionary.IsErased)
        {
            return;
        }

        // ACAD_DSTYLE хранит локальные переопределения стиля, которые AutoCAD применяет
        // поверх DimensionStyle. Пока эта запись существует, смена стиля не даёт эффекта.
        DBDictionary dict = (DBDictionary)trx.GetObject(
            dim.ExtensionDictionary, OpenMode.ForWrite, false);

        if (!dict.Contains(AcadDimensionStyleDictionaryPrefix))
        {
            return;
        }

        ObjectId removedId = dict.Remove(AcadDimensionStyleDictionaryPrefix);

        if (!removedId.IsNull && !removedId.IsErased)
        {
            DBObject obj = trx.GetObject(removedId, OpenMode.ForWrite, false);
            if (!obj.IsErased)
            {
                obj.Erase();
            }
        }
    }

    private static bool RemoveDimensionStyleOverrideSections(TypedValue[] values, out List<TypedValue> cleanedValues)
    {
        cleanedValues = [];
        bool changed = false;

        for (int i = 0; i < values.Length;)
        {
            if (!IsRegApp(values[i], out string? appName))
            {
                cleanedValues.Add(values[i]);
                i++;
                continue;
            }

            int sectionStart = i;
            i++;

            List<TypedValue> sectionValues = [];
            while (i < values.Length && !IsRegApp(values[i], out _))
            {
                sectionValues.Add(values[i]);
                i++;
            }

            List<TypedValue> cleanedSection = sectionValues;
            bool sectionChanged = false;

            if (appName!.Equals(AcadRegAppName, StringComparison.OrdinalIgnoreCase))
            {
                sectionChanged = RemoveAcadDimensionStyleOverrideSection(sectionValues, out cleanedSection);
            }
            else if (appName.Equals(LegacyDstyleRegAppName, StringComparison.OrdinalIgnoreCase))
            {
                cleanedSection = [];
                sectionChanged = true;
            }

            if (!sectionChanged)
            {
                cleanedValues.Add(values[sectionStart]);
                cleanedValues.AddRange(sectionValues);
                continue;
            }

            changed = true;
            if (cleanedSection.Count == 0)
            {
                continue;
            }

            cleanedValues.Add(values[sectionStart]);
            cleanedValues.AddRange(cleanedSection);
        }

        return changed;
    }

    private static bool RemoveAcadDimensionStyleOverrideSection(List<TypedValue> sectionValues, out List<TypedValue> cleanedSection)
    {
        cleanedSection = [];
        bool changed = false;

        for (int i = 0; i < sectionValues.Count; i++)
        {
            TypedValue value = sectionValues[i];
            if (!IsDimensionStyleOverrideMarker(value))
            {
                cleanedSection.Add(value);
                continue;
            }

            changed = true;
            i = SkipOverridePayload(sectionValues, i);
        }

        return changed;
    }

    private static int SkipOverridePayload(List<TypedValue> sectionValues, int markerIndex)
    {
        int i = markerIndex + 1;
        if (i >= sectionValues.Count || !IsControlString(sectionValues[i], "{"))
        {
            return markerIndex;
        }

        int depth = 0;
        for (; i < sectionValues.Count; i++)
        {
            if (IsControlString(sectionValues[i], "{"))
            {
                depth++;
            }
            else if (IsControlString(sectionValues[i], "}"))
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return sectionValues.Count - 1;
    }

    private static bool IsRegApp(TypedValue value, out string? appName)
    {
        appName = value.Value as string;
        return value.TypeCode == (int)DxfCode.ExtendedDataRegAppName && appName is not null;
    }

    private static bool IsDimensionStyleOverrideMarker(TypedValue value)
    {
        return value.TypeCode == (int)DxfCode.ExtendedDataAsciiString
            && value.Value is string marker
            && marker.Equals(DimensionStyleOverrideMarker, StringComparison.Ordinal);
    }

    private static bool IsControlString(TypedValue value, string expected)
    {
        return value.TypeCode == (int)DxfCode.ExtendedDataControlString
            && value.Value is string control
            && control.Equals(expected, StringComparison.Ordinal);
    }
}

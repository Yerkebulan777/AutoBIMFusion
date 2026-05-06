using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Утилиты для работы с размерами AutoCAD.
/// </summary>
internal static class DimensionUtils
{
    private const string AcadRegAppName = "ACAD";
    private const string AcadDimensionStyleDictionaryPrefix = "ACAD_DSTYLE";
    private const string LegacyDstyleRegAppName = "DSTYLE";
    private const string DimensionStyleOverrideMarker = "DSTYLE";

    /// <summary>
    /// Удаляет переопределения размерного стиля (DSTYLE), хранящиеся в XData объекта.
    /// </summary>
    /// <param name="dim">Объект размера.</param>
    /// <returns>True, если переопределения были найдены и удалены.</returns>
    internal static bool TryRemoveDimensionStyleOverrides(Dimension dim)
    {
        try
        {
            bool xdataChanged = TryRemoveDimensionStyleXDataOverrides(dim);
            bool dictionaryChanged = TryRemoveDimensionStyleDictionaryOverrides(dim);

            return xdataChanged || dictionaryChanged;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            LoggerFactory.GetSharedLogger().Warning(ex, "Не удалось удалить переопределения DSTYLE для размера {Handle}", dim.Handle);
        }

        return false;
    }

    private static bool TryRemoveDimensionStyleXDataOverrides(Dimension dim)
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

        if (!TryRemoveDimensionStyleOverrideSection(values, out List<TypedValue> cleanedValues))
        {
            return false;
        }

        if (!dim.IsWriteEnabled)
        {
            dim.UpgradeOpen();
        }

        if (cleanedValues.Count == 0)
        {
            dim.XData = null;
        }
        else
        {
            using ResultBuffer cleaned = new(cleanedValues.ToArray());
            dim.XData = cleaned;
        }

        return true;
    }

    private static bool TryRemoveDimensionStyleDictionaryOverrides(Dimension dim)
    {
        if (dim.ExtensionDictionary.IsNull || dim.ExtensionDictionary.IsErased)
        {
            return false;
        }

        DBDictionary extensionDictionary = (DBDictionary)dim.Database.TransactionManager.TopTransaction.GetObject(dim.ExtensionDictionary, OpenMode.ForRead);
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

        if (overrideIds.Count == 0)
        {
            return false;
        }

        foreach (ObjectId overrideId in overrideIds)
        {
            DBObject overrideObject = dim.Database.TransactionManager.TopTransaction.GetObject(overrideId, OpenMode.ForWrite);
            if (!overrideObject.IsErased)
            {
                overrideObject.Erase();
            }
        }

        return true;
    }

    private static bool TryRemoveDimensionStyleOverrideSection(TypedValue[] values, out List<TypedValue> cleanedValues)
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
            // IsRegApp возвращает true только при appName != null — оператор ! документирует этот инвариант.
            if (appName!.Equals(AcadRegAppName, StringComparison.OrdinalIgnoreCase))
            {
                sectionChanged = TryRemoveAcadDimensionStyleOverrideSection(sectionValues, out cleanedSection);
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

    private static bool TryRemoveAcadDimensionStyleOverrideSection(
        List<TypedValue> sectionValues,
        out List<TypedValue> cleanedSection)
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
        return value.TypeCode == (int)DxfCode.ExtendedDataRegAppName
            && appName is not null;
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

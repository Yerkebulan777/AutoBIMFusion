# Legacy unit-mismatch workarounds

Архив старых обходов проблемы скрытого масштабирования AutoCAD при `WblockCloneObjects` между базами с разными `Insunits`/`Measurement`. Все фрагменты заменены единым `DatabaseUnitSyncScope` (`src/AutoBIMFusion.AutoCAD/AcadSupport/DatabaseUnitSyncScope.cs`), дата 2026-05-07.

Пути в исторических фрагментах ниже оставлены как архивный контекст до модульного рефакторинга 2026-05-10.

---

## 1. Manual Insunits/Measurement swap (BlockInserter.cs)

Фрагмент из `AutoBIMFusion/Application/Combine/BlockInserter.cs` (строки 71-122 до рефакторинга): ручное сохранение/восстановление единиц вокруг `WblockCloneObjects` плюс мутация `targetMs.Units`.

```csharp
UnitsValue originalTargetDbUnits = targetDb.Insunits;
MeasurementValue originalTargetDbMeasurement = targetDb.Measurement;
Extents3d? worldBounds = null;
int clonedCount = 0;

try
{
    using Transaction targetTr = targetDb.TransactionManager.StartTransaction();
    BlockTableRecord targetMs = (BlockTableRecord)targetTr.GetObject(targetMsId, OpenMode.ForWrite);
    UnitsValue originalTargetMsUnits = targetMs.Units;

    targetDb.Insunits = sourceDb.Insunits;
    targetDb.Measurement = sourceDb.Measurement;
    targetMs.Units = targetDb.Insunits;

    using IdMapping map = new();
    targetDb.WblockCloneObjects(sourceIds, targetMsId, map, DuplicateRecordCloning.Ignore, false);

    foreach (IdPair pair in map)
    {
        if (!pair.IsCloned || !pair.IsPrimary)
        {
            continue;
        }

        if (targetTr.GetObject(pair.Value, OpenMode.ForWrite) is Entity ent)
        {
            ent.TransformBy(displacement);
            clonedCount++;

            Extents3d? ext = ExtentsUtils.TryGetExtents(ent);
            if (ext.HasValue)
            {
                worldBounds = worldBounds.HasValue
                    ? ExtentsUtils.Union(worldBounds.Value, ext.Value)
                    : ext.Value;
            }
        }
    }

    targetDb.Insunits = originalTargetDbUnits;
    targetDb.Measurement = originalTargetDbMeasurement;
    targetMs.Units = originalTargetMsUnits;

    targetTr.Commit();
}
finally
{
    targetDb.Insunits = originalTargetDbUnits;
    targetDb.Measurement = originalTargetDbMeasurement;
    ExtentsUtils.SyncUnits(targetDb);
}
```

---

## 2. `btr.Units = targetDb.Insunits` loop (BlockInserter.cs)

Фрагмент из `BlockInserter.cs` строки 41-51 до рефакторинга: перезапись `Units` у всех BTR в source перед клонированием.

```csharp
using (Transaction trx = sourceDb.TransactionManager.StartTransaction())
{
    BlockTable bt = (BlockTable)trx.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);

    foreach (ObjectId btrId in bt)
    {
        if (trx.GetObject(btrId, OpenMode.ForWrite) is BlockTableRecord btr && !btr.IsFromExternalReference)
        {
            btr.Units = targetDb.Insunits;
        }
    }
    // ... сбор sourceIds ...
    trx.Commit();
}
```

---

## 3. `AcadUnitScalingOverrideScope` (AcadWarningSuppressScope.cs)

Полный класс из `AutoBIMFusion/Application/AcadSupport/AcadWarningSuppressScope.cs` строки 66-77. Использовался в `CombineOrchestrator.cs:49`.

```csharp
/// <summary>
/// Устанавливает единицы вставки блоков (INSUNITSDEFSOURCE и INSUNITSDEFTARGET = 4 = мм)
/// для корректного масштабирования при WblockCloneObjects.
/// </summary>
internal sealed class AcadUnitScalingOverrideScope : SysVarScope
{
    public AcadUnitScalingOverrideScope()
    {
        Set("INSUNITSDEFSOURCE", 4);
        Set("INSUNITSDEFTARGET", 4);
    }
}
```

Использование:

```csharp
using (targetDoc.LockDocument())
using (new AcadUnitScalingOverrideScope())
{
    worldBounds = inserter.InsertNativeObjects(targetDoc.Database, sourceDb, layoutName, bounds.Value);
    // ...
}
```

---

## 4. `DimensionUtils.TryRemoveDimensionStyleOverrides`

Полный исходник `AutoBIMFusion/Application/Combine/Layouts/DimensionUtils.cs`. Удалял DSTYLE-переопределения, которые ядро AutoCAD создавало в XData и `ExtensionDictionary` размеров после клонирования между разными системами единиц.

```csharp
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
        if (xdata is null) return false;

        TypedValue[] values;
        using (xdata) { values = xdata.AsArray(); }

        if (!TryRemoveDimensionStyleOverrideSection(values, out List<TypedValue> cleanedValues))
            return false;

        if (!dim.IsWriteEnabled) dim.UpgradeOpen();

        if (cleanedValues.Count == 0) dim.XData = null;
        else
        {
            using ResultBuffer cleaned = new(cleanedValues.ToArray());
            dim.XData = cleaned;
        }
        return true;
    }

    private static bool TryRemoveDimensionStyleDictionaryOverrides(Dimension dim)
    {
        if (dim.ExtensionDictionary.IsNull || dim.ExtensionDictionary.IsErased) return false;

        DBDictionary extensionDictionary = (DBDictionary)dim.Database.TransactionManager.TopTransaction.GetObject(dim.ExtensionDictionary, OpenMode.ForRead);
        List<ObjectId> overrideIds = [];

        foreach (DBDictionaryEntry entry in extensionDictionary)
        {
            if (entry.Key.StartsWith(AcadDimensionStyleDictionaryPrefix, StringComparison.OrdinalIgnoreCase)
                && !entry.Value.IsNull && !entry.Value.IsErased)
            {
                overrideIds.Add(entry.Value);
            }
        }

        if (overrideIds.Count == 0) return false;

        foreach (ObjectId overrideId in overrideIds)
        {
            DBObject overrideObject = dim.Database.TransactionManager.TopTransaction.GetObject(overrideId, OpenMode.ForWrite);
            if (!overrideObject.IsErased) overrideObject.Erase();
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
            if (appName!.Equals(AcadRegAppName, StringComparison.OrdinalIgnoreCase))
                sectionChanged = TryRemoveAcadDimensionStyleOverrideSection(sectionValues, out cleanedSection);
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
            if (cleanedSection.Count == 0) continue;

            cleanedValues.Add(values[sectionStart]);
            cleanedValues.AddRange(cleanedSection);
        }

        return changed;
    }

    private static bool TryRemoveAcadDimensionStyleOverrideSection(List<TypedValue> sectionValues, out List<TypedValue> cleanedSection)
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
        if (i >= sectionValues.Count || !IsControlString(sectionValues[i], "{")) return markerIndex;

        int depth = 0;
        for (; i < sectionValues.Count; i++)
        {
            if (IsControlString(sectionValues[i], "{")) depth++;
            else if (IsControlString(sectionValues[i], "}"))
            {
                depth--;
                if (depth == 0) return i;
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
```

Вызывался в `ViewportTransformer.NormalizeDimensionsInsideViewport` (строки 309-312) и `FinalizeModelSpaceDimensionLinearScales` (строки 369-372) с инкрементом счётчика `overridesCleared`, который попадал в Serilog-логи.

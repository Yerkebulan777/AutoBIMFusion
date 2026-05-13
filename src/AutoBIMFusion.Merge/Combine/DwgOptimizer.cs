using AutoBIMFusion.Common.Helpers;
using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Выполняет глубокую программную очистку (Purge) базы данных DWG
///     перед сохранением итогового файла. Удаляет неиспользуемые слои,
///     блоки, стили текста, типы линий и другие именованные объекты.
/// </summary>
public static class DwgOptimizer
{
    /// <summary>
    /// Максимальное количество проходов очистки. Некоторые объекты
    /// становятся неиспользуемыми только после удаления других.
    /// </summary>
    private const int MaxPurgePasses = 5;

    public static void Optimize(Database db, Logger log)
    {
        var passes = 0;

        while (passes < MaxPurgePasses)
        {
            var passCount = PurgePass(db, log);
            if (passCount == 0) break;

            passes++;
        }
    }

    private static int PurgePass(Database db, Logger log)
    {
        using ObjectIdCollection candidates = [];

        using var trx = db.TransactionManager.StartTransaction();

        AddTableIds(trx, db.BlockTableId, candidates, log);
        AddTableIds(trx, db.LayerTableId, candidates, log);
        AddTableIds(trx, db.LinetypeTableId, candidates, log);
        AddTableIds(trx, db.TextStyleTableId, candidates, log);
        AddTableIds(trx, db.DimStyleTableId, candidates, log);
        AddTableIds(trx, db.RegAppTableId, candidates, log);
        AddTableIds(trx, db.UcsTableId, candidates, log);
        AddTableIds(trx, db.ViewTableId, candidates, log);
        AddTableIds(trx, db.ViewportTableId, candidates, log);

        AddDictionaryIds(trx, db.MLeaderStyleDictionaryId, candidates, log);
        AddDictionaryIds(trx, db.MaterialDictionaryId, candidates, log);
        AddDictionaryIds(trx, db.TableStyleDictionaryId, candidates, log);
        AddDictionaryIds(trx, db.PlotStyleNameDictionaryId, candidates, log);
        AddDictionaryIds(trx, db.GroupDictionaryId, candidates, log);
        AddDictionaryIds(trx, db.VisualStyleDictionaryId, candidates, log);

        if (candidates.Count == 0)
        {
            trx.Commit();
            return 0;
        }

        try
        {
            db.Purge(candidates);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Ошибка при вызове Database.Purge");
            trx.Commit();
            return 0;
        }

        if (candidates.Count == 0)
        {
            trx.Commit();
            return 0;
        }

        var erased = ErasePurgedObjects(trx, candidates, log);

        trx.Commit();
        return erased;
    }

    /// <summary>
    ///     Удаляет объекты, оставшиеся в коллекции после вызова <see cref="Database.Purge" />.
    /// </summary>
    internal static int ErasePurgedObjects(Transaction trx, ObjectIdCollection candidates, Logger log)
    {
        var erased = 0;
        foreach (ObjectId id in candidates)
        {
            if (!id.IsValidForOperation()) continue;

            try
            {
                var obj = trx.GetObject(id, OpenMode.ForWrite);
                obj.Erase();
                erased++;
            }
            catch (Exception ex)
            {
                log.Debug(ex, "Purge: не удалось удалить объект {Handle}", id.Handle);
            }
        }

        return erased;
    }

    private static void AddTableIds(Transaction trx, ObjectId tableId, ObjectIdCollection target, Logger log)
    {
        if (tableId.IsNull) return;

        try
        {
            var table = (SymbolTable)trx.GetObject(tableId, OpenMode.ForRead);
            foreach (var id in table)
                if (id.IsValidForOperation())
                    _ = target.Add(id);
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Purge: таблица {Handle} недоступна", tableId.Handle);
        }
    }

    private static void AddDictionaryIds(Transaction trx, ObjectId dictId, ObjectIdCollection target, Logger log)
    {
        if (dictId.IsNull) return;

        try
        {
            var dict = (DBDictionary)trx.GetObject(dictId, OpenMode.ForRead);
            foreach (var entry in dict)
            {
                var id = entry.Value;
                if (id.IsValidForOperation()) _ = target.Add(id);
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Purge: словарь {Handle} недоступен", dictId.Handle);
        }
    }
}

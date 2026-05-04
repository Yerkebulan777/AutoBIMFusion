using Serilog.Core;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Выполняет глубокую программную очистку (Purge) базы данных DWG
/// перед сохранением итогового файла. Удаляет неиспользуемые слои,
/// блоки, стили текста, типы линий и другие именованные объекты.
/// </summary>
internal static class DwgOptimizer
{
    /// <summary>
    /// Максимальное количество проходов очистки. Некоторые объекты
    /// становятся неиспользуемыми только после удаления других.
    /// </summary>
    private const int MaxPurgePasses = 5;

    public static void Optimize(Database db, Logger log)
    {
        log.Information("Очистка (Purge)...");

        int totalPurged = 0;
        int passes = 0;

        while (passes < MaxPurgePasses)
        {
            int passCount = PurgePass(db, log);
            if (passCount == 0)
            {
                break;
            }

            totalPurged += passCount;
            passes++;
        }

        if (totalPurged > 0)
        {
            log.Information($"Очищено: {totalPurged} объектов (проходов: {passes})");
        }
        else
        {
            log.Information("Очистка: неиспользуемых объектов нет");
        }
    }

    private static int PurgePass(Database db, Logger log)
    {
        using ObjectIdCollection candidates = [];

        using Transaction tr = db.TransactionManager.StartTransaction();

        AddTableIds(tr, db.BlockTableId, candidates, log);
        AddTableIds(tr, db.LayerTableId, candidates, log);
        AddTableIds(tr, db.LinetypeTableId, candidates, log);
        AddTableIds(tr, db.TextStyleTableId, candidates, log);
        AddTableIds(tr, db.DimStyleTableId, candidates, log);
        AddTableIds(tr, db.RegAppTableId, candidates, log);
        AddTableIds(tr, db.UcsTableId, candidates, log);
        AddTableIds(tr, db.ViewTableId, candidates, log);
        AddTableIds(tr, db.ViewportTableId, candidates, log);

        AddDictionaryIds(tr, db.MLeaderStyleDictionaryId, candidates, log);
        AddDictionaryIds(tr, db.MaterialDictionaryId, candidates, log);
        AddDictionaryIds(tr, db.TableStyleDictionaryId, candidates, log);
        AddDictionaryIds(tr, db.PlotStyleNameDictionaryId, candidates, log);
        AddDictionaryIds(tr, db.GroupDictionaryId, candidates, log);
        AddDictionaryIds(tr, db.VisualStyleDictionaryId, candidates, log);

        if (candidates.Count == 0)
        {
            tr.Commit();
            return 0;
        }

        try
        {
            db.Purge(candidates);
        }
        catch (System.Exception ex)
        {
            log.Warning(ex, "Ошибка при вызове Database.Purge");
            tr.Commit();
            return 0;
        }

        if (candidates.Count == 0)
        {
            tr.Commit();
            return 0;
        }

        int erased = 0;
        foreach (ObjectId id in candidates)
        {
            if (id.IsNull || id.IsErased)
            {
                continue;
            }

            try
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                obj.Erase();
                erased++;
            }
            catch (System.Exception ex)
            {
                log.Warning($"Не удалось удалить объект {id.Handle}: {ex.Message}");
            }
        }

        tr.Commit();
        return erased;
    }

    private static void AddTableIds(Transaction tr, ObjectId tableId, ObjectIdCollection target, Logger log)
    {
        if (tableId.IsNull)
        {
            return;
        }

        try
        {
            SymbolTable table = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                if (!id.IsNull && !id.IsErased)
                {
                    _ = target.Add(id);
                }
            }
        }
        catch (System.Exception ex)
        {
            log.Debug($"Purge: таблица {tableId.Handle} недоступна — {ex.Message}");
        }
    }

    private static void AddDictionaryIds(Transaction tr, ObjectId dictId, ObjectIdCollection target, Logger log)
    {
        if (dictId.IsNull)
        {
            return;
        }

        try
        {
            DBDictionary dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in dict)
            {
                ObjectId id = entry.Value;
                if (!id.IsNull && !id.IsErased)
                {
                    _ = target.Add(id);
                }
            }
        }
        catch (System.Exception ex)
        {
            log.Debug($"Purge: словарь {dictId.Handle} недоступен — {ex.Message}");
        }
    }
}

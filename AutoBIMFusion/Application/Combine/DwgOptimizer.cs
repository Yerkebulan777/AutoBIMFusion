using Serilog.Core;

namespace AutoBIMFusion.Application.Combine;

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

        using Transaction trx = db.TransactionManager.StartTransaction();

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
        catch (System.Exception ex)
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

        int erased = ErasePurgedObjects(trx, candidates, log);

        trx.Commit();
        return erased;
    }

    /// <summary>
    /// Удаляет объекты, оставшиеся в коллекции после вызова <see cref="Database.Purge"/>.
    /// </summary>
    internal static int ErasePurgedObjects(Transaction trx, ObjectIdCollection candidates, Logger log)
    {
        int erased = 0;
        foreach (ObjectId id in candidates)
        {
            if (id.IsNull || id.IsErased)
            {
                continue;
            }

            try
            {
                DBObject obj = trx.GetObject(id, OpenMode.ForWrite);
                obj.Erase();
                erased++;
            }
            catch (System.Exception ex)
            {
                log.Debug($"Purge: не удалось удалить объект {id.Handle} — {ex.Message}");
            }
        }

        return erased;
    }

    private static void AddTableIds(Transaction trx, ObjectId tableId, ObjectIdCollection target, Logger log)
    {
        if (tableId.IsNull)
        {
            return;
        }

        try
        {
            SymbolTable table = (SymbolTable)trx.GetObject(tableId, OpenMode.ForRead);
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

    private static void AddDictionaryIds(Transaction trx, ObjectId dictId, ObjectIdCollection target, Logger log)
    {
        if (dictId.IsNull)
        {
            return;
        }

        try
        {
            DBDictionary dict = (DBDictionary)trx.GetObject(dictId, OpenMode.ForRead);
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

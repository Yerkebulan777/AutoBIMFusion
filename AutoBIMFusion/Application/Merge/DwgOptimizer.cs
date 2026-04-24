using AutoBIMFusion.Infrastructure.Logging;

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

    public static void Optimize(Database db, OperationLogger log)
    {
        log.Info("Очистка (Purge)...");

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
            log.Info($"Очищено: {totalPurged} объектов (проходов: {passes})");
        }
        else
        {
            log.Info("Очистка: неиспользуемых объектов нет");
        }
    }

    private static int PurgePass(Database db, OperationLogger log)
    {
        ObjectIdCollection candidates = [];

        using Transaction tr = db.TransactionManager.StartTransaction();
        // Именованные таблицы
        AddTableIds(tr, db.BlockTableId, candidates);
        AddTableIds(tr, db.LayerTableId, candidates);
        AddTableIds(tr, db.LinetypeTableId, candidates);
        AddTableIds(tr, db.TextStyleTableId, candidates);
        AddTableIds(tr, db.DimStyleTableId, candidates);
        AddTableIds(tr, db.RegAppTableId, candidates);
        AddTableIds(tr, db.UcsTableId, candidates);
        AddTableIds(tr, db.ViewTableId, candidates);
        AddTableIds(tr, db.ViewportTableId, candidates);

        // Словари стилей и прочих именованных объектов
        AddDictionaryIds(tr, db.MLeaderStyleDictionaryId, candidates);
        AddDictionaryIds(tr, db.MaterialDictionaryId, candidates);
        AddDictionaryIds(tr, db.TableStyleDictionaryId, candidates);
        AddDictionaryIds(tr, db.PlotStyleNameDictionaryId, candidates);
        AddDictionaryIds(tr, db.GroupDictionaryId, candidates);
        AddDictionaryIds(tr, db.VisualStyleDictionaryId, candidates);

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
            log.Warn(ex, "Ошибка при вызове Database.Purge");
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
                log.Warn($"Не удалось удалить объект {id.Handle}: {ex.Message}");
            }
        }

        tr.Commit();
        return erased;
    }

    private static void AddTableIds(Transaction tr, ObjectId tableId, ObjectIdCollection target)
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
        catch
        {
            // Таблица недоступна — пропускаем
        }
    }

    private static void AddDictionaryIds(Transaction tr, ObjectId dictId, ObjectIdCollection target)
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
        catch
        {
            // Словарь недоступен — пропускаем
        }
    }
}

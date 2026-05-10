using Serilog.Core;
using AutoBIMFusion.AutoCAD.Helpers;
using AutoBIMFusion.Merge.Layouts;

namespace AutoBIMFusion.Merge.Combine.Layouts;

/// <summary>
/// Утилита для разбиения (Explode) блоков, попадающих в границы видовых экранов,
/// до начала клонирования и масштабирования. Это предотвращает искажение текста
/// внутри блоков при последующих трансформациях.
/// </summary>
internal static class BlockExplodeUtils
{
    /// <summary>
    /// Находит все BlockReference в ModelSpace, чьи габариты пересекаются
    /// хотя бы с одним окном из списка, и разбивает их рекурсивно.
    /// </summary>
    /// <param name="db">База данных чертежа.</param>
    /// <param name="spaceId">ObjectId записи BlockTableRecord для ModelSpace.</param>
    /// <param name="windows">Список границ видовых экранов (главный + вспомогательные).</param>
    /// <param name="log">Логгер.</param>
    internal static void ExplodeBlocksInWindows(Database db, ObjectId spaceId, IEnumerable<Extents3d> windows, Logger log)
    {
        List<Extents3d> windowList = windows as List<Extents3d> ?? windows.ToList();

        if (windowList.Count == 0)
        {
            return;
        }

        // Используем уже существующий механизм для быстрого сбора сущностей с габаритами
        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> snapshots =
            ViewportTransformer.CollectModelEntitiesWithExtents(db, spaceId, log);

        // Фильтруем: только BlockReference, пересекающиеся хотя бы с одним окном
        RXClass blockClass = RXObject.GetClass(typeof(BlockReference));
        List<ObjectId> blocksToExplode = [];

        foreach (ViewportTransformer.ModelEntitySnapshot snap in snapshots)
        {
            if (!snap.Id.ObjectClass.IsDerivedFrom(blockClass))
            {
                continue;
            }

            foreach (Extents3d window in windowList)
            {
                if (ExtentsUtils.AabbIntersect(window, snap.Extents))
                {
                    blocksToExplode.Add(snap.Id);
                    break;
                }
            }
        }

        if (blocksToExplode.Count == 0)
        {
            log.Debug("ExplodeBlocksInWindows: нет блоков для разбиения.");
            return;
        }

        log.Debug($"ExplodeBlocksInWindows: найдено {blocksToExplode.Count} блоков для разбиения.");

        int totalExploded = 0;
        int totalFailed = 0;

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord modelSpace = (BlockTableRecord)trx.GetObject(spaceId, OpenMode.ForWrite);

        // Очередь для обработки вложенных блоков
        Queue<ObjectId> queue = new(blocksToExplode);

        while (queue.Count > 0)
        {
            ObjectId blockId = queue.Dequeue();

            // Пропускаем уже удалённые или стёртые объекты
            if (blockId.IsNull || blockId.IsErased)
            {
                continue;
            }

            if (trx.GetObject(blockId, OpenMode.ForRead) is not BlockReference blockRef)
            {
                continue;
            }

            // Пропускаем блоки, у которых отключено расчленение
            if (!blockRef.BlockTableRecord.IsNull)
            {
                BlockTableRecord btr = (BlockTableRecord)trx.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead);
                if (!btr.Explodable)
                {
                    log.Warning($"ExplodeBlocksInWindows: блок '{btr.Name}' (Handle={blockRef.Handle}) не поддерживает расчленение — пропущен.");
                    totalFailed++;
                    continue;
                }
            }

            // Выполняем разбиение
            DBObjectCollection explodedObjects = [];
            try
            {
                blockRef.UpgradeOpen();
                blockRef.Explode(explodedObjects);
            }
            catch (System.Exception ex)
            {
                log.Warning($"ExplodeBlocksInWindows: не удалось разбить блок Handle={blockRef.Handle}: {ex.Message}");
                totalFailed++;
                continue;
            }

            // Добавляем полученные примитивы в ModelSpace и проверяем на вложенные блоки
            foreach (DBObject obj in explodedObjects)
            {
                if (obj is not Entity ent)
                {
                    continue;
                }

                ObjectId newId = modelSpace.AppendEntity(ent);
                trx.AddNewlyCreatedDBObject(ent, true);

                // Если разбитый объект — тоже блок, добавляем в очередь для рекурсивного разбиения
                if (ent is BlockReference nestedBlock)
                {
                    queue.Enqueue(newId);
                }
            }

            // Удаляем исходный блок
            blockRef.Erase();
            totalExploded++;
        }

        trx.Commit();

        log.Debug($"ExplodeBlocksInWindows: разбито {totalExploded} блоков, не удалось разбить {totalFailed}.");
    }
}

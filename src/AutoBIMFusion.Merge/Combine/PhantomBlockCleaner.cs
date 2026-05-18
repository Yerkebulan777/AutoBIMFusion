using AutoBIMFusion.Common.Helpers;
using Serilog.Core;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Обнаруживает и удаляет "фантомные" блоки — определения с маленьким BoundingBox,
///     геометрия которых аномально смещена от начала координат.
///     Такие блоки искажают <see cref="ExtentsUtils.GetDatabaseExtents"/> и ломают пайплайн слияния.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PhantomBlockCleaner
{
    private const double MaxBoundingBoxDiagonal = 15.0;
    private const double ThresholdMultiplier = 1.5;
    private const double DefaultThresholdBase = 1000.0;

    /// <summary>
    ///     Сканирует базу данных, удаляет все вхождения фантомных блоков во всех пространствах
    ///     и полностью очищает их определения из базы через <see cref="Database.Purge"/>.
    ///     Гарантирует, что phantom-геометрия не влияет на вычисление границ и не клонируется
    ///     при слиянии через <see cref="Database.WblockCloneObjects"/>.
    /// </summary>
    public static void Clean(Database db, Logger log)
    {
        log.Information("Запуск очистки фантомных блоков");

        double threshold = ComputeOffsetThreshold(db);
        log.Information("Порог смещения фантомных блоков: {Threshold:F2} ед.", threshold);

        HashSet<ObjectId> phantomBtrs = FindPhantomBlocks(db, threshold, log);
        log.Information("Найдено {DefinitionCount} определений фантомных блоков", phantomBtrs.Count);

        if (phantomBtrs.Count  > 0)
        {
            int erasedCount = EraseAllReferences(db, phantomBtrs);

            PurgeDefinitions(db, phantomBtrs);

            log.Information("Удалено {ErasedCount} вхождений, очищено {DefinitionCount} определений фантомных блоков", erasedCount, phantomBtrs.Count);
        }
    }

    /// <summary>
    ///     Вычисляет порог аномального смещения на основе максимальной диагонали Paper Space листов.
    ///     Fallback: <see cref="DefaultThresholdBase"/> единиц, если листы отсутствуют или имеют нулевой размер.
    /// </summary>
    private static double ComputeOffsetThreshold(Database db)
    {
        double maxDiagonal = 0.0;

        using Transaction trx = db.TransactionManager.StartTransaction();

        DBDictionary layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        foreach (DBDictionaryEntry entry in layoutDict)
        {
            Layout layout = (Layout)trx.GetObject(entry.Value, OpenMode.ForRead);

            if (layout.ModelType)
            {
                continue;
            }

            Point2d paperSize = layout.PlotPaperSize;
            double diagonal = Sqrt((paperSize.X * paperSize.X) + (paperSize.Y * paperSize.Y));

            if (diagonal > maxDiagonal)
            {
                maxDiagonal = diagonal;
            }
        }

        trx.Commit();

        double thresholdBase = maxDiagonal > 1.0 ? maxDiagonal : DefaultThresholdBase;
        return thresholdBase * ThresholdMultiplier;
    }

    /// <summary>
    ///     Проходит по всем пользовательским определениям блоков и возвращает идентификаторы фантомных.
    ///     Фантомом считается блок с маленькой диагональю BoundingBox и аномальным удалением от начала координат.
    /// </summary>
    private static HashSet<ObjectId> FindPhantomBlocks(Database db, double threshold, Logger log)
    {
        HashSet<ObjectId> result = [];

        using Transaction trx = db.TransactionManager.StartTransaction();

        BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId btrId in bt)
        {
            if (!btrId.IsValid || btrId.IsErased)
            {
                continue;
            }

            BlockTableRecord btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);

            // Пропускаем системные и внешние блоки
            if (btr.IsLayout || btr.IsFromExternalReference || btr.Name.StartsWith('*'))
            {
                log.Debug(
                    "PhantomBlockCleaner: пропущен блок «{Name}» (layout={IsLayout}, xref={IsXref}, anonymous={IsAnonymous})",
                    btr.Name,
                    btr.IsLayout,
                    btr.IsFromExternalReference,
                    btr.Name.StartsWith('*'));
                continue;
            }

            Extents3d? combined = null;
            int entityCount = 0;
            int extentsCount = 0;

            foreach (ObjectId entId in btr)
            {
                if (!entId.IsValid || entId.IsErased)
                {
                    continue;
                }

                entityCount++;

                if (trx.GetObject(entId, OpenMode.ForRead) is not Entity ent || ent.IsErased)
                {
                    log.Debug("PhantomBlockCleaner: блок «{Name}» содержит объект без Entity-геометрии", btr.Name);
                    continue;
                }

                Extents3d? ext = ExtentsUtils.TryGetExtents(ent);

                if (ext is null)
                {
                    log.Debug(
                        "PhantomBlockCleaner: для Entity {EntityType} в блоке «{Name}» не удалось вычислить BoundingBox",
                        ent.GetType().Name,
                        btr.Name);
                    continue;
                }

                extentsCount++;
                combined = combined is null ? ext.Value : ExtentsUtils.Union(combined.Value, ext.Value);
            }

            if (entityCount == 0)
            {
                log.Debug("PhantomBlockCleaner: блок «{Name}» пропущен, нет нестёртых entities", btr.Name);
                continue;
            }

            if (combined is null)
            {
                log.Debug(
                    "PhantomBlockCleaner: блок «{Name}» пропущен, BoundingBox не вычислен для {EntityCount} entities",
                    btr.Name,
                    entityCount);
                continue;
            }

            Extents3d bounds = combined.Value;
            double diagonal = bounds.MaxPoint.DistanceTo(bounds.MinPoint);
            double maxDistance = GetMaxDistanceFromOrigin(bounds);

            log.Debug(
                "PhantomBlockCleaner: блок «{Name}», entities={EntityCount}, extents={ExtentsCount}, bounds={Bounds}, diagonal={Diagonal:F2}, maxDistance={MaxDistance:F2}, threshold={Threshold:F2}",
                btr.Name,
                entityCount,
                extentsCount,
                ExtentsUtils.FormatExtents(bounds),
                diagonal,
                maxDistance,
                threshold);

            if (diagonal <= MaxBoundingBoxDiagonal && maxDistance > threshold)
            {
                _ = result.Add(btrId);
                log.Debug(
                    "PhantomBlockCleaner: блок «{Name}» принят как фантом, diagonal={Diagonal:F2}, maxDistance={MaxDistance:F2}",
                    btr.Name,
                    diagonal,
                    maxDistance);
                continue;
            }

            log.Debug(
                "PhantomBlockCleaner: блок «{Name}» не фантом, smallBounds={SmallBounds}, farFromOrigin={FarFromOrigin}",
                btr.Name,
                diagonal <= MaxBoundingBoxDiagonal,
                maxDistance > threshold);
        }

        trx.Commit();
        return result;
    }

    private static double GetMaxDistanceFromOrigin(Extents3d bounds)
    {
        Point3d min = bounds.MinPoint;
        Point3d max = bounds.MaxPoint;

        Point3d[] corners =
        [
            new(min.X, min.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z),
            new(min.X, max.Y, max.Z),
            new(max.X, min.Y, min.Z),
            new(max.X, min.Y, max.Z),
            new(max.X, max.Y, min.Z),
            new(max.X, max.Y, max.Z)
        ];

        double result = 0.0;

        foreach (Point3d corner in corners)
        {
            result = Max(result, corner.DistanceTo(Point3d.Origin));
        }

        return result;
    }

    /// <summary>
    ///     Стирает все вхождения (<see cref="BlockReference"/>) фантомных блоков во ВСЕХ пространствах
    ///     базы данных — Model Space, Paper Space и внутри других блоков.
    ///     Использует <see cref="BlockTableRecord.GetBlockReferenceIds"/> для поиска по всей базе.
    /// </summary>
    /// <returns>Количество удалённых вхождений.</returns>
    private static int EraseAllReferences(Database db, HashSet<ObjectId> phantomBtrs)
    {
        int count = 0;

        using Transaction trx = db.TransactionManager.StartTransaction();

        foreach (ObjectId btrId in phantomBtrs)
        {
            BlockTableRecord btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);

            // directOnly=true: только в текущей базе (без внешних xref)
            // forceOwnerXref=true: включая вхождения внутри других определений блоков
            ObjectIdCollection refs = btr.GetBlockReferenceIds(true, true);

            foreach (ObjectId refId in refs)
            {
                if (!refId.IsValid || refId.IsErased)
                {
                    continue;
                }

                BlockReference br = (BlockReference)trx.GetObject(refId, OpenMode.ForWrite);
                br.Erase();
                count++;
            }
        }

        trx.Commit();
        return count;
    }

    /// <summary>
    ///     Удаляет определения фантомных блоков (<see cref="BlockTableRecord"/>) из базы данных.
    ///     Вызывать только после <see cref="EraseAllReferences"/> — иначе <see cref="Database.Purge"/>
    ///     не пометит BTR как удаляемые и они останутся в базе.
    /// </summary>
    private static void PurgeDefinitions(Database db, HashSet<ObjectId> phantomBtrs)
    {
        using ObjectIdCollection ids = new(phantomBtrs.ToArray());

        // Purge модифицирует коллекцию: оставляет только те ID, которые реально можно удалить.
        // Если все вхождения стёрты — все BTR останутся в коллекции.
        db.Purge(ids);

        if (ids.Count > 0)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();

            foreach (ObjectId id in ids)
            {
                if (!id.IsErased)
                {
                    trx.GetObject(id, OpenMode.ForWrite).Erase();
                }
            }

            trx.Commit();
        }
    }
}

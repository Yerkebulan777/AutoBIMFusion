using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Удаляет из Model Space всё, что полностью лежит вне рамки-штампа (frameBounds).
/// frameBounds — bbox клонированных paper-объектов (то, что осталось от рамки/штампа
/// после переноса через главный viewport).
/// </summary>
internal static class ModelSpaceTrimmer
{
    /// <summary>
    /// Считает bounding-box указанных объектов (обычно — клонированный paper content).
    /// Возвращает null, если ни один объект не имеет валидных extents.
    /// </summary>
    internal static Extents3d? ComputeBounds(Database db, ObjectIdCollection entityIds, OperationLogger log)
    {
        if (entityIds.Count == 0)
        {
            log.Debug("ModelSpaceTrimmer.ComputeBounds: entityIds is empty");
            return null;
        }

        Extents3d? acc = null;

        using Transaction tr = db.TransactionManager.StartTransaction();

        foreach (ObjectId id in entityIds)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            Extents3d? ext = GeometryUtils.TryGetExtents(ent);

            if (ext is null)
            {
                continue;
            }

            acc = acc is null ? ext.Value : GeometryUtils.Union(acc.Value, ext.Value);
        }

        tr.Commit();
        if (acc.HasValue)
        {
            log.Debug($"ModelSpaceTrimmer.ComputeBounds: entities={entityIds.Count}, bounds={GeometryUtils.FormatExtents(acc.Value)}");
        }
        else
        {
            log.Debug($"ModelSpaceTrimmer.ComputeBounds: entities={entityIds.Count}, no valid extents");
        }

        return acc;
    }

    /// <summary>
    /// Удаляет из Model Space все сущности, чей bbox полностью вне frameBounds.
    /// Сущности без валидных extents (например, пустые блоки) пропускаются.
    /// </summary>
    internal static int TrimOutside(Database db, Extents3d frameBounds, OperationLogger log)
    {
        int erased = 0;
        int total = 0;
        int skippedNoExtents = 0;
        int inside = 0;
        int outside = 0;
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        using Transaction tr = db.TransactionManager.StartTransaction();
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            total++;

            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            Extents3d? ext = GeometryUtils.TryGetExtents(ent);

            if (ext is null)
            {
                skippedNoExtents++;
                continue;
            }

            if (!GeometryUtils.AabbIntersect(frameBounds, ext.Value))
            {
                outside++;
                ent.UpgradeOpen();
                ent.Erase();
                erased++;
            }
            else
            {
                inside++;
            }
        }

        tr.Commit();
        log.Debug(
            $"ModelSpaceTrimmer.TrimOutside frame={GeometryUtils.FormatExtents(frameBounds)}, total={total}, inside={inside}, " +
            $"outside={outside}, skippedNoExtents={skippedNoExtents}, erased={erased}");
        return erased;
    }

}

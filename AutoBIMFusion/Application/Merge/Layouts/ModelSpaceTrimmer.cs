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
    internal static Extents3d? ComputeBounds(Database db, ObjectIdCollection entityIds, AILog log)
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

            Extents3d? ext = ExtentsUtils.TryGetExtents(ent);

            if (ext is null)
            {
                continue;
            }

            acc = acc is null ? ext.Value : ExtentsUtils.Union(acc.Value, ext.Value);
        }

        tr.Commit();
        if (acc.HasValue)
        {
            log.Debug($"ModelSpaceTrimmer.ComputeBounds: entities={entityIds.Count}, bounds={ExtentsUtils.FormatExtents(acc.Value)}");
        }
        else
        {
            log.Debug($"ModelSpaceTrimmer.ComputeBounds: entities={entityIds.Count}, no valid extents");
        }

        return acc;
    }

    /// <summary>
    /// Вторичная защита: удаляет из Model Space все сущности, чей bbox не пересекает frameBounds.
    /// Сущности без валидных extents (например, пустые блоки) пропускаются.
    ///
    /// Замечание: этот метод НЕ является основным механизмом очистки объектов вспомогательных VP.
    /// Объекты aux VP, чьи модельные координаты попадают в диапазон frameBounds (охватывающий
    /// весь лист), не будут удалены здесь. Основная очистка выполняется в
    /// ViewportTransformer.EraseEntitiesOutsideMainWindow непосредственно после клонирования.
    /// </summary>
    internal static int TrimOutside(Database db, Extents3d frameBounds, AILog log)
    {
        int erased = 0;
        int total = 0;
        int skippedNoExtents = 0;
        int inside = 0;
        int outside = 0;
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        using Transaction tr = db.TransactionManager.StartTransaction();

        // Гарантируем актуальность границ всей БД перед началом фильтрации
        db.UpdateExt(true);

        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            total++;

            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            // Оптимизация: для простых точечных объектов (Text, Point, BlockReference)
            // проверяем их базовую точку. Если она ВНУТРИ frameBounds, то объект точно пересекается/внутри,
            // и мы можем пропустить дорогой вызов GeometricExtents.
            if (ExtentsUtils.IsEntityPointIn(ent, frameBounds))
            {
                inside++;
                continue;
            }

            Extents3d? ext = ExtentsUtils.TryGetExtents(ent);

            if (ext is null)
            {
                skippedNoExtents++;
                continue;
            }

            if (!ExtentsUtils.AabbIntersect(frameBounds, ext.Value))
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
            $"ModelSpaceTrimmer.TrimOutside frame={ExtentsUtils.FormatExtents(frameBounds)}, total={total}, inside={inside}, " +
            $"outside={outside}, skippedNoExtents={skippedNoExtents}, erased={erased}");
        return erased;
    }


}

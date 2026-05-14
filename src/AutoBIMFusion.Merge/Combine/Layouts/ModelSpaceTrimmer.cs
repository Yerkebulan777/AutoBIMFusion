using AutoBIMFusion.Common.Helpers;
using Serilog.Core;

namespace AutoBIMFusion.Merge.Combine.Layouts;

/// <summary>
///     Удаляет из Model Space всё, что полностью лежит вне рамки-штампа (frameBounds).
///     frameBounds — bbox клонированных paper-объектов (то, что осталось от рамки/штампа
///     после переноса через главный vpt).
/// </summary>
internal static class ModelSpaceTrimmer
{
    /// <summary>
    ///     Считает bounding-box указанных объектов (обычно — клонированный paper content).
    ///     Возвращает null, если ни один объект не имеет валидных extents.
    ///     Делегирует к <see cref="ExtentsUtils.ComputeBounds"/>.
    /// </summary>
    internal static Extents3d? ComputeBounds(Database db, ObjectIdCollection entityIds, Logger log)
    {
        Extents3d? result = ExtentsUtils.ComputeBounds(db, entityIds);

        if (result.HasValue)
        {
            log.Debug($"ModelSpaceTrimmer.ComputeBounds: entities={entityIds.Count}, bounds={ExtentsUtils.FormatExtents(result.Value)}");
        }

        return result;
    }

    /// <summary>
    ///     Вторичная защита: удаляет из Model Space все сущности, чей bbox не пересекает frameBounds.
    ///     Сущности без валидных extents (например, пустые блоки) пропускаются.
    ///     Замечание: этот метод НЕ является основным механизмом очистки объектов вспомогательных VP.
    ///     Объекты aux VP, чьи модельные координаты попадают в диапазон frameBounds (охватывающий
    ///     весь лист), не будут удалены здесь. Основная очистка выполняется в
    ///     ViewportTransformer.EraseEntitiesOutsideMainWindow непосредственно после клонирования.
    /// </summary>
    internal static int TrimOutside(Database db, Extents3d frameBounds, Logger log)
    {
        int erased = 0;

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        using Transaction trx = db.TransactionManager.StartTransaction();

        var ms = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            // Оптимизация: для простых точечных объектов (Text, Point, BlockReference)
            // проверяем их базовую точку. Если она ВНУТРИ frameBounds, то объект точно пересекается/внутри,
            // и мы можем пропустить дорогой вызов GeometricExtents.
            if (ExtentsUtils.IsEntityPointIn(ent, frameBounds))
            {
                continue;
            }

            Extents3d? ext = ExtentsUtils.TryGetExtents(ent);

            if (ext is null)
            {
                continue;
            }

            if (!ExtentsUtils.AabbIntersect(frameBounds, ext.Value))
            {
                ent.UpgradeOpen();
                ent.Erase();
                erased++;
            }
        }

        trx.Commit();
        log.Debug($"ModelSpaceTrimmer.TrimOutside erased={erased}");
        return erased;
    }
}

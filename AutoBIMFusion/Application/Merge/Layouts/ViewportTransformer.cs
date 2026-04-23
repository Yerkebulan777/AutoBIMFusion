using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Переводит содержимое вспомогательного viewport'а (узла) в модельные координаты
/// главного viewport'а и клонирует объекты на новом месте.
///
/// Математика: MainModelFromAuxModel = MainModelFromPaper ∘ PaperFromAuxModel.
///
/// PaperFromAuxModel(p) = CenterPaper_aux + Rot(-twist_aux) * (p - ViewCenter_aux) * scale_aux
/// MainModelFromPaper(p) = ViewCenter_main + Rot(+twist_main) * (p - CenterPaper_main) / scale_main
/// </summary>
internal static class ViewportTransformer
{
    internal sealed record ModelEntitySnapshot(ObjectId Id, Extents3d Extents);

    /// <summary>
    /// Матрица переноса «модель aux-VP → модель main-VP».
    /// </summary>
    internal static Matrix3d BuildMatrix(LayoutViewportInfo main, LayoutViewportInfo aux, OperationLogger log)
    {
        Vector3d z = Vector3d.ZAxis;
        Point3d origin = Point3d.Origin;

        Matrix3d tAux = Matrix3d.Displacement(origin - aux.ViewCenter);
        Matrix3d rAux = Matrix3d.Rotation(-aux.ViewTwist, z, origin);
        Matrix3d sAux = Matrix3d.Scaling(aux.CustomScale, origin);
        Matrix3d tPaper = Matrix3d.Displacement(aux.CenterPaper - main.CenterPaper);
        Matrix3d sMain = Matrix3d.Scaling(1.0 / main.CustomScale, origin);
        Matrix3d rMain = Matrix3d.Rotation(main.ViewTwist, z, origin);
        Matrix3d tMain = Matrix3d.Displacement(main.ViewCenter - origin);

        Matrix3d result = tMain * rMain * sMain * tPaper * sAux * rAux * tAux;

        log.Debug(
            $"BuildMatrix aux#{aux.Number} -> main#{main.Number}: " +
            $"auxScale={aux.CustomScale:F6}, mainScale={main.CustomScale:F6}, " +
            $"auxTwist={aux.ViewTwist:F6}, mainTwist={main.ViewTwist:F6}, " +
            $"auxWindow={GeometryUtils.FormatExtents(aux.ModelWindow)}");

        return result;
    }

    /// <summary>
    /// Матрица переноса «бумага → модель main-VP». Используется для содержимого Paper Space
    /// (рамка, штамп, тексты) когда его пересаживают в Model Space через главный VP.
    /// </summary>
    internal static Matrix3d BuildPaperToMainMatrix(LayoutViewportInfo main, OperationLogger log)
    {
        Vector3d z = Vector3d.ZAxis;
        Point3d origin = Point3d.Origin;

        Matrix3d tPaper = Matrix3d.Displacement(origin - main.CenterPaper);
        Matrix3d sMain = Matrix3d.Scaling(1.0 / main.CustomScale, origin);
        Matrix3d rMain = Matrix3d.Rotation(main.ViewTwist, z, origin);
        Matrix3d tMain = Matrix3d.Displacement(main.ViewCenter - origin);

        Matrix3d result = tMain * rMain * sMain * tPaper;

        log.Debug(
            $"BuildPaperToMainMatrix main#{main.Number}: " +
            $"mainScale={main.CustomScale:F6}, mainTwist={main.ViewTwist:F6}, " +
            $"centerPaper={GeometryUtils.FormatPoint(main.CenterPaper)}, viewCenter={GeometryUtils.FormatPoint(main.ViewCenter)}");

        return result;
    }

    internal static IReadOnlyList<ModelEntitySnapshot> CollectModelEntitiesWithExtents(Database db, ObjectId msId, OperationLogger log)
    {
        List<ModelEntitySnapshot> result = [];
        int total = 0;
        int skippedViewport = 0;
        int skippedNoExtents = 0;

        using Transaction tr = db.TransactionManager.StartTransaction();
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            total++;

            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            if (ent is Viewport)
            {
                skippedViewport++;
                continue;
            }

            Extents3d? ext = GeometryUtils.TryGetExtents(ent);
            if (ext is null)
            {
                skippedNoExtents++;
                continue;
            }

            result.Add(new ModelEntitySnapshot(id, ext.Value));
        }

        tr.Commit();
        log.Debug($"CollectModelEntitiesWithExtents total={total}, cached={result.Count}, skippedViewport={skippedViewport}, skippedNoExtents={skippedNoExtents}");
        return result;
    }

    internal static ObjectIdCollection DeepCloneAndTransform(
        Database db, ObjectIdCollection sourceIds, ObjectId sourceOwnerId, ObjectId ownerId,
        Matrix3d matrix, OperationLogger log, string sourceName)
    {
        IReadOnlyList<ObjectId> sourceOrder = DrawOrderPreserver.Capture(db, sourceOwnerId, sourceIds, log);

        IdMapping map = [];
        db.DeepCloneObjects(sourceIds, ownerId, map, false);

        ObjectIdCollection cloned = [];
        int mappedPrimary = 0;

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            foreach (IdPair pair in map)
            {
                if (!pair.IsCloned || !pair.IsPrimary)
                {
                    continue;
                }

                mappedPrimary++;

                if (tr.GetObject(pair.Value, OpenMode.ForWrite) is Entity e)
                {
                    e.TransformBy(matrix);
                    _ = cloned.Add(pair.Value);
                }
            }

            tr.Commit();
        }

        DrawOrderPreserver.Restore(db, ownerId, sourceOrder, map, log);

        log.Debug($"DeepCloneAndTransform source={sourceName}, input={sourceIds.Count}, mappedPrimary={mappedPrimary}, transformed={cloned.Count}");
        return cloned;
    }

    internal static ObjectIdCollection SelectModelInside(
        IReadOnlyList<ModelEntitySnapshot> modelEntities,
        Extents3d window,
        OperationLogger log)
    {
        ObjectIdCollection result = [];
        int outsideWindow = 0;

        foreach (ModelEntitySnapshot entity in modelEntities)
        {
            if (GeometryUtils.AabbIntersect(window, entity.Extents))
            {
                _ = result.Add(entity.Id);
            }
            else
            {
                outsideWindow++;
            }
        }

        log.Debug(
            $"SelectModelInside cached={modelEntities.Count}, selected={result.Count}, " +
            $"outsideWindow={outsideWindow}, window={GeometryUtils.FormatExtents(window)}");
        return result;
    }
}

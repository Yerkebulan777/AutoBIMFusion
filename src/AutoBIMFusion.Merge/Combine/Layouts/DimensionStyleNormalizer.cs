namespace AutoBIMFusion.Merge.Layouts;

internal static class DimensionStyleNormalizer
{
    /// <summary>
    /// Назначает эталонный стиль всем скопированным размерам и сбрасывает DSTYLE overrides.
    /// Вызывать после TransformBy, чтобы RecomputeDimensionBlock использовал финальные координаты.
    /// </summary>
    internal static void NormalizeClonedDimensions(
        IdMapping idMap,
        Transaction trx,
        ObjectId targetDimStyleId,
        double targetVisualScale,
        double linearScaleMultiplier)
    {
        DimStyleTableRecord styleRec = (DimStyleTableRecord)trx.GetObject(targetDimStyleId, OpenMode.ForRead);
        HashSet<ObjectId> normalizedDimensions = [];

        foreach (IdPair pair in idMap)
        {
            if (!pair.IsCloned || pair.Value.IsNull || pair.Value.IsErased)
            {
                continue;
            }

            if (!normalizedDimensions.Add(pair.Value))
            {
                continue;
            }

            if (trx.GetObject(pair.Value, OpenMode.ForWrite, false) is not Dimension dim)
            {
                continue;
            }

            dim.DimensionStyle = targetDimStyleId;
            dim.SetDimstyleData(styleRec);

            dim.TextRotation = 0.0;
            dim.Dimtmove = 0;
            dim.Dimscale = targetVisualScale;
            dim.Dimlfac = linearScaleMultiplier;
            ResetTextPositionForce(dim);

            TouchControlPoints(dim);
            dim.RecomputeDimensionBlock(true);
        }
    }

    /// <summary>
    /// Полный сброс визуального состояния размера с применением нового стиля.
    /// Не удаляет и не пересоздаёт объект — ObjectId и Handle сохраняются.
    /// </summary>
    internal static void HardResetDimensionStyle(ObjectId dimId, ObjectId newStyleId)
    {
        Database db = dimId.Database;
        using Transaction trx = db.TransactionManager.StartTransaction();

        if (trx.GetObject(dimId, OpenMode.ForWrite, false) is not Dimension dim)
        {
            trx.Commit();
            return;
        }

        DimStyleTableRecord styleRec = (DimStyleTableRecord)trx.GetObject(newStyleId, OpenMode.ForRead);

        dim.DimensionStyle = newStyleId;
        dim.SetDimstyleData(styleRec);

        dim.TextRotation = 0.0;
        dim.Dimtmove = 0;
        ResetTextPositionForce(dim);

        TouchControlPoints(dim);
        dim.RecomputeDimensionBlock(true);

        trx.Commit();
    }

    // AutoCAD игнорирует true, если флаг уже был включён после клонирования.
    private static void ResetTextPositionForce(Dimension dim)
    {
        dim.UsingDefaultTextPosition = false;
        dim.UsingDefaultTextPosition = true;

        Point3d textPt = dim.TextPosition;
        if (Math.Abs(textPt.Z) != 0)
        {
            dim.TextPosition = new Point3d(textPt.X, textPt.Y, 0);
        }
    }

    // Форсирует пересчёт OCS/WCS — AutoCAD кэширует старую геометрию до явного переприсваивания точек.
    private static void TouchControlPoints(Dimension dim)
    {
        if (dim is RotatedDimension rotDim)
        {
            rotDim.XLine1Point = rotDim.XLine1Point;
            rotDim.XLine2Point = rotDim.XLine2Point;
            rotDim.DimLinePoint = rotDim.DimLinePoint;
        }
        else if (dim is AlignedDimension alDim)
        {
            alDim.XLine1Point = alDim.XLine1Point;
            alDim.XLine2Point = alDim.XLine2Point;
            alDim.DimLinePoint = alDim.DimLinePoint;
        }
        else
        {
            dim.TextPosition = dim.TextPosition;
        }
    }
}


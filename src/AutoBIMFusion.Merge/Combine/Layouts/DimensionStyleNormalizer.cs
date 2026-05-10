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
            DimensionTextAnchor.AnchorTextToMidpoint(dim);
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
        DimensionTextAnchor.AnchorTextToMidpoint(dim);
        dim.RecomputeDimensionBlock(true);

        trx.Commit();
    }
}

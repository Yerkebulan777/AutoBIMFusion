namespace AutoBIMFusion.Application.Combine;

internal static class DimensionCleanupHelper
{
    /// <summary>
    /// Назначает эталонный стиль всем скопированным размерам и сбрасывает AEC XData overrides.
    /// Вызывать ПОСЛЕ TransformBy (displacement), чтобы RecomputeDimensionBlock использовал финальную позицию.
    /// </summary>
    internal static void UnifyClonedDimensions(
        IdMapping idMap,
        Transaction trx,
        ObjectId targetDimStyleId,
        double targetVisualScale,
        double linearScaleMultiplier)
    {
        foreach (IdPair pair in idMap)
        {
            if (!pair.IsCloned || !pair.IsPrimary)
            {
                continue;
            }

            if (pair.Value.IsNull || pair.Value.IsErased)
            {
                continue;
            }

            if (trx.GetObject(pair.Value, OpenMode.ForWrite, false) is not Dimension dim)
            {
                continue;
            }

            dim.DimensionStyle = targetDimStyleId;
            dim.XData = null;
            dim.Dimscale = targetVisualScale;
            dim.Dimlfac = linearScaleMultiplier;
            dim.RecomputeDimensionBlock(true);
        }
    }
}

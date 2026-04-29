namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Applies AutoCAD entity transforms with the required post-processing for
/// dimensions, multileaders, and hatches.
/// </summary>
internal static class EntityTransformUtils
{
    internal enum DimensionScaleOrder
    {
        BeforeTransform,
        AfterTransform
    }

    internal readonly record struct TransformResult(
        bool Transformed,
        bool SkippedAssociativeHatch,
        bool DimensionScaleAdjusted);

    internal static double GetScaleFactor(Matrix3d matrix)
    {
        return Vector3d.XAxis.TransformBy(matrix).Length;
    }

    internal static TransformResult TransformEntity(
        Entity entity,
        Matrix3d matrix,
        double scaleFactor,
        DimensionScaleOrder dimensionScaleOrder)
    {
        if (entity is Hatch { Associative: true })
        {
            return new TransformResult(false, true, false);
        }

        bool dimensionScaleAdjusted = false;

        if (entity is Dimension dimension && dimensionScaleOrder == DimensionScaleOrder.BeforeTransform)
        {
            AdjustDimensionScale(dimension, scaleFactor);
            dimensionScaleAdjusted = true;
        }

        entity.TransformBy(matrix);

        if (entity is Dimension transformedDimension)
        {
            if (dimensionScaleOrder == DimensionScaleOrder.AfterTransform)
            {
                AdjustDimensionScale(transformedDimension, scaleFactor);
                dimensionScaleAdjusted = true;
            }

            transformedDimension.RecomputeDimensionBlock(true);
        }
        else if (entity is MLeader mleader)
        {
            AdjustMLeaderScale(mleader, scaleFactor);
        }
        else if (entity is Hatch hatch)
        {
            EvaluateHatch(hatch);
        }

        return new TransformResult(true, false, dimensionScaleAdjusted);
    }

    private static void AdjustDimensionScale(Dimension dimension, double scaleFactor)
    {
        double currentDimscale = dimension.Dimscale == 0.0 ? 1.0 : dimension.Dimscale;
        dimension.Dimscale = currentDimscale * scaleFactor;

        if (scaleFactor > 0.0001)
        {
            dimension.Dimlfac /= scaleFactor;
        }
    }

    private static void AdjustMLeaderScale(MLeader mleader, double scaleFactor)
    {
        double currentScale = mleader.Scale == 0.0 ? 1.0 : mleader.Scale;
        mleader.Scale = currentScale * scaleFactor;
    }

    private static void EvaluateHatch(Hatch hatch)
    {
        try
        {
            hatch.EvaluateHatch(true);
        }
        catch
        {
            // AutoCAD can reject evaluation for damaged or very complex hatch geometry.
        }
    }
}

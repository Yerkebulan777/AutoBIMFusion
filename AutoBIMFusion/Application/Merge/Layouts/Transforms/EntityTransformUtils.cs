using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts.Transforms;

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
        DimensionScaleOrder dimensionScaleOrder,
        AILog? log = null,
        string diagnosticScenario = "unknown")
    {
        if (entity is Hatch { Associative: true })
        {
            return new TransformResult(false, true, false);
        }

        bool dimensionScaleAdjusted = false;

        if (entity is Dimension dimension)
        {
            dimensionScaleAdjusted = DimensionTransformUtils.TransformDimension(
                dimension,
                matrix,
                scaleFactor,
                dimensionScaleOrder,
                log,
                diagnosticScenario);
        }
        else
        {
            entity.TransformBy(matrix);

            if (entity is MLeader mleader)
            {
                AdjustMLeaderScale(mleader, scaleFactor);
            }
            else if (entity is Hatch hatch)
            {
                EvaluateHatch(hatch);
            }
        }

        return new TransformResult(true, false, dimensionScaleAdjusted);
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

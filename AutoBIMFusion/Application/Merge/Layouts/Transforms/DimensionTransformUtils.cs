using AutoBIMFusion.Infrastructure.Logging;
using System.Globalization;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Изолирует всю специальную обработку AutoCAD Dimension при геометрических
/// трансформациях листа в модель.
/// </summary>
internal static class DimensionTransformUtils
{
    internal static bool TransformDimension(
        Dimension dimension,
        Matrix3d matrix,
        double scaleFactor,
        EntityTransformUtils.DimensionScaleOrder order,
        AILog? log,
        string scenario)
    {
        bool scaleAdjusted = false;

        // Для model-clamp компенсация применяется до TransformBy, а для клонов Paper/Aux - после.
        // Это сохраняет текущий алгоритм и позволяет диагностировать, где именно ломается размер.
        if (order == EntityTransformUtils.DimensionScaleOrder.BeforeTransform)
        {
            LogDimensionDiagnostic(log, scenario, "before-transform", dimension, scaleFactor, order);
            AdjustDimensionScale(dimension, scaleFactor);
            LogDimensionDiagnostic(log, scenario, "after-adjust-before-transform", dimension, scaleFactor, order);
            scaleAdjusted = true;
        }
        else
        {
            LogDimensionDiagnostic(log, scenario, "before-transform", dimension, scaleFactor, order);
        }

        dimension.TransformBy(matrix);
        LogDimensionDiagnostic(log, scenario, "after-transform", dimension, scaleFactor, order);

        if (order == EntityTransformUtils.DimensionScaleOrder.AfterTransform)
        {
            AdjustDimensionScale(dimension, scaleFactor);
            LogDimensionDiagnostic(log, scenario, "after-adjust-after-transform", dimension, scaleFactor, order);
            scaleAdjusted = true;
        }

        dimension.RecomputeDimensionBlock(true);
        LogDimensionDiagnostic(log, scenario, "after-recompute", dimension, scaleFactor, order);

        return scaleAdjusted;
    }

    private static void AdjustDimensionScale(Dimension dimension, double scaleFactor)
    {
        double currentDimscale = dimension.Dimscale == 0.0 ? 1.0 : dimension.Dimscale;
        dimension.Dimscale = currentDimscale * scaleFactor;

        // Dimlfac влияет на отображаемое числовое значение. Сейчас оставляем старую формулу,
        // но логируем ее изменения через [DIM-DIAG], чтобы подтвердить root cause перед фиксом.
        if (scaleFactor > 0.0001)
        {
            dimension.Dimlfac /= scaleFactor;
        }
    }

    private static void LogDimensionDiagnostic(
        AILog? log,
        string scenario,
        string stage,
        Dimension dimension,
        double scaleFactor,
        EntityTransformUtils.DimensionScaleOrder order)
    {
        if (log is null)
        {
            return;
        }

        Extents3d? extents = ExtentsUtils.TryGetExtents(dimension);
        string extentsText = extents.HasValue ? ExtentsUtils.FormatExtents(extents.Value) : "none";
        string bboxHeight = extents.HasValue
            ? FormatDouble(extents.Value.MaxPoint.Y - extents.Value.MinPoint.Y)
            : "n/a";

        // [DIM-DIAG] - временная диагностика для поиска ошибки масштабирования, не финальный фикс.
        log.Debug(
            $"[DIM-DIAG] scenario={scenario}, stage={stage}, " +
            $"handle={dimension.Handle}, type={dimension.GetType().Name}, " +
            $"scaleFactor={FormatDouble(scaleFactor)}, order={order}, " +
            $"measurement={ReadDouble(() => dimension.Measurement)}, " +
            $"text=\"{EscapeDiagnosticText(dimension.DimensionText)}\", " +
            $"dimscale={FormatDouble(dimension.Dimscale)}, dimlfac={FormatDouble(dimension.Dimlfac)}, " +
            $"bboxHeight={bboxHeight}, extents={extentsText}");
    }

    private static string ReadDouble(Func<double> read)
    {
        try
        {
            return FormatDouble(read());
        }
        catch (System.Exception ex)
        {
            return $"unavailable:{ex.GetType().Name}";
        }
    }

    private static string FormatDouble(double value)
    {
        return double.IsFinite(value)
            ? value.ToString("F6", CultureInfo.InvariantCulture)
            : "n/a";
    }

    private static string EscapeDiagnosticText(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}

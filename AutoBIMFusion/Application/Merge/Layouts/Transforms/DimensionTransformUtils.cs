using AutoBIMFusion.Infrastructure.Logging;
using System.Globalization;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Изолирует всю специальную обработку AutoCAD Dimension при геометрических
/// трансформациях листа в модель.
/// </summary>
internal static class DimensionTransformUtils
{
    private const double RatioTolerance = 0.05;
    private static readonly object SummarySync = new();
    private static readonly Dictionary<DiagnosticSummaryKey, DiagnosticSummaryStats> Summary = [];

    internal static void BeginDiagnosticRun()
    {
        lock (SummarySync)
        {
            Summary.Clear();
        }
    }

    internal static void LogDiagnosticSummary(AILog log)
    {
        List<KeyValuePair<DiagnosticSummaryKey, DiagnosticSummaryStats>> snapshot;

        lock (SummarySync)
        {
            snapshot = Summary.ToList();
        }

        foreach (KeyValuePair<DiagnosticSummaryKey, DiagnosticSummaryStats> item in snapshot
                     .OrderBy(i => i.Key.Scenario, StringComparer.Ordinal)
                     .ThenBy(i => i.Key.ScaleFactor)
                     .ThenBy(i => i.Key.Stage, StringComparer.Ordinal))
        {
            DiagnosticSummaryStats stats = item.Value;
            log.Info(
                $"[DIM-DIAG-SUMMARY] scenario={item.Key.Scenario}, scaleFactor={FormatDouble(item.Key.ScaleFactor)}, " +
                $"stage={item.Key.Stage}, count={stats.Count}, measurementPreserved={stats.MeasurementPreserved}, " +
                $"visualSizePreserved={stats.VisualSizePreserved}, bboxScaledLikeTransform={stats.BBoxScaledLikeTransform}, " +
                $"changedAfterPrevious={stats.ChangedAfterPrevious}");
        }
    }

    internal static bool TransformDimension(
        Dimension dimension,
        Matrix3d matrix,
        double scaleFactor,
        EntityTransformUtils.DimensionScaleOrder order,
        AILog? log,
        string scenario)
    {
        bool scaleAdjusted = false;
        DimensionDiagnosticSnapshot baseline = CaptureSnapshot(dimension);
        DimensionDiagnosticSnapshot previous = baseline;

        // Для model-clamp компенсация применяется до TransformBy, а для клонов Paper/Aux - после.
        // Это сохраняет текущий алгоритм и позволяет диагностировать, где именно ломается размер.
        if (order == EntityTransformUtils.DimensionScaleOrder.BeforeTransform)
        {
            LogDimensionDiagnostic(log, scenario, "before-transform", dimension, scaleFactor, order, baseline, previous);
            AdjustDimensionScale(dimension, scaleFactor);
            previous = LogDimensionDiagnostic(log, scenario, "after-adjust-before-transform", dimension, scaleFactor, order, baseline, previous);
            scaleAdjusted = true;
        }
        else
        {
            LogDimensionDiagnostic(log, scenario, "before-transform", dimension, scaleFactor, order, baseline, previous);
        }

        dimension.TransformBy(matrix);
        previous = LogDimensionDiagnostic(log, scenario, "after-transform", dimension, scaleFactor, order, baseline, previous);

        if (order == EntityTransformUtils.DimensionScaleOrder.AfterTransform)
        {
            AdjustDimensionScale(dimension, scaleFactor);
            previous = LogDimensionDiagnostic(log, scenario, "after-adjust-after-transform", dimension, scaleFactor, order, baseline, previous);
            scaleAdjusted = true;
        }

        dimension.RecomputeDimensionBlock(true);
        _ = LogDimensionDiagnostic(log, scenario, "after-recompute", dimension, scaleFactor, order, baseline, previous);

        return scaleAdjusted;
    }

    private static void AdjustDimensionScale(Dimension dimension, double scaleFactor)
    {
        // Dimscale управляет визуальным размером текста/стрелок. TransformBy уже масштабирует
        // размерный блок геометрически, поэтому дополнительное Dimscale *= scaleFactor давало
        // повторное визуальное увеличение после RecomputeDimensionBlock.
        // Dimlfac влияет только на отображаемое числовое значение. Его меняем отдельно и
        // осторожно, чтобы геометрическое масштабирование не исказило текст размера.
        if (scaleFactor > 0.0001)
        {
            dimension.Dimlfac /= scaleFactor;
        }
    }

    private static DimensionDiagnosticSnapshot LogDimensionDiagnostic(
        AILog? log,
        string scenario,
        string stage,
        Dimension dimension,
        double scaleFactor,
        EntityTransformUtils.DimensionScaleOrder order,
        DimensionDiagnosticSnapshot baseline,
        DimensionDiagnosticSnapshot previous)
    {
        DimensionDiagnosticSnapshot current = CaptureSnapshot(dimension);

        if (log is null)
        {
            return current;
        }

        string extentsText = current.Extents.HasValue ? ExtentsUtils.FormatExtents(current.Extents.Value) : "none";
        string bboxHeight = FormatNullableDouble(current.BBoxHeight);
        string bboxHeightRatio = FormatRatio(current.BBoxHeight, baseline.BBoxHeight);
        string visualTextHeightRatio = FormatRatio(current.VisualTextHeight, baseline.VisualTextHeight);
        string visualArrowSizeRatio = FormatRatio(current.VisualArrowSize, baseline.VisualArrowSize);
        string measurementRatio = FormatRatio(current.Measurement, baseline.Measurement);
        string measurementApplicable = current.MeasurementApplicable ? "true" : "false";
        string changedAfterPrevious = HasChanged(current, previous) ? "true" : "false";
        DimensionStyleDiagnosticUtils.DimensionStyleSnapshot? styleSnapshot = DimensionStyleDiagnosticUtils.TryReadStyleSnapshot(dimension);
        string entityDiffersFromStyle = DimensionStyleDiagnosticUtils.EntityDiffersFromStyle(dimension, styleSnapshot)
            ? "true"
            : "false";

        RecordSummary(scenario, stage, scaleFactor, current, baseline, previous);

        // [DIM-DIAG] - временная диагностика для поиска ошибки масштабирования, не финальный фикс.
        log.Debug(
            $"[DIM-DIAG] scenario={scenario}, stage={stage}, " +
            $"handle={dimension.Handle}, type={dimension.GetType().Name}, " +
            $"scaleFactor={FormatDouble(scaleFactor)}, order={order}, " +
            $"measurement={FormatNullableDouble(current.Measurement)}, measurementRatio={measurementRatio}, " +
            $"measurementApplicable={measurementApplicable}, " +
            $"text=\"{EscapeDiagnosticText(dimension.DimensionText)}\", " +
            $"dimscale={FormatDouble(dimension.Dimscale)}, dimlfac={FormatDouble(dimension.Dimlfac)}, " +
            $"visualTextHeight={FormatNullableDouble(current.VisualTextHeight)}, visualTextHeightRatio={visualTextHeightRatio}, " +
            $"visualArrowSize={FormatNullableDouble(current.VisualArrowSize)}, visualArrowSizeRatio={visualArrowSizeRatio}, " +
            $"bboxHeight={bboxHeight}, bboxHeightRatio={bboxHeightRatio}, changedAfterPrevious={changedAfterPrevious}, " +
            $"dimensionStyleName=\"{EscapeDiagnosticText(ReadText(() => dimension.DimensionStyleName))}\", " +
            $"dimensionStyleHandle={ReadText(() => dimension.DimensionStyle.Handle.ToString())}, " +
            $"entityDiffersFromStyle={entityDiffersFromStyle}, " +
            $"entityDimtxt={ReadDouble(() => dimension.Dimtxt)}, entityDimasz={ReadDouble(() => dimension.Dimasz)}, " +
            $"entityDimscale={FormatDouble(dimension.Dimscale)}, entityDimlfac={FormatDouble(dimension.Dimlfac)}, " +
            $"{DimensionStyleDiagnosticUtils.FormatStyleSnapshot(styleSnapshot)}, " +
            $"entityDimexo={ReadDouble(() => dimension.Dimexo)}, entityDimexe={ReadDouble(() => dimension.Dimexe)}, " +
            $"entityDimtad={ReadText(() => dimension.Dimtad.ToString(CultureInfo.InvariantCulture))}, " +
            $"entityDimjust={ReadText(() => dimension.Dimjust.ToString(CultureInfo.InvariantCulture))}, " +
            $"entityDimclrd={ReadText(() => FormatColor(dimension.Dimclrd))}, " +
            $"entityDimclre={ReadText(() => FormatColor(dimension.Dimclre))}, " +
            $"entityDimclrt={ReadText(() => FormatColor(dimension.Dimclrt))}, " +
            $"textPosition={ReadText(() => ExtentsUtils.FormatPoint(dimension.TextPosition))}, " +
            $"{FormatDimensionGeometry(dimension)}, extents={extentsText}");

        return current;
    }

    private static DimensionDiagnosticSnapshot CaptureSnapshot(Dimension dimension)
    {
        Extents3d? extents = ExtentsUtils.TryGetExtents(dimension);
        double? bboxHeight = extents.HasValue
            ? extents.Value.MaxPoint.Y - extents.Value.MinPoint.Y
            : null;

        double dimscale = dimension.Dimscale == 0.0 ? 1.0 : dimension.Dimscale;

        return new DimensionDiagnosticSnapshot(
            ReadNullableDouble(() => dimension.Measurement),
            IsMeasurementApplicable(dimension.DimensionText),
            ReadNullableDouble(() => dimension.Dimtxt * dimscale),
            ReadNullableDouble(() => dimension.Dimasz * dimscale),
            bboxHeight,
            extents);
    }

    private static void RecordSummary(
        string scenario,
        string stage,
        double scaleFactor,
        DimensionDiagnosticSnapshot current,
        DimensionDiagnosticSnapshot baseline,
        DimensionDiagnosticSnapshot previous)
    {
        DiagnosticSummaryKey key = new(scenario, RoundScaleFactor(scaleFactor), stage);

        lock (SummarySync)
        {
            if (!Summary.TryGetValue(key, out DiagnosticSummaryStats? stats))
            {
                stats = new DiagnosticSummaryStats();
                Summary[key] = stats;
            }

            stats.Count++;

            bool visualSizePreserved =
                IsRatioNear(current.VisualTextHeight, baseline.VisualTextHeight, 1.0)
                && IsRatioNear(current.VisualArrowSize, baseline.VisualArrowSize, 1.0);

            if (IsRatioNear(current.Measurement, baseline.Measurement, 1.0)
                || (!baseline.MeasurementApplicable && visualSizePreserved))
            {
                stats.MeasurementPreserved++;
            }

            if (visualSizePreserved)
            {
                stats.VisualSizePreserved++;
            }

            if (scaleFactor > 1.0 + RatioTolerance && IsRatioNear(current.BBoxHeight, baseline.BBoxHeight, scaleFactor))
            {
                stats.BBoxScaledLikeTransform++;
            }

            if (HasChanged(current, previous))
            {
                stats.ChangedAfterPrevious++;
            }
        }
    }

    private static bool IsMeasurementApplicable(string? dimensionText)
    {
        // Empty text means AutoCAD displays the measured value. Overrides without
        // the "<>" placeholder replace the value, so raw Measurement drift is not user-visible.
        return string.IsNullOrEmpty(dimensionText)
            || dimensionText.Contains("<>", StringComparison.Ordinal);
    }

    private static string FormatDimensionGeometry(Dimension dimension)
    {
        return dimension switch
        {
            RotatedDimension rotated =>
                $"dimLinePoint={ExtentsUtils.FormatPoint(rotated.DimLinePoint)}, " +
                $"xLine1Point={ExtentsUtils.FormatPoint(rotated.XLine1Point)}, " +
                $"xLine2Point={ExtentsUtils.FormatPoint(rotated.XLine2Point)}",
            AlignedDimension aligned =>
                $"dimLinePoint={ExtentsUtils.FormatPoint(aligned.DimLinePoint)}, " +
                $"xLine1Point={ExtentsUtils.FormatPoint(aligned.XLine1Point)}, " +
                $"xLine2Point={ExtentsUtils.FormatPoint(aligned.XLine2Point)}",
            _ => "dimLinePoint=n/a, xLine1Point=n/a, xLine2Point=n/a"
        };
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

    private static double? ReadNullableDouble(Func<double> read)
    {
        try
        {
            double value = read();
            return double.IsFinite(value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadText(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (System.Exception ex)
        {
            return $"unavailable:{ex.GetType().Name}";
        }
    }

    private static string FormatColor(Autodesk.AutoCAD.Colors.Color color)
    {
        return $"{color.ColorMethod}:{color.ColorIndex}";
    }

    private static string FormatDouble(double value)
    {
        return double.IsFinite(value)
            ? value.ToString("F6", CultureInfo.InvariantCulture)
            : "n/a";
    }

    private static string FormatNullableDouble(double? value)
    {
        return value.HasValue ? FormatDouble(value.Value) : "n/a";
    }

    private static string FormatRatio(double? current, double? baseline)
    {
        if (!current.HasValue || !baseline.HasValue || Abs(baseline.Value) < 1e-9)
        {
            return "n/a";
        }

        return FormatDouble(current.Value / baseline.Value);
    }

    private static bool IsRatioNear(double? current, double? baseline, double expected)
    {
        if (!current.HasValue || !baseline.HasValue || Abs(baseline.Value) < 1e-9)
        {
            return false;
        }

        return Abs((current.Value / baseline.Value) - expected) <= RatioTolerance;
    }

    private static bool HasChanged(DimensionDiagnosticSnapshot current, DimensionDiagnosticSnapshot previous)
    {
        return !IsSame(current.Measurement, previous.Measurement) || !IsSame(current.BBoxHeight, previous.BBoxHeight);
    }

    private static bool IsSame(double? left, double? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return left.HasValue == right.HasValue;
        }

        return Abs(left.Value - right.Value) <= Max(1e-6, Abs(right.Value) * 1e-6);
    }

    private static double RoundScaleFactor(double scaleFactor)
    {
        return double.IsFinite(scaleFactor) ? Round(scaleFactor, 6) : scaleFactor;
    }

    private static string EscapeDiagnosticText(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private sealed record DiagnosticSummaryKey(string Scenario, double ScaleFactor, string Stage);

    private sealed class DiagnosticSummaryStats
    {
        internal int Count { get; set; }
        internal int MeasurementPreserved { get; set; }
        internal int VisualSizePreserved { get; set; }
        internal int BBoxScaledLikeTransform { get; set; }
        internal int ChangedAfterPrevious { get; set; }
    }

    private readonly record struct DimensionDiagnosticSnapshot(
        double? Measurement,
        bool MeasurementApplicable,
        double? VisualTextHeight,
        double? VisualArrowSize,
        double? BBoxHeight,
        Extents3d? Extents);
}

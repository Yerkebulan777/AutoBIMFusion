using Serilog.Core;

namespace AutoBIMFusion.Merge.Combine.Layouts;

internal readonly record struct ViewportScaleNormalization(
    double WorkingCustomScale,
    double GeometryScale,
    double TargetVisualScale,
    double LinearScaleMultiplier);

/// <summary>
///     Нормализует масштаб главного VP к рабочему 1:100.
///     geometryScale = customScale / 0.01, Dimlfac = 1 / geometryScale, Dimscale = 100.
/// </summary>
internal static class ViewportScaleNormalizer
{
    internal const double WorkingScaleMultiplier = 100.0;

    internal static ViewportScaleNormalization Normalize(double customScale, Logger? log = null)
    {
        if (customScale <= 0.0 || double.IsNaN(customScale) || double.IsInfinity(customScale))
            throw new ArgumentOutOfRangeException(nameof(customScale), customScale,
                "Масштаб viewport должен быть положительным и конечным.");

        var workingCustomScale = 1.0 / WorkingScaleMultiplier;
        var geometryScale = customScale / workingCustomScale;
        var linearScaleMultiplier = 1.0 / geometryScale;

        if (log is not null)
        {
            log.Debug(
                "[LINEAR-SCALE] customScale={CustomScale:G10}, workingCustomScale={WorkingCustomScale:G10}," +
                " geometryScale={GeometryScale:G10}, linearScaleMultiplier={LinearScaleMultiplier:G10}," +
                " targetVisualScale={TargetVisualScale}",
                customScale, workingCustomScale, geometryScale, linearScaleMultiplier, (int)WorkingScaleMultiplier);

            if (linearScaleMultiplier is < 0.0001 or > 10000.0)
                log.Warning(
                    "[LINEAR-SCALE] подозрительное значение Dimlfac={LinearScaleMultiplier:G10}:" +
                    " customScale={CustomScale:G10} вне ожидаемого диапазона",
                    linearScaleMultiplier, customScale);
        }

        return new ViewportScaleNormalization(
            workingCustomScale,
            geometryScale,
            WorkingScaleMultiplier,
            linearScaleMultiplier);
    }
}

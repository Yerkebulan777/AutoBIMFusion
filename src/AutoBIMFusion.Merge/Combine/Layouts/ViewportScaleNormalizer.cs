using Serilog.Core;
using Serilog.Events;

namespace AutoBIMFusion.Merge.Combine.Layouts;

internal readonly record struct ViewportScaleNormalization(
    double WorkingCustomScale,
    double GeometryScale,
    double TargetVisualScale,
    double LinearScaleMultiplier);

internal static class ViewportScaleNormalizer
{
    internal const double WorkingScaleMultiplier = 100.0;

    internal static ViewportScaleNormalization Normalize(double customScale, Logger? log = null)
    {
        if (customScale <= 0.0 || double.IsNaN(customScale) || double.IsInfinity(customScale))
            throw new ArgumentOutOfRangeException(nameof(customScale), customScale,
                "Масштаб viewport должен быть положительным и конечным.");

        // workingCustomScale — рабочий масштаб вьюпорта после нормализации (всегда 1/100)
        var workingCustomScale = 1.0 / WorkingScaleMultiplier;

        // geometryScale — во сколько раз растягивается геометрия Model Space,
        // чтобы перейти от оригинального масштаба к рабочему 1/100
        var geometryScale = customScale / workingCustomScale;

        // linearScaleMultiplier → Dimlfac: компенсирует растяжение геометрии,
        // чтобы размеры показывали реальные значения, а не масштабированные
        var linearScaleMultiplier = 1.0 / geometryScale;

        if (log is not null && log.IsEnabled(LogEventLevel.Debug))
        {
            log.Debug(
                "[LINEAR-SCALE] вход: customScale={CustomScale:G10}",
                customScale);

            log.Debug(
                "[LINEAR-SCALE] шаг1: workingCustomScale = 1 / {WorkingScaleMultiplier} = {WorkingCustomScale:G10}",
                (int)WorkingScaleMultiplier, workingCustomScale);

            log.Debug(
                "[LINEAR-SCALE] шаг2: geometryScale = customScale / workingCustomScale" +
                " = {CustomScale:G10} / {WorkingCustomScale:G10} = {GeometryScale:G10}",
                customScale, workingCustomScale, geometryScale);

            log.Debug(
                "[LINEAR-SCALE] шаг3: linearScaleMultiplier (Dimlfac) = 1 / geometryScale" +
                " = 1 / {GeometryScale:G10} = {LinearScaleMultiplier:G10}",
                geometryScale, linearScaleMultiplier);

            log.Debug(
                "[LINEAR-SCALE] шаг4: targetVisualScale (Dimscale) = {TargetVisualScale} (константа)",
                (int)WorkingScaleMultiplier);

            log.Debug(
                "[LINEAR-SCALE] итог: workingCustomScale={WorkingCustomScale:G10}," +
                " geometryScale={GeometryScale:G10}," +
                " linearScaleMultiplier={LinearScaleMultiplier:G10}," +
                " targetVisualScale={TargetVisualScale}",
                workingCustomScale, geometryScale, linearScaleMultiplier, (int)WorkingScaleMultiplier);

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

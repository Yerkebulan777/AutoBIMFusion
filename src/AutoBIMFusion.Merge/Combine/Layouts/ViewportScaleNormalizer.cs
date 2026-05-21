using Serilog.Core;
using Serilog.Events;

namespace AutoBIMFusion.Merge.Combine.Layouts;

internal readonly record struct ViewportScaleNormalization(
    double WorkingCustomScale,
    double GeometryScale,
    double TargetVisualScale,
    double LinearScaleMultiplier);

/// <summary>
///     Вычисляет параметры нормализации масштаба для главного Viewport'а листа.
/// </summary>
/// <remarks>
///     <para>
///         <b>Цель нормализации:</b><br/>
///         Все листы после merge работают при едином рабочем масштабе 1:100
///         (<c>workingCustomScale = 1/100 = 0.01</c>). Нормализация приводит
///         исходную геометрию к этому масштабу и корректирует <c>Dimlfac</c>,
///         чтобы размеры по-прежнему показывали реальные значения.
///     </para>
///     <para>
///         <b>Полная цепочка масштабирования:</b>
///     </para>
///     <list type="table">
///         <listheader><term>Символ</term><description>Смысл</description></listheader>
///         <item>
///             <term><c>customScale</c></term>
///             <description>Оригинальный масштаб главного VP (напр. 0.04 = 1:25)</description>
///         </item>
///         <item>
///             <term><c>workingCustomScale</c></term>
///             <description>Рабочий масштаб = 1/100 = 0.01 (константа)</description>
///         </item>
///         <item>
///             <term><c>geometryScale</c></term>
///             <description>
///                 Коэффициент масштабирования геометрии Model Space:<br/>
///                 <c>geometryScale = customScale / workingCustomScale</c><br/>
///                 Пример: 0.04 / 0.01 = 4 — все объекты масштабируются ×4,
///                 чтобы через viewport 1:100 выглядеть так же, как через 1:25.
///             </description>
///         </item>
///         <item>
///             <term><c>linearScaleMultiplier</c> (Dimlfac)</term>
///             <description>
///                 Обратный коэффициент для размеров:<br/>
///                 <c>Dimlfac = 1 / geometryScale = workingCustomScale / customScale</c><br/>
///                 Компенсирует растяжение геометрии: если линия 1000 мм стала 4000 мм,
///                 Dimlfac=0.25 отображает 4000×0.25 = 1000 — исходное значение.
///             </description>
///         </item>
///         <item>
///             <term><c>targetVisualScale</c> (Dimscale)</term>
///             <description>
///                 = WorkingScaleMultiplier = 100 (константа).<br/>
///                 Масштабирует визуальные атрибуты размеров (стрелки, текст) под рабочий
///                 масштаб 1:100, не влияет на отображаемое числовое значение.
///             </description>
///         </item>
///     </list>
///     <para>
///         <b>Для вспомогательных VP</b> (<c>auxScale ≠ mainScale</c>) итоговый масштаб
///         контента = <c>auxScale / workingCustomScale</c>. Корректный <c>Dimlfac</c>
///         для такого контента = <c>workingCustomScale / auxScale</c>. Поскольку
///         <see cref="Normalize"/> принимает только масштаб главного VP, aux-VP при
///         отличном масштабе получают тот же <c>Dimlfac</c> — это known limitation.
///     </para>
/// </remarks>
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

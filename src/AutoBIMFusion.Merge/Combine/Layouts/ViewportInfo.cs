namespace AutoBIMFusion.Merge.Combine.Layouts;

/// <summary>
///     Снимок Viewport'а с листа: положение на бумаге, параметры вида модели, масштаб.
///     Главный VP выбирается через <see cref="PickMainViewport"/> по модальному масштабу.
/// </summary>
internal sealed record ViewportInfo(
    ObjectId VpId,
    int Number,
    Point3d CenterPaper,
    double WidthPaper,
    double HeightPaper,
    Point3d ViewCenter,
    double ViewHeight,
    double ViewTwist,
    double CustomScale,
    Extents3d ModelWindow)
{
    public double PaperArea => WidthPaper * HeightPaper;

    /// <summary>
    ///     Выбирает «главный» Viewport листа — тот, чей масштаб используется
    ///     для нормализации геометрии и расчёта <c>Dimlfac</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Алгоритм (2 шага):</b>
    ///     </para>
    ///     <list type="number">
    ///         <item>
    ///             <description>
    ///                 Группируем VP по <c>CustomScale</c> (с округлением до 4 знаков, чтобы
    ///                 не разбивать одинаковые масштабы из-за плавающей точки).
    ///                 Выбираем группу с наибольшим числом VP («модальный масштаб»).
    ///                 Тай-брейк по суммарной площади группы — если два масштаба встречаются
    ///                 одинаково часто, берём тот, чьи VP занимают больше бумаги.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 Внутри выбранной группы берём VP с максимальной площадью бумаги
    ///                 (<c>PaperArea = Width × Height</c>) — это основной чертёж листа.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <b>Пример:</b> лист с VP при 1:25 × 3 шт. и VP при 1:100 × 1 шт. →
    ///         модальный масштаб = 0.04 → выбирается самый большой из 1:25-VP.
    ///         Результат: <c>geometryScale = 4</c>, <c>Dimlfac = 0.25</c> — корректно
    ///         компенсирует масштабирование геометрии основных разрезов.
    ///     </para>
    ///     <para>
    ///         <b>Почему не PaperArea / CustomScale (старая формула):</b><br/>
    ///         Деление на маленький CustomScale (напр. 0.01 для 1:100) давало обзорному VP
    ///         в 4 раза больший score, чем рабочим VP при 1:25.
    ///         Это приводило к выбору обзорного VP как главного →
    ///         <c>geometryScale = 1</c>, <c>Dimlfac = 1.0</c>, тогда как контент
    ///         вспомогательных VP при 1:25 масштабировался ×4 через <c>auxToMain</c>
    ///         (net scale = <c>auxScale / mainScale = 0.04 / 0.01 = 4</c>).
    ///         Итог: размеры на основных разрезах показывали значения в 4 раза больше реальных.
    ///     </para>
    ///     <para>
    ///         <b>Ограничение:</b> если на листе VP разных масштабов встречаются с одинаковой
    ///         частотой (например 1:25 × 1 + 1:100 × 1), тай-брейк идёт по суммарной PaperArea
    ///         группы. В этом случае VP нестандартного масштаба (auxiliary) может получить
    ///         неверный <c>Dimlfac</c> — это known limitation, требующий per-VP tracking.
    ///     </para>
    /// </remarks>
    internal static ViewportInfo PickMainViewport(IReadOnlyList<ViewportInfo> vps)
    {
        ArgumentNullException.ThrowIfNull(vps);

        if (vps.Count == 0) throw new ArgumentException("Список vpt'ов пуст.", nameof(vps));
        if (vps.Count == 1) return vps[0];

        var modalGroup = vps
            .GroupBy(v => Math.Round(v.CustomScale, 4))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(v => v.PaperArea))
            .First();

        return modalGroup.MaxBy(v => v.PaperArea)!;
    }
}

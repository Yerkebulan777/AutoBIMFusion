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

    internal static ViewportInfo PickMainViewport(IReadOnlyList<ViewportInfo> vps)
    {
        ArgumentNullException.ThrowIfNull(vps);

        if (vps.Count == 0) throw new ArgumentException("Список vpt'ов пуст.", nameof(vps));
        if (vps.Count == 1) return vps[0];

        // Выбираем главный VP по модальному масштабу (наиболее частый CustomScale),
        // затем по максимальной площади бумаги внутри группы.
        //
        // Старая формула PaperArea/CustomScale была неверной: она давала слишком высокий
        // приоритет overview-VP (например 1:100), из-за чего linearScaleMultiplier
        // получался =1 вместо 0.25 для листов с основными разрезами 1:25.
        //
        // Логика: если на листе три VP при 1:25 и один при 1:100,
        // модальный масштаб = 0.04 → выбираем самый большой из 0.04-VP →
        // geometryScale=4, Dimlfac=0.25 (корректно для основных разрезов).
        var modalGroup = vps
            .GroupBy(v => Math.Round(v.CustomScale, 4))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(v => v.PaperArea))
            .First();

        return modalGroup.MaxBy(v => v.PaperArea)!;
    }
}

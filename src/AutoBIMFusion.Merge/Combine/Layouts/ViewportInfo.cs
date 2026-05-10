namespace AutoBIMFusion.Merge.Combine.Layouts;

/// <summary>
/// Снимок Viewport'а с листа: положение на бумаге, параметры вида модели, масштаб.
/// CoverageScore используется для выбора главного vpt.
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
    /// Площадь × (1 / CustomScale). Чем больше, тем больше модели VP «покрывает» на листе.
    /// </summary>
    public double CoverageScore => CustomScale > 0 ? PaperArea / CustomScale : 0;

    internal static ViewportInfo PickMainViewport(IReadOnlyList<ViewportInfo> vps)
    {
        ArgumentNullException.ThrowIfNull(vps);

        if (vps.Count == 0)
        {
            throw new ArgumentException("Список vpt'ов пуст.", nameof(vps));
        }

        ViewportInfo best = vps[0];
        double bestScore = best.CoverageScore;

        for (int i = 1; i < vps.Count; i++)
        {
            double score = vps[i].CoverageScore;

            if (score > bestScore)
            {
                bestScore = score;
                best = vps[i];
            }
        }

        return best;
    }
}


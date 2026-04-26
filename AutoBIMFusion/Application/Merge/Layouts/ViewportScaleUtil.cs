using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Утилиты масштаба главного viewport'а: зажим и применение к Model Space.
/// Вынесено из ViewportLayoutExporter, чтобы ProcessSingleVp и ProcessMultiVp
/// не дублировали одинаковую логику.
///
/// ВАЖНО — почему ProcessMultiVp нельзя зажать main ДО построения aux-матриц:
/// если main зажат (1:1 -> 1:100), то делитель 1/main.CustomScale в BuildMatrix
/// становится в clampRatio раз больше → aux-клоны улетают в clampRatio раз
/// дальше/крупнее оригинальных объектов главного VP.
///
/// Правильный порядок:
///   1) Строить матрицы aux VP по mainOriginal.
///   2) После клонирования aux масштабировать ВЕСЬ Model Space на clampRatio
///      вокруг mainOriginal.ViewCenter (ApplyClampToModelSpace).
///   3) Переносить paper через BuildPaperToMainMatrix(mainClamped).
/// </summary>
internal static class ViewportScaleUtil
{
    /// <summary>
    /// Если 1/CustomScale меньше этого порога — VP зажимается до 1:MaxScaleMultiplier.
    /// Предотвращает огромный Model Space при очень крупных масштабах (1:1, 1:5 и т.п.).
    /// </summary>
    public const double MaxScaleMultiplier = 100.0;

    /// <summary>
    /// Зажимает VP до 1:MaxScaleMultiplier, если исходный масштаб крупнее порога.
    /// </summary>
    public static LayoutViewportInfo ClampMainVpScale(LayoutViewportInfo vp, OperationLogger log)
    {
        double multiplier = 1.0 / vp.CustomScale;

        if (multiplier < MaxScaleMultiplier)
        {
            log.Info($"VP #{vp.Number}: масштаб 1:{multiplier:F0} зажат до 1:{MaxScaleMultiplier:F0}");
            return vp with { CustomScale = 1.0 / MaxScaleMultiplier };
        }

        log.Info($"VP #{vp.Number}: масштаб 1:{multiplier:F0} (без зажима, customScale={vp.CustomScale:F6})");
        return vp;
    }

    /// <summary>
    /// clampRatio = originalScale / clampedScale. При отсутствии зажима = 1.0.
    /// </summary>
    public static double ClampRatio(LayoutViewportInfo original, LayoutViewportInfo clamped)
        => original.CustomScale / clamped.CustomScale;

    /// <summary>
    /// Масштабирует весь Model Space на clampRatio вокруг ViewCenter главного VP.
    /// Выравнивает оригинальные объекты + aux-клоны под масштаб paper-содержимого,
    /// которое придёт через mainClamped матрицу.
    /// Если clampRatio ≤ 1 — ничего не делает.
    /// </summary>
    public static void ApplyClampToModelSpace(
        Database db,
        LayoutViewportInfo mainOriginal,
        double clampRatio,
        OperationLogger log)
    {
        if (clampRatio <= 1.0 + 1e-9)
        {
            log.Debug(
                $"VP main#{mainOriginal.Number}: масштабирование Model Space не требуется " +
                $"(clampRatio={clampRatio:F6})");
            return;
        }

        log.Info(
            $"VP main#{mainOriginal.Number}: масштабируем Model Space, " +
            $"ratio={clampRatio:F6}, center={GeometryUtils.FormatPoint(mainOriginal.ViewCenter)}");

        Matrix3d scaleMatrix = Matrix3d.Scaling(clampRatio, mainOriginal.ViewCenter);
        ViewportTransformer.ScaleModelSpaceObjects(db, scaleMatrix, clampRatio, log);
    }
}

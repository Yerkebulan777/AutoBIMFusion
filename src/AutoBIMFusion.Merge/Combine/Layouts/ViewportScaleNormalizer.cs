namespace AutoBIMFusion.Merge.Combine.Layouts;

internal readonly record struct ViewportScaleNormalization(
    double WorkingCustomScale,
    double GeometryScale,
    double TargetVisualScale,
    double LinearScaleMultiplier);

internal static class ViewportScaleNormalizer
{
    internal const double WorkingScaleMultiplier = 100.0;

    internal static ViewportScaleNormalization Normalize(double customScale)
    {
        if (customScale <= 0.0 || double.IsNaN(customScale) || double.IsInfinity(customScale))
        {
            throw new ArgumentOutOfRangeException(nameof(customScale), customScale,
                "Масштаб viewport должен быть положительным и конечным.");
        }

        double workingCustomScale = 1.0 / WorkingScaleMultiplier;
        double geometryScale = customScale / workingCustomScale;
        double linearScaleMultiplier = 1.0 / geometryScale;

        return new ViewportScaleNormalization(
            workingCustomScale,
            geometryScale,
            WorkingScaleMultiplier,
            linearScaleMultiplier);
    }
}

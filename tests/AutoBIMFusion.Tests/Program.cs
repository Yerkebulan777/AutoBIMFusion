using AutoBIMFusion.Merge.Layouts;

VerifyViewportScale(200.0, expectedGeometryScale: 0.5, expectedLinearScale: 2.0);
VerifyViewportScale(100.0, expectedGeometryScale: 1.0, expectedLinearScale: 1.0);
VerifyViewportScale(50.0, expectedGeometryScale: 2.0, expectedLinearScale: 0.5);

Console.WriteLine("Scale normalization tests passed.");

static void VerifyViewportScale(double viewportScaleDenominator, double expectedGeometryScale, double expectedLinearScale)
{
    ViewportScaleNormalization actual = ViewportScaleNormalizer.Normalize(1.0 / viewportScaleDenominator);

    AssertClose(1.0 / 100.0, actual.WorkingCustomScale, $"WorkingCustomScale 1:{viewportScaleDenominator:0}");
    AssertClose(expectedGeometryScale, actual.GeometryScale, $"GeometryScale 1:{viewportScaleDenominator:0}");
    AssertClose(100.0, actual.TargetVisualScale, $"TargetVisualScale 1:{viewportScaleDenominator:0}");
    AssertClose(expectedLinearScale, actual.LinearScaleMultiplier, $"LinearScaleMultiplier 1:{viewportScaleDenominator:0}");
    AssertClose(250.0, 2.5 * actual.TargetVisualScale, $"Arrow size 1:{viewportScaleDenominator:0}");
}

static void AssertClose(double expected, double actual, string name)
{
    if (Math.Abs(expected - actual) > 1e-9)
    {
        throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }
}


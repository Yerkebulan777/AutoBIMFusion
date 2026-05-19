using AutoBIMFusion.Merge.Combine.Layouts;

return TestRunner.Run(
    ("Small object fully inside aux window is selected", (Action)(() =>
    {
        var window = Bounds(0, 0, 100, 100);
        var mainWindow = Bounds(-200, -200, 200, 200);
        var smallInside = Bounds(10, 10, 20, 20);

        var decision = ViewportTransformer.ClassifyModelEntityForViewport(window, mainWindow, smallInside);

        TestRunner.AssertEqual(ViewportTransformer.ModelEntitySelection.Selected, decision);
    })),
    ("Small partial aux object is skipped", (Action)(() =>
    {
        var window = Bounds(0, 0, 100, 100);
        var mainWindow = Bounds(-200, -200, 200, 200);
        var smallPartial = Bounds(95, 10, 105, 20);

        var decision = ViewportTransformer.ClassifyModelEntityForViewport(window, mainWindow, smallPartial);

        TestRunner.AssertEqual(ViewportTransformer.ModelEntitySelection.SmallPartialOutsideWindow, decision);
    })),
    ("Large partial aux object keeps legacy intersection behavior", (Action)(() =>
    {
        var window = Bounds(0, 0, 100, 100);
        var mainWindow = Bounds(-500, -500, 500, 500);
        var largePartial = Bounds(-90, 10, 20, 20);

        var decision = ViewportTransformer.ClassifyModelEntityForViewport(window, mainWindow, largePartial);

        TestRunner.AssertEqual(ViewportTransformer.ModelEntitySelection.Selected, decision);
    })));

static Extents3d Bounds(double minX, double minY, double maxX, double maxY)
{
    return new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
}

internal static class TestRunner
{
    internal static int Run(params (string Name, Action Body)[] tests)
    {
        var failed = 0;

        foreach (var (name, body) in tests)
        {
            try
            {
                body();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
            }
        }

        return failed == 0 ? 0 : 1;
    }

    internal static void AssertEqual<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
        }
    }
}

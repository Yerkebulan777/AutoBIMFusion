using AutoBIMFusion.Merge.Combine;
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
    })),
    ("Resolves Cyrillic raster by name when RBF temp path is gone", () =>
        AssertDiskResolve("ST_01_01_S2_KJ_отсоединено-П-образныйстержень-6938401.png", imageSubdir: null, expectFound: true)),
    ("Resolves raster one subdirectory below the source DWG", () =>
        AssertDiskResolve("sheet.png", "images", true)),
    ("Missing raster next to source DWG stays unresolved", () =>
        AssertDiskResolve("missing.png", imageSubdir: null, expectFound: false)));

static Extents3d Bounds(double minX, double minY, double maxX, double maxY)
{
    return new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
}

static void AssertDiskResolve(string fileName, string? imageSubdir, bool expectFound)
{
    var root = Path.Combine(Path.GetTempPath(), "abf-raster-" + Guid.NewGuid().ToString("N"));
    _ = Directory.CreateDirectory(root);
    try
    {
        var sourceDir = Path.Combine(root, "source");
        var imageDir = imageSubdir is null ? sourceDir : Path.Combine(sourceDir, imageSubdir);
        _ = Directory.CreateDirectory(imageDir);
        var realImage = Path.Combine(imageDir, fileName);
        if (expectFound)
            File.WriteAllBytes(realImage, [0x89, 0x50, 0x4E, 0x47]);

        var found = RasterImagePathFixer.TryResolveOnDisk(
            Path.Combine(Path.GetTempPath(), "RBF-dead", fileName),
            [sourceDir],
            out var resolved);

        TestRunner.AssertEqual(expectFound, found);
        TestRunner.AssertEqual(
            expectFound ? Path.GetFullPath(realImage) : string.Empty,
            expectFound ? Path.GetFullPath(resolved) : resolved);
    }
    finally
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException)
        {
        }
    }
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

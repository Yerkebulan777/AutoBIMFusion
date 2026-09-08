using AutoBIMFusion.Common.Helpers;
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
    {
        WithTempDir(root =>
        {
            var sourceDir = Path.Combine(root, "source");
            _ = Directory.CreateDirectory(sourceDir);
            var fileName = "ST_01_01_S2_KJ_отсоединено-П-образныйстержень-6938401.png";
            var realImage = Path.Combine(sourceDir, fileName);
            File.WriteAllBytes(realImage, [0x89, 0x50, 0x4E, 0x47]);
            var deadRbfPath = Path.Combine(
                Path.GetTempPath(),
                "RBF-293a18f3fcde49c08ad711a06c09f833",
                fileName);

            var found = FileUtil.TryResolveImagePathOnDisk(deadRbfPath, [sourceDir], out var resolved);

            TestRunner.AssertEqual(true, found);
            TestRunner.AssertEqual(Path.GetFullPath(realImage), Path.GetFullPath(resolved));
        });
    }),
    ("Resolves raster one subdirectory below the source DWG", () =>
    {
        WithTempDir(root =>
        {
            var sourceDir = Path.Combine(root, "source");
            var imageDir = Path.Combine(sourceDir, "images");
            _ = Directory.CreateDirectory(imageDir);
            var realImage = Path.Combine(imageDir, "sheet.png");
            File.WriteAllBytes(realImage, [0x89, 0x50, 0x4E, 0x47]);
            var deadRbfPath = Path.Combine(Path.GetTempPath(), "RBF-dead", "sheet.png");

            var found = FileUtil.TryResolveImagePathOnDisk(deadRbfPath, [sourceDir], out var resolved);

            TestRunner.AssertEqual(true, found);
            TestRunner.AssertEqual(Path.GetFullPath(realImage), Path.GetFullPath(resolved));
        });
    }),
    ("Missing raster next to source DWG stays unresolved", () =>
    {
        WithTempDir(root =>
        {
            var sourceDir = Path.Combine(root, "source");
            _ = Directory.CreateDirectory(sourceDir);
            var deadRbfPath = Path.Combine(Path.GetTempPath(), "RBF-dead", "missing.png");

            var found = FileUtil.TryResolveImagePathOnDisk(deadRbfPath, [sourceDir], out var resolved);

            TestRunner.AssertEqual(false, found);
            TestRunner.AssertEqual(string.Empty, resolved);
        });
    }));

static Extents3d Bounds(double minX, double minY, double maxX, double maxY)
{
    return new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
}

static void WithTempDir(Action<string> body)
{
    var root = Path.Combine(Path.GetTempPath(), "abf-raster-" + Guid.NewGuid().ToString("N"));
    _ = Directory.CreateDirectory(root);
    try
    {
        body(root);
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

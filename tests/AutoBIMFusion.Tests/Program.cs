using AutoBIMFusion.Merge.Combine.Layouts;
using AutoBIMFusion.Merge.Diagnostics;
using System.Text.Json;

return TestRunner.Run(
    ("Diagnostics are enabled by debug level or explicit flag", (Action)(() =>
    {
        TestRunner.AssertTrue(MergeDiagnostics.IsEnabledFor("DEBUG", null));
        TestRunner.AssertTrue(MergeDiagnostics.IsEnabledFor("Information", "1"));
        TestRunner.AssertTrue(MergeDiagnostics.IsEnabledFor("Information", "true"));
        TestRunner.AssertFalse(MergeDiagnostics.IsEnabledFor("Information", null));
    })),
    ("Diagnostics cap samples to default limit", (Action)(() =>
    {
        var sample = MergeDiagnostics.TakeSample(Enumerable.Range(1, 25).Select(i => $"H{i}"));

        TestRunner.AssertEqual(20, sample.Count);
        TestRunner.AssertEqual("H1", sample[0]);
        TestRunner.AssertEqual("H20", sample[^1]);
    })),
    ("Diagnostics JSONL event contains required envelope fields", (Action)(() =>
    {
        MergeDiagnosticContext context = MergeDiagnostics.CreateFileContext(@"C:\dwg\a.dwg");

        string json = MergeDiagnostics.BuildEventJson(
            context,
            "file.start",
            new Dictionary<string, object?>
            {
                ["fileName"] = "a.dwg",
                ["entityCount"] = 3
            });

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        TestRunner.AssertEqual("file.start", root.GetProperty("eventName").GetString()!);
        TestRunner.AssertEqual(context.MergeFileId, root.GetProperty("mergeFileId").GetString()!);
        TestRunner.AssertEqual("a.dwg", root.GetProperty("fileName").GetString()!);
        TestRunner.AssertEqual(3, root.GetProperty("entityCount").GetInt32());
        TestRunner.AssertTrue(root.TryGetProperty("timestamp", out _));
    })),
    ("Diagnostics log line is usable without JSONL file", (Action)(() =>
    {
        MergeDiagnosticContext context = MergeDiagnostics.CreateFileContext(@"C:\dwg\a.dwg");

        string line = MergeDiagnostics.BuildEventLogLine(
            context,
            "scale.normalized",
            new Dictionary<string, object?>
            {
                ["geometryScale"] = 100.0
            });

        TestRunner.AssertContains("[MERGE_DIAG]", line);
        TestRunner.AssertContains("scale.normalized", line);
        TestRunner.AssertContains("\"geometryScale\":100", line);
    })),
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

    internal static void AssertTrue(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    internal static void AssertFalse(bool condition)
    {
        if (condition)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }

    internal static void AssertContains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
        }
    }
}

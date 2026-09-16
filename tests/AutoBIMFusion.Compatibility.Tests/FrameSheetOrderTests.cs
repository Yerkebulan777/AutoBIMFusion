using AutoBIMFusion.QuickPdf.Frames;

internal static class FrameSheetOrderTests
{
    internal static void Run()
    {
        AssertOrder("empty list stays empty", []);
        AssertOrder("single frame unchanged", [(10, 20)], Sheet(10, 20));

        AssertOrder(
            "2x2 grid is left-to-right then top-to-bottom",
            [(0, 100), (100, 100), (0, 0), (100, 0)],
            Sheet(0, 0),
            Sheet(100, 0),
            Sheet(0, 100),
            Sheet(100, 100));

        AssertOrder(
            "shuffled row prints left-to-right",
            [(0, 0), (100, 0), (200, 0)],
            Sheet(200, 0),
            Sheet(0, 0),
            Sheet(100, 0));

        AssertOrder(
            "right sheet slightly higher still prints left first",
            [(0, 100), (100, 100)],
            Sheet(0, 100),
            Sheet(100, 100, height: 52));

        AssertOrder(
            "left sheet slightly higher still prints left first",
            [(0, 100), (100, 100)],
            Sheet(0, 100, height: 52),
            Sheet(100, 100));

        AssertOrder(
            "column prints top then bottom",
            [(0, 100), (0, 0)],
            Sheet(0, 0),
            Sheet(0, 100));

        AssertOrder(
            "wide right sheet does not print before narrow left",
            [(0, 0), (50, 0)],
            Sheet(50, 0, width: 150),
            Sheet(0, 0, width: 40));

        DetectedFrame[] grid3x2 =
        [
            Sheet(200, 0),
            Sheet(0, 50),
            Sheet(100, 0),
            Sheet(200, 50),
            Sheet(0, 0),
            Sheet(100, 50)
        ];
        (double X, double Y)[] grid3x2Order =
        [
            (0, 50), (100, 50), (200, 50),
            (0, 0), (100, 0), (200, 0)
        ];
        AssertOrder("3x2 grid shuffled is left-to-right then top-to-bottom", grid3x2Order, grid3x2);
        AssertOrder(
            "3x2 grid reverse input keeps the same print order",
            grid3x2Order,
            [.. grid3x2.Reverse()]);
    }

    private static DetectedFrame Sheet(double minX, double minY, double width = 50, double height = 50)
    {
        return new DetectedFrame(minX, minY, minX + width, minY + height, []);
    }

    private static void AssertOrder(string name, (double X, double Y)[] expected, params DetectedFrame[] frames)
    {
        (double X, double Y)[] actual =
        [
            .. FrameSheetOrder.Sort(frames).Select(frame => (frame.MinX, frame.MinY))
        ];
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(name);
        }
    }
}

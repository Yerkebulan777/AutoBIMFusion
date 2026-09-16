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
            Sheet(0, 0), Sheet(100, 0), Sheet(0, 100), Sheet(100, 100));

        AssertOrder(
            "shuffled row prints left-to-right",
            [(0, 0), (100, 0), (200, 0)],
            Sheet(100, 0), Sheet(0, 0), Sheet(200, 0));

        AssertOrder(
            "column prints top then bottom",
            [(0, 100), (0, 0)],
            Sheet(0, 0), Sheet(0, 100));

        AssertOrder(
            "same center Y forms a row despite different top edges",
            [(0, 100), (100, 100)],
            Sheet(100, 100, height: 200), Sheet(0, 100, height: 20));

        AssertOrder(
            "center X takes precedence over left and right edges",
            [(-100, 0), (0, 0), (100, 0)],
            Sheet(0, 0, width: 50), Sheet(100, 0, width: 500), Sheet(-100, 0, width: 400));

        AssertOrder(
            "center Y takes precedence over top edges",
            [(100, 100), (0, 0)],
            Sheet(0, 0, height: 400), Sheet(100, 100, height: 20));

        AssertOrder(
            "distinct center heights are not merged by frame size",
            [(100, 101), (0, 100)],
            Sheet(0, 100, height: 1000), Sheet(100, 101, height: 1000));

        AssertOrder(
            "negative coordinates use the same directions",
            [(-200, -50), (-100, -50), (-200, -150), (-100, -150)],
            Sheet(-200, -150), Sheet(-100, -50), Sheet(-100, -150), Sheet(-200, -50));

        DetectedFrame[] grid3x2 =
        [
            Sheet(200, 0), Sheet(0, 50), Sheet(100, 0),
            Sheet(200, 50), Sheet(0, 0), Sheet(100, 50)
        ];
        (double X, double Y)[] grid3x2Order =
        [
            (0, 50), (100, 50), (200, 50),
            (0, 0), (100, 0), (200, 0)
        ];
        AssertOrder("3x2 shuffled grid", grid3x2Order, grid3x2);
        AssertOrder("reversed input keeps the same print order", grid3x2Order, [.. grid3x2.AsEnumerable().Reverse()]);

        DetectedFrame first = new(-25, -25, 25, 25, [42]);
        DetectedFrame second = new(-100, -50, 100, 50, [7]);
        IReadOnlyList<DetectedFrame> sameCenter = FrameSheetOrder.Sort([first, second]);
        if (sameCenter.Count != 2 || !ReferenceEquals(sameCenter[0], first) || !ReferenceEquals(sameCenter[1], second))
        {
            throw new InvalidOperationException("equal centers preserve detected frames and input order");
        }
    }

    private static DetectedFrame Sheet(double centerX, double centerY, double width = 50, double height = 50)
    {
        return new DetectedFrame(
            centerX - width / 2, centerY - height / 2,
            centerX + width / 2, centerY + height / 2, []);
    }

    private static void AssertOrder(string name, (double X, double Y)[] expected, params DetectedFrame[] frames)
    {
        (double X, double Y)[] actual =
        [
            .. FrameSheetOrder.Sort(frames).Select(frame => ((frame.MinX + frame.MaxX) / 2, (frame.MinY + frame.MaxY) / 2))
        ];
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                name + ": expected " + string.Join(", ", expected) + "; actual " + string.Join(", ", actual));
        }
    }
}

using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.Logging;
using AutoBIMFusion.QuickPdf.Frames;
using AutoBIMFusion.QuickPdf.Media;
using AutoBIMFusion.QuickPdf.Naming;
using AutoBIMFusion.QuickPdf.Plotting;
using Serilog;
using System.Globalization;
using System.Text.Json;

Assert(StringUtils.EscapeForQuotedContext("a\\b\"\r\n") == "a\\\\b\\\"\\r\\n", "escaping");
Assert(StringUtils.EscapeForQuotedContext(null) == "", "null escaping");
Assert(StringUtils.Truncate("  first\r\nsecond", "fallback", 3) == "fir", "span truncation");
Assert(StringUtils.Truncate(" \t", "fallback", 3) == "fallback", "blank fallback");
try
{
    StringUtils.Truncate("text", " ", 3);
    throw new InvalidOperationException("Blank fallback accepted.");
}
catch (ArgumentException) { }

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
Assert(NumericUtils.FormatF6(1.25) == "1.250000", "invariant numeric format");
foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
{
    Assert(NumericUtils.FormatF6(value) == "n/a", "non-finite numeric format");
}

string directory = Path.Combine(Path.GetTempPath(), "AutoBIMFusion-compatibility-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
string logFile = Path.Combine(directory, "test.log");
string quickPdfLogFile = Path.Combine(directory, "quickpdf.log");
try
{
    using (var log = new LoggerConfiguration().WriteTo.File(logFile, shared: true).CreateLogger())
    {
        log.Information("Compatibility {Version}", "2019–2027");
    }
    Assert(File.ReadAllText(logFile).Contains("2019–2027"), "Serilog file sink");

    using (var log = LoggerFactory.ApplyAlwaysOnOverrides(
               new LoggerConfiguration().MinimumLevel.Warning())
           .WriteTo.File(quickPdfLogFile, shared: true)
           .CreateLogger())
    {
        log.Information("ordinary information");
        log.ForContext("SourceContext", LoggerFactory.QuickPdfContext)
            .Information("custom paper diagnostics");
    }
    string quickPdfLog = File.ReadAllText(quickPdfLogFile);
    Assert(quickPdfLog.Contains("custom paper diagnostics"), "quickpdf information always logged");
    Assert(!quickPdfLog.Contains("ordinary information"), "ordinary information remains opt-in");

    string json = JsonSerializer.Serialize(new { Success = true, Message = "Проверка" });
    using var document = JsonDocument.Parse(json);
    Assert(document.RootElement.GetProperty("Message").GetString() == "Проверка", "JSON round trip");
}
finally
{
    File.Delete(logFile);
    File.Delete(quickPdfLogFile);
    Directory.Delete(directory);
}

Assert(QuickPdfNaming.SafeName("Plan:A*.dwg") == "Plan_A_", "quickpdf safe name");
Assert(QuickPdfNaming.SafeName(@"C:\tmp\Frame.dwg") == "Frame", "quickpdf path name");
Assert(QuickPdfNaming.SafeName("   ") == "Drawing", "quickpdf blank name");
Assert(QuickPdfNaming.TryReadSheetIndex("Plan_012", "Plan", out int sheet) && sheet == 12, "quickpdf sheet index");
Assert(!QuickPdfNaming.TryReadSheetIndex("Plan_12a", "Plan", out _), "quickpdf non-digit sheet");
PdfDestination unnamed = QuickPdfNaming.Resolve(null, @"C:\tmp\Plan.dwg");
Assert(unnamed.Prefix == "Plan", "quickpdf default prefix from drawing");
Assert(Path.GetFileName(unnamed.Folder) == "Plan", "quickpdf default folder from drawing");
PdfDestination named = QuickPdfNaming.Resolve(@"D:\pdf\Sheets.pdf", "Plan.dwg");
Assert(named.Prefix == "Sheets", "quickpdf selected prefix");
Assert(named.Folder == @"D:\pdf", "quickpdf selected folder");
Assert(QuickPdfNaming.DrawingName("Plan.dwg", @"C:\tmp\other.dwg") == "Plan.dwg", "dwg name preferred");
Assert(QuickPdfNaming.DrawingName(null, @"C:\tmp\other.dwg") == @"C:\tmp\other.dwg", "document name fallback");

DetectedFrame areaFrame = new(0, 0, 10, 10, [1]);
Assert(FrameAreaFilter.Intersecting([areaFrame], new FrameWindow(5, 5, 15, 15)).Count == 1,
    "intersecting window keeps frame");
Assert(FrameAreaFilter.Intersecting([areaFrame], new FrameWindow(-1, -1, 11, 11)).Count == 1,
    "containing window keeps frame");
Assert(FrameAreaFilter.Intersecting([areaFrame], new FrameWindow(20, 20, 30, 30)).Count == 0,
    "disjoint window drops frame");

IReadOnlyList<DetectedFrame> exactA4Frames = AxisAlignedFrameDetector.Find(
[
    new FramePath(1, true,
    [
        new FramePoint(0, 0),
        new FramePoint(21000, 0),
        new FramePoint(21000, 29700),
        new FramePoint(0, 29700)
    ])
], 3);
Assert(exactA4Frames.Count == 1, "exact portrait A4 frame accepted");

IReadOnlyList<DetectedFrame> landscapeFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(2, true,
    [
        new FramePoint(0, 0),
        new FramePoint(29700, 0),
        new FramePoint(29700, 21000),
        new FramePoint(0, 21000)
    ])
], 3);
Assert(landscapeFrames.Count == 1, "exact landscape A4 frame accepted");

IReadOnlyList<DetectedFrame> diagonalFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(3, true,
    [
        new FramePoint(0, 14850),
        new FramePoint(14850, 0),
        new FramePoint(29700, 14850),
        new FramePoint(14850, 29700)
    ])
], 3);
Assert(diagonalFrames.Count == 0, "diagonal quadrilateral rejected");

IReadOnlyList<DetectedFrame> collinearVertexFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(4, true,
    [
        new FramePoint(0, 0),
        new FramePoint(15000, 0),
        new FramePoint(30000, 0),
        new FramePoint(30000, 30000),
        new FramePoint(0, 30000),
        new FramePoint(0, 12000)
    ])
], 3);
Assert(collinearVertexFrames.Count == 1, "collinear polyline vertices accepted");

IReadOnlyList<DetectedFrame> concaveFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(5, true,
    [
        new FramePoint(0, 0),
        new FramePoint(30000, 0),
        new FramePoint(30000, 30000),
        new FramePoint(15000, 30000),
        new FramePoint(15000, 20000),
        new FramePoint(0, 20000)
    ])
], 3);
Assert(concaveFrames.Count == 0, "concave orthogonal polyline rejected");

IReadOnlyList<DetectedFrame> nearClosedFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(6, false,
    [
        new FramePoint(0, 2.9),
        new FramePoint(0, 30000),
        new FramePoint(30000, 30000),
        new FramePoint(30000, 0),
        new FramePoint(0, 0)
    ])
], 3);
Assert(nearClosedFrames.Count == 1, "polyline closure gap within tolerance accepted");

IReadOnlyList<DetectedFrame> openFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(7, false,
    [
        new FramePoint(0, 3.1),
        new FramePoint(0, 30000),
        new FramePoint(30000, 30000),
        new FramePoint(30000, 0),
        new FramePoint(0, 0)
    ])
], 3);
Assert(openFrames.Count == 0, "polyline closure gap above tolerance rejected");

IReadOnlyList<DetectedFrame> lineFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(10, false, [new FramePoint(0, 0), new FramePoint(30000, 0)]),
    new FramePath(11, false, [new FramePoint(30002.9, 0), new FramePoint(30002.9, 30000)]),
    new FramePath(12, false, [new FramePoint(30000, 30002.9), new FramePoint(0, 30002.9)]),
    new FramePath(13, false, [new FramePoint(-2.9, 30000), new FramePoint(-2.9, 0)])
], 3);
Assert(lineFrames.Count == 1, "four lines with corner gaps within tolerance accepted");
Assert(lineFrames[0].SourceIds.OrderBy(id => id).SequenceEqual([10, 11, 12, 13]),
    "all four frame lines selected");

IReadOnlyList<DetectedFrame> brokenLineFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(20, false, [new FramePoint(0, 0), new FramePoint(30000, 0)]),
    new FramePath(21, false, [new FramePoint(30003.1, 0), new FramePoint(30003.1, 30000)]),
    new FramePath(22, false, [new FramePoint(30000, 30000), new FramePoint(0, 30000)]),
    new FramePath(23, false, [new FramePoint(0, 30000), new FramePoint(0, 0)])
], 3);
Assert(brokenLineFrames.Count == 0, "line corner gap above tolerance rejected");

IReadOnlyList<DetectedFrame> nestedFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(30, true,
    [
        new FramePoint(0, 0), new FramePoint(50000, 0),
        new FramePoint(50000, 40000), new FramePoint(0, 40000)
    ]),
    new FramePath(31, true,
    [
        new FramePoint(5000, 5000), new FramePoint(35000, 5000),
        new FramePoint(35000, 35000), new FramePoint(5000, 35000)
    ])
], 3);
Assert(nestedFrames.Count == 1 && nestedFrames[0].SourceIds.SequenceEqual([30]),
    "only outer frame retained when candidates are nested");

IReadOnlyList<DetectedFrame> duplicateFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(40, true,
    [
        new FramePoint(0, 0), new FramePoint(30000, 0),
        new FramePoint(30000, 30000), new FramePoint(0, 30000)
    ]),
    new FramePath(41, true,
    [
        new FramePoint(1, 1), new FramePoint(30001, 1),
        new FramePoint(30001, 30001), new FramePoint(1, 30001)
    ])
], 3);
Assert(duplicateFrames.Count == 1 && duplicateFrames[0].SourceIds.OrderBy(id => id).SequenceEqual([40, 41]),
    "coincident frame entities are grouped");

IReadOnlyList<DetectedFrame> overlappingFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(50, true,
    [
        new FramePoint(0, 0), new FramePoint(30000, 0),
        new FramePoint(30000, 30000), new FramePoint(0, 30000)
    ]),
    new FramePath(51, true,
    [
        new FramePoint(15000, 15000), new FramePoint(45000, 15000),
        new FramePoint(45000, 45000), new FramePoint(15000, 45000)
    ])
], 3);
Assert(overlappingFrames.Count == 2, "overlapping non-contained frames retained");

IReadOnlyList<DetectedFrame> gridFrames = FrameSheetOrder.Sort(
[
    new DetectedFrame(0, 0, 50, 50, [4]),
    new DetectedFrame(100, 0, 150, 50, [3]),
    new DetectedFrame(0, 100, 50, 150, [2]),
    new DetectedFrame(100, 100, 150, 150, [1])
]);
Assert(gridFrames.Select(frame => frame.SourceIds[0]).SequenceEqual([1, 2, 3, 4]),
    "frames sorted right-to-left then top-to-bottom");

IReadOnlyList<DetectedFrame> misalignedRow = FrameSheetOrder.Sort(
[
    new DetectedFrame(0, 100, 50, 152, [20]),
    new DetectedFrame(100, 100, 150, 150, [10])
]);
Assert(misalignedRow.Select(frame => frame.SourceIds[0]).SequenceEqual([10, 20]),
    "same-row frames keep right-to-left order when tops differ slightly");

Assert(PdfPc3Viewer.TryDisable("""
    {
     "4" :
     {
      "name" : "View_New_File",
      "value" : true
     }
    }
    """, out string silencedPc3) &&
    silencedPc3.IndexOf("\"value\" : false", StringComparison.Ordinal) >= 0 &&
    silencedPc3.IndexOf("\"value\" : true", StringComparison.Ordinal) < 0,
    "json pc3 viewer flag disabled");
Assert(!PdfPc3Viewer.TryDisable("PIAFILEVERSION_2.0,PC3VER1,compress", out _),
    "compressed pc3 viewer patch rejected");
Assert(!PdfPc3Viewer.TryDisable("""
    {
      "name" : "View_New_File",
      "value" : false
    }
    """, out _),
    "already disabled json pc3 left unchanged");

IReadOnlyList<DetectedFrame> incompleteBoundaryFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(60, true,
    [
        new FramePoint(0, 0),
        new FramePoint(30000, 0),
        new FramePoint(0, 0),
        new FramePoint(0, 30000)
    ])
], 3);
Assert(incompleteBoundaryFrames.Count == 0, "polyline without all four rectangle sides rejected");

IReadOnlyList<DetectedFrame> falseBucketNeighborFrames = AxisAlignedFrameDetector.Find(
[
    new FramePath(70, false, [new FramePoint(0, 0), new FramePoint(30000, 0)]),
    new FramePath(71, false, [new FramePoint(0, 0), new FramePoint(0, 30000)]),
    new FramePath(72, false, [new FramePoint(30000, 0), new FramePoint(30000, 30000)]),
    new FramePath(73, false, [new FramePoint(5.9, 30000), new FramePoint(30000, 30000)])
], 3);
Assert(falseBucketNeighborFrames.Count == 0, "spatial bucket neighbor outside tolerance rejected");

const string isoA4Name = "ISO_A4_(210.00_x_297.00_MM)";
const string isoAltName = "ISO_ALT_(211.00_x_297.00_MM)";
Assert(IsoMedia.IsIsoName("ISO_full_bleed_A4_(210.00_x_297.00_MM)"), "iso prefix");
Assert(!IsoMedia.IsIsoName("ANSI_A_(8.50_x_11.00_Inches)"), "non-iso prefix");
Assert(IsoMedia.TryParseSize(isoA4Name, out double isoW, out double isoH) && isoW == 210 && isoH == 297, "iso parse");
Assert(IsoMedia.TryGetMatchingArea(isoA4Name, 205, 292, out double isoArea) && isoArea == 62370, "iso matching candidate area");
Assert(!IsoMedia.TryGetMatchingArea(isoA4Name, 200, 280, out _), "iso excessive whitespace candidate rejected");
Assert(!IsoMedia.TryGetMatchingArea("ANSI_A_(210.00_x_297.00_MM)", 205, 292, out _), "non-iso candidate rejected");
Assert(IsoMedia.PaperMatches(210, 297, 210, 297), "iso exact dimensions match");
Assert(IsoMedia.PaperMatches(210, 297, 205, 292), "iso five millimeter excess matches");
Assert(!IsoMedia.PaperMatches(210, 297, 204.99, 292), "iso width excess over threshold rejected");
Assert(!IsoMedia.PaperMatches(210, 297, 205, 291.99), "iso height excess over threshold rejected");
Assert(IsoMedia.PaperMatches(210, 297, 292, 205), "iso rotated dimensions match");
Assert(!IsoMedia.PaperMatches(210, 297, 200, 280), "iso excessive whitespace rejected");
Assert(!IsoMedia.PaperMatches(210, 297, 211, 297), "iso undersized width rejected");
Assert(!IsoMedia.PaperMatches(210, 297, 210.04, 297), "iso slight undersized width rejected");
Assert(!IsoMedia.PaperMatches(210, 297, 297, 210.04), "iso slight rotated undersized width rejected");
Assert(IsoMedia.Kind("ISO_full_bleed_A4_(210.00_x_297.00_MM)") < IsoMedia.Kind("ISO_expand_A4_(210.00_x_297.00_MM)"), "iso kind order");

var probedMedia = new List<string>();
IsoMediaSelection? selectedMedia = IsoMediaSelector.Pick(
    [isoA4Name, isoAltName],
    206,
    292,
    name =>
    {
        probedMedia.Add(name);
        return name == isoA4Name
            ? new IsoMediaProbeResult(false, 62370)
            : new IsoMediaProbeResult(true, 62000);
    });
Assert(probedMedia.Count == 2, "all matching iso candidates probed");
Assert(selectedMedia.HasValue && selectedMedia.Value.CanonicalName == isoAltName && selectedMedia.Value.Rotated,
    "smallest driver-reported iso selected");

probedMedia.Clear();
selectedMedia = IsoMediaSelector.Pick(
    [isoA4Name, isoAltName],
    206,
    292,
    name =>
    {
        probedMedia.Add(name);
        return name == isoA4Name ? new IsoMediaProbeResult(false, 62370) : null;
    });
Assert(probedMedia.Count == 2, "trailing iso candidate probed");
Assert(selectedMedia.HasValue && selectedMedia.Value.CanonicalName == isoA4Name,
    "rejected trailing iso candidate does not displace winner");

ExactPaper.Validate(100, 50, 100, 50, [0, 0, 0, 0], 0.01, 0.01);
try
{
    ExactPaper.Validate(100, 50, 210, 297, [0, 0, 0, 0], 0.01, 0.01);
    throw new InvalidOperationException("Exact paper substitution accepted.");
}
catch (AutoBIMFusion.QuickPdf.QuickPdfException) { }

const string decoratedEvaluateLisp = "?acedEvaluateLisp@@YAHPEB_WAEAPEAUresbuf@@@Z";
IntPtr resolvedExport = AccoreNative.ResolveEvaluateLisp(
    name => name == decoratedEvaluateLisp ? new IntPtr(42) : IntPtr.Zero);
Assert(resolvedExport == new IntPtr(42), "accore decorated export fallback");

const string decoratedReleaseResult = "?acutRelRb@@YAHPEAUresbuf@@@Z";
resolvedExport = AccoreNative.ResolveReleaseResult(
    module => module == "acdb24.dll" ? new IntPtr(24) : new IntPtr(1),
    (module, name) => module == new IntPtr(24) && name == decoratedReleaseResult
        ? new IntPtr(84)
        : IntPtr.Zero);
Assert(resolvedExport == new IntPtr(84), "acdb decorated release export fallback");
Assert(AccoreNative.EvaluationSucceeded(1), "acedEvaluateLisp success result accepted");
Assert(!AccoreNative.EvaluationSucceeded(0), "acedEvaluateLisp failure result rejected");

Console.WriteLine("PASS: escaping, guards, spans, numeric formatting, Serilog, JSON and QuickPDF helpers.");

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}

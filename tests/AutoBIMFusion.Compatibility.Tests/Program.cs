using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.Logging;
using AutoBIMFusion.QuickPdf.Media;
using AutoBIMFusion.QuickPdf.Naming;
using AutoBIMFusion.QuickPdf.Plotting;
using Serilog;
using System.Globalization;
using System.Text.Json;
#if NETFRAMEWORK
using AutoBIMFusion.Common.Compatibility;
#endif

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

var queue = new PriorityQueue<int, double>();
var random = new Random(47);
var priorities = Enumerable.Range(0, 1000).Select(_ => random.Next(-20, 20)).ToArray();
foreach (int priority in priorities) queue.Enqueue(priority, priority);
Assert(queue.Count == priorities.Length, "duplicate priorities retained");
foreach (int expected in priorities.OrderBy(x => x)) Assert(queue.Dequeue() == expected, "priority order");
Assert(queue.Count == 0, "queue drained");
try
{
    queue.Dequeue();
    throw new Exception("Empty dequeue accepted.");
}
catch (InvalidOperationException) { }

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

Console.WriteLine("PASS: escaping, guards, spans, numeric formatting, priority queue, Serilog, JSON and QuickPDF helpers.");

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}

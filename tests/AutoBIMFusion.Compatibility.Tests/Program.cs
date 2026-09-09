using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.QuickPdf.Media;
using AutoBIMFusion.QuickPdf.Naming;
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
try
{
    using (var log = new LoggerConfiguration().WriteTo.File(logFile, shared: true).CreateLogger())
    {
        log.Information("Compatibility {Version}", "2019–2027");
    }
    Assert(File.ReadAllText(logFile).Contains("2019–2027"), "Serilog file sink");
    string json = JsonSerializer.Serialize(new { Success = true, Message = "Проверка" });
    using var document = JsonDocument.Parse(json);
    Assert(document.RootElement.GetProperty("Message").GetString() == "Проверка", "JSON round trip");
}
finally
{
    File.Delete(logFile);
    Directory.Delete(directory);
}

Assert(QuickPdfNaming.SafeName("Plan:A*.dwg") == "Plan_A_", "quickpdf safe name");
Assert(QuickPdfNaming.SafeName(@"C:\tmp\Frame.dwg") == "Frame", "quickpdf path name");
Assert(QuickPdfNaming.SafeName("   ") == "Drawing", "quickpdf blank name");
Assert(QuickPdfNaming.TryReadSheetIndex("Plan_012", "Plan", out int sheet) && sheet == 12, "quickpdf sheet index");
Assert(!QuickPdfNaming.TryReadSheetIndex("Plan_12a", "Plan", out _), "quickpdf non-digit sheet");

Assert(IsoMedia.IsIsoName("ISO_full_bleed_A4_(210.00_x_297.00_MM)"), "iso prefix");
Assert(!IsoMedia.IsIsoName("ANSI_A_(8.50_x_11.00_Inches)"), "non-iso prefix");
Assert(IsoMedia.TryParseSize("ISO_A4_(210.00_x_297.00_MM)", out double isoW, out double isoH) && isoW == 210 && isoH == 297, "iso parse");
Assert(IsoMedia.PaperCanFit(210, 297, 200, 280), "iso fit");
Assert(!IsoMedia.PaperCanFit(210, 297, 500, 280), "iso too small");
Assert(IsoMedia.Kind("ISO_full_bleed_A4_(210.00_x_297.00_MM)") < IsoMedia.Kind("ISO_expand_A4_(210.00_x_297.00_MM)"), "iso kind order");

ExactPaper.Validate(100, 50, 100, 50, [0, 0, 0, 0], 0.01, 0.01);
try
{
    ExactPaper.Validate(100, 50, 210, 297, [0, 0, 0, 0], 0.01, 0.01);
    throw new InvalidOperationException("Exact paper substitution accepted.");
}
catch (AutoBIMFusion.QuickPdf.QuickPdfException) { }

Console.WriteLine("PASS: escaping, guards, spans, numeric formatting, priority queue, Serilog, JSON and QuickPDF helpers.");

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}

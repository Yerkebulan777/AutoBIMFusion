using AutoBIMFusion.Common.Helpers;
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

Console.WriteLine("PASS: escaping, guards, spans, numeric formatting, priority queue, Serilog and JSON.");

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}

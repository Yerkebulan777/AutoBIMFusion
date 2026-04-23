using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using System.Reflection;

namespace AutoBIMFusion.Infrastructure.Logging;

internal static class LoggerFactory
{
    private static readonly Lazy<Logger> SharedLogger = new(CreateFileLogger);

    public static Logger GetSharedLogger()
    {
        return SharedLogger.Value;
    }

    private static Logger CreateFileLogger()
    {
        try
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            string logsDir = Path.Combine(baseDir, "Logs");
            string logFile = Path.Combine(logsDir, $"merge-{DateTime.Today:yyyy-MM-dd}.log");

            _ = Directory.CreateDirectory(logsDir);

            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logFile, shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(new DiagnosticSink())
                .CreateLogger();
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"Failed to create file logger: {ex}");
            return new LoggerConfiguration().CreateLogger();
        }
    }

    private sealed class DiagnosticSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            string msg = $"{logEvent.Timestamp:HH:mm:ss.fff} [{logEvent.Level}] {logEvent.RenderMessage()}";
            if (logEvent.Exception is not null)
            {
                msg = $"{msg}{Environment.NewLine}{logEvent.Exception}";
            }

            if (Debugger.IsAttached)
            {
                Debug.WriteLine(msg);
            }
            else
            {
                System.Diagnostics.Trace.WriteLine(msg);
            }
        }
    }
}

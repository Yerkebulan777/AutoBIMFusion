using System.Diagnostics;
using System.Reflection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Trace = System.Diagnostics.Trace;

namespace AutoBIMFusion.Common.Logging;

public static class LoggerFactory
{
    private static readonly Lazy<Logger> SharedLogger = new(CreateFileLogger);

    public static Logger GetSharedLogger()
    {
        return SharedLogger.Value;
    }

    public static string GetCurrentLogFilePath()
    {
        return Path.Combine(GetLogsDirectory(), BuildLogFileName());
    }

    private static string BuildLogFileName()
    {
        return $"merge-{DateTime.Today:yyyy-MM-dd}.log";
    }

    private static Logger CreateFileLogger()
    {
        try
        {
            var logsDir = GetLogsDirectory();
            var logFile = GetCurrentLogFilePath();

            _ = Directory.CreateDirectory(logsDir);

            return CreateFileLoggerCore(logFile);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to create file logger: {ex}");
            return new LoggerConfiguration().CreateLogger();
        }
    }

    private static Logger CreateFileLoggerCore(string logFile)
    {
        try
        {
            var logsDir = Path.GetDirectoryName(logFile);
            if (!string.IsNullOrEmpty(logsDir)) _ = Directory.CreateDirectory(logsDir);

            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(logFile, shared: true,
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(new DiagnosticSink())
                .CreateLogger();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to create logger for file '{logFile}': {ex}");
            return new LoggerConfiguration().CreateLogger();
        }
    }

    private static string GetLogsDirectory()
    {
        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.Combine(baseDir, "Logs");
    }

    private sealed class DiagnosticSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            var msg = $"{logEvent.Timestamp:HH:mm:ss.fff} [{logEvent.Level}] {logEvent.RenderMessage()}";
            if (logEvent.Exception is not null) msg = $"{msg}{Environment.NewLine}{logEvent.Exception}";

            if (Debugger.IsAttached)
                Debug.WriteLine(msg);
            else
                Trace.WriteLine(msg);
        }
    }
}

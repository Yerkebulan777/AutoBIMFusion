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
            string logsDir = GetLogsDirectory();
            string logFile = GetCurrentLogFilePath();

            _ = Directory.CreateDirectory(logsDir);

            return CreateFileLoggerCore(logFile);
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"Failed to create file logger: {ex}");
            return new LoggerConfiguration().CreateLogger();
        }
    }

    private static Logger CreateFileLoggerCore(string logFile)
    {
        try
        {
            string? logsDir = Path.GetDirectoryName(logFile);
            if (!string.IsNullOrEmpty(logsDir))
            {
                _ = Directory.CreateDirectory(logsDir);
            }

            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(logFile, shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(new DiagnosticSink())
                .CreateLogger();
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"Failed to create logger for file '{logFile}': {ex}");
            return new LoggerConfiguration().CreateLogger();
        }
    }

    private static string GetLogsDirectory()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.Combine(baseDir, "Logs");
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

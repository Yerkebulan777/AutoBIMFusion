using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using DiagnosticsTrace = System.Diagnostics.Trace;

namespace AutoBIMFusion.Common.Logging;

public static class LoggerFactory
{
    private static readonly Lazy<Logger> SharedLogger = new(CreateFileLogger);

    private const string LogLevelEnvVar = "AUTOBIMFUSION_LOG_LEVEL";
    private const long MaxFileSizeBytes = 10L * 1024 * 1024; // 10 MB
    private const int MaxRetainedFiles = 10;
    private const int AsyncBufferSize = 8192;

    public static Logger GetSharedLogger()
    {
        return SharedLogger.Value;
    }

    public static string GetCurrentLogFilePath()
    {
        return Path.Combine(GetLogsDirectory(), BuildLogFileName());
    }

    /// <summary>
    /// Returns the logs directory path. Falls back to the current directory
    /// if the assembly location cannot be determined (e.g. memory-loaded assembly).
    /// </summary>
    private static string GetLogsDirectory()
    {
        string assemblyLocation = typeof(LoggerFactory).Assembly.Location;

        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            string? baseDir = Path.GetDirectoryName(assemblyLocation);
            if (baseDir is not null)
                return Path.Combine(baseDir, "Logs");
        }

        // Fallback: use the app context base directory (e.g., C:\Program Files\Autodesk\...)
        return Path.Combine(AppContext.BaseDirectory, "Logs");
    }

    private static string BuildLogFileName()
    {
        return $"merge-{DateTime.Today:yyyy-MM-dd}.log";
    }

    private static Logger CreateFileLogger()
    {
        try
        {
            LogEventLevel minimumLevel = ResolveMinimumLevel();
            string logsDir = GetLogsDirectory();
            _ = Directory.CreateDirectory(logsDir);

            string logFile = Path.Combine(logsDir, BuildLogFileName());
            return CreateFileLoggerCore(logFile, minimumLevel);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to create file logger: {ex}");
            return CreateSilentLogger(LogEventLevel.Information);
        }
    }

    private static Logger CreateFileLoggerCore(string logFile, LogEventLevel minimumLevel)
    {
        try
        {
            return new LoggerConfiguration()
                .MinimumLevel.Is(minimumLevel)
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .Enrich.With<ThreadIdEnricher>()
                .WriteTo.Async(
                    wt => wt.File(
                        logFile,
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: MaxFileSizeBytes,
                        retainedFileCountLimit: MaxRetainedFiles,
                        shared: true,
                        outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (PID:{ProcessId}, TID:{ThreadId}) {Message:lj}{NewLine}{Exception}"),
                    bufferSize: AsyncBufferSize,
                    blockWhenFull: false)
                .WriteTo.Sink(new DiagnosticSink(minimumLevel))
                .CreateLogger();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to create logger for file '{logFile}': {ex}");
            return CreateSilentLogger(minimumLevel);
        }
    }

    private static Logger CreateSilentLogger(LogEventLevel minimumLevel)
    {
        return new LoggerConfiguration()
            .WriteTo.Sink(new DiagnosticSink(minimumLevel))
            .CreateLogger();
    }



    private static LogEventLevel ResolveMinimumLevel()
    {
        string? envValue = Environment.GetEnvironmentVariable(LogLevelEnvVar);

        if (string.IsNullOrWhiteSpace(envValue))
            return LogEventLevel.Information;

        return envValue.Trim().ToUpperInvariant() switch
        {
            "VERBOSE" => LogEventLevel.Verbose,
            "DEBUG" => LogEventLevel.Debug,
            "INFORMATION" or "INFO" => LogEventLevel.Information,
            "WARNING" or "WARN" => LogEventLevel.Warning,
            "ERROR" => LogEventLevel.Error,
            "FATAL" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    private sealed class ThreadIdEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "ThreadId", Environment.CurrentManagedThreadId));
        }
    }

    private sealed class DiagnosticSink : ILogEventSink
    {
        private readonly LogEventLevel _minimumLevel;

        public DiagnosticSink(LogEventLevel minimumLevel)
        {
            _minimumLevel = minimumLevel;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level < _minimumLevel)
                return;

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
                DiagnosticsTrace.WriteLine(msg);
            }
        }
    }
}

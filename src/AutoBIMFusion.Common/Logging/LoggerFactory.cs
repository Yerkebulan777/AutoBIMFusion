using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using System.Text;
using DiagnosticsTrace = System.Diagnostics.Trace;

namespace AutoBIMFusion.Common.Logging;

public static class LoggerFactory
{
    private static string? _currentLogFilePath;

    private static readonly Lazy<Logger> SharedLogger = new(CreateLogger);

    private const long MaxFileSizeBytes = 10L * 1024 * 1024;
    private const int MaxRetainedFiles = 5;
    private const string LogLevelEnvVar = "LOG_LEVEL";

    public static Logger GetSharedLogger() => SharedLogger.Value;

    public static string GetCurrentLogFilePath()
    {
        return _currentLogFilePath ?? Path.Combine(GetLogsDirectory(), BuildLogFileName());
    }

    private static string GetLogsDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "AutoBIMFusion", "Logs");
    }

    private static string BuildLogFileName() => $"merge-{DateTime.Today:yyyy-MM-dd}.log";

    private static LogEventLevel ResolveMinimumLevel()
    {
        string? env = Environment.GetEnvironmentVariable(LogLevelEnvVar);

        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim().ToUpperInvariant() switch
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

#if DEBUG
        return LogEventLevel.Debug;
#else
        return LogEventLevel.Information;
#endif
    }

    private static Logger CreateLogger()
    {
        LogEventLevel level = ResolveMinimumLevel();
        try
        {
            string logsDir = GetLogsDirectory();
            Directory.CreateDirectory(logsDir);
            string logFile = Path.Combine(logsDir, BuildLogFileName());
            _currentLogFilePath = logFile;

            return new LoggerConfiguration()
                .MinimumLevel.Is(level)
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .Enrich.With<ThreadIdEnricher>()
                .WriteTo.File(
                    logFile,
                    rollingInterval: RollingInterval.Infinite,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: MaxFileSizeBytes,
                    retainedFileCountLimit: MaxRetainedFiles,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (PID:{ProcessId}, TID:{ThreadId}) {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(new DiagnosticSink())
                .CreateLogger();
        }
        catch (Exception ex)
        {
            TryWriteBootstrapFailure(ex);
            Debug.WriteLine($"[AutoBIMFusion] Logger init failed: {ex}");

            return new LoggerConfiguration()
                .MinimumLevel.Is(level)
                .WriteTo.Sink(new DiagnosticSink())
                .CreateLogger();
        }
    }

    private static void TryWriteBootstrapFailure(Exception ex)
    {
        try
        {
            string logsDir;
            try { logsDir = GetLogsDirectory(); }
            catch { logsDir = Path.Combine(AppContext.BaseDirectory, "Logs"); }

            Directory.CreateDirectory(logsDir);

            string message = new StringBuilder()
                .AppendLine("==== AutoBIMFusion logger bootstrap failure ====")
                .AppendLine($"Timestamp : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}")
                .AppendLine($"ProcessId : {Environment.ProcessId}")
                .AppendLine($"Exception : {ex}")
                .AppendLine()
                .ToString();

            File.AppendAllText(Path.Combine(logsDir, "logger-bootstrap-failure.log"), message, Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to write bootstrap diagnostics: {writeEx}");
        }
    }

    private sealed class ThreadIdEnricher : ILogEventEnricher
    {
        [ThreadStatic]
        private static LogEventProperty? _cached;

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            _cached ??= propertyFactory.CreateProperty("ThreadId", Environment.CurrentManagedThreadId);
            logEvent.AddPropertyIfAbsent(_cached);
        }
    }

    private sealed class DiagnosticSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            string msg = logEvent.Exception is null
                ? $"{logEvent.Timestamp:HH:mm:ss.fff} [{logEvent.Level}] {logEvent.RenderMessage()}"
                : $"{logEvent.Timestamp:HH:mm:ss.fff} [{logEvent.Level}] {logEvent.RenderMessage()}{Environment.NewLine}{logEvent.Exception}";

            if (Debugger.IsAttached)
                Debug.WriteLine(msg);
            else
                DiagnosticsTrace.WriteLine(msg);
        }
    }
}

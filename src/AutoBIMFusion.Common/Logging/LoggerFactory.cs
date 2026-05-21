using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using System.Text;
using DiagnosticsTrace = System.Diagnostics.Trace;

namespace AutoBIMFusion.Common.Logging;

public static class LoggerFactory
{
    private static readonly Lazy<Logger> SharedLogger = new(CreateLogger);

    private const LogEventLevel Level = LogEventLevel.Debug;
    private const long MaxFileSizeBytes = 10L * 1024 * 1024;
    private const int MaxRetainedFiles = 5;

    public static Logger GetSharedLogger()
    {
        return SharedLogger.Value;
    }

    public static string GetCurrentLogFilePath()
    {
        return Path.Combine(GetLogsDirectory(), BuildLogFileName());
    }

    private static string GetLogsDirectory()
    {
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        string logsDir = Path.Combine(documentsPath, "AutoBIMFusion", "Logs");

        Console.WriteLine(logsDir);

        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        return logsDir;
    }

    private static string BuildLogFileName()
    {
        return $"merge-{DateTime.Today:yyyy-MM-dd}.log";
    }

    private static Logger CreateLogger()
    {
        try
        {
            string logsDir = GetLogsDirectory();

            string logFile = Path.Combine(logsDir, BuildLogFileName());

            return new LoggerConfiguration()
                .MinimumLevel.Is(Level)
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
                .MinimumLevel.Is(Level)
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

            _ = Directory.CreateDirectory(logsDir);

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
        private LogEventProperty? _cached;

        /// <summary>
        /// Enriches log events with the current thread ID.
        /// Caches the property for reuse since thread ID doesn't change within the same thread.
        /// </summary>
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

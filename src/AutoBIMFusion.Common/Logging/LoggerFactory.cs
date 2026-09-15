using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace AutoBIMFusion.Common.Logging;

public static class LoggerFactory
{
    public const string MergeDwgCommand = "MERGEDWG";
    public const string QuickPdfCommand = "QUICKPDF";

    private static readonly ConcurrentDictionary<string, Logger> CommandLoggers = new(StringComparer.OrdinalIgnoreCase);

    private const LogEventLevel DefaultLevel = LogEventLevel.Warning;
    private const long MaxFileSizeBytes = 10L * 1024 * 1024;
    private const int MaxRetainedFiles = 5;

    public static Logger GetCommandLogger(string commandName)
    {
        string normalized = NormalizeCommandName(commandName);
        return CommandLoggers.GetOrAdd(normalized, CreateLogger);
    }

    public static string GetCurrentLogFilePath(string commandName)
    {
        return Path.Combine(GetLogsDirectory(), BuildLogFileName(commandName));
    }

    public static string GetLogsDirectory()
    {
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string logsDir = Path.Combine(documentsPath, "AutoBIMFusion", "Logs");

        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        return logsDir;
    }

    internal static string BuildLogFileName(string commandName)
    {
        string slug = NormalizeCommandName(commandName).ToLowerInvariant().Replace('_', '-');
        return $"{slug}-{DateTime.Today:yyyy-MM-dd}.log";
    }

    internal static LoggerConfiguration ApplyCommandMinimum(
        LoggerConfiguration configuration,
        LogEventLevel configuredLevel) =>
        configuration.MinimumLevel.Is(
            configuredLevel < LogEventLevel.Information ? configuredLevel : LogEventLevel.Information);

    private static string NormalizeCommandName(string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return commandName.Trim();
    }

    private static Logger CreateLogger(string commandName)
    {
        string? configuredLevel = Environment.GetEnvironmentVariable("LOG_LEVEL")?.Trim();
        LogEventLevel level = Enum.TryParse(configuredLevel, true, out LogEventLevel parsedLevel)
            && Enum.IsDefined(typeof(LogEventLevel), parsedLevel)
            ? parsedLevel
            : DefaultLevel;

        try
        {
            string logFile = Path.Combine(GetLogsDirectory(), BuildLogFileName(commandName));

            return ApplyCommandMinimum(new LoggerConfiguration(), level)
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
                .CreateLogger();
        }
        catch (Exception ex)
        {
            TryWriteBootstrapFailure(ex);
            Debug.WriteLine($"[AutoBIMFusion] Logger init failed: {ex}");

            return ApplyCommandMinimum(new LoggerConfiguration(), level).CreateLogger();
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

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            _cached ??= propertyFactory.CreateProperty("ThreadId", Environment.CurrentManagedThreadId);
            logEvent.AddPropertyIfAbsent(_cached);
        }
    }
}

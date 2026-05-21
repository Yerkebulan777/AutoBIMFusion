using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using DiagnosticsTrace = System.Diagnostics.Trace;

namespace AutoBIMFusion.Common.Logging;

public static class LoggerFactory
{
    private static readonly Lazy<Logger> SharedLogger = new(CreateFileLogger);
    private static string? currentLogFilePath;

    private const string BootstrapFailureFileName = "logger-bootstrap-failure.log";
    private const long MaxFileSizeBytes = 10L * 1024 * 1024;
    private const string LogLevelEnvVar = "LOG_LEVEL";
    private const int MaxRetainedFiles = 5;

    public static Logger GetSharedLogger() => SharedLogger.Value;

    public static string GetCurrentLogFilePath()
    {
        return currentLogFilePath ?? Path.Combine(GetLogsDirectory(), BuildLogFileName());
    }

    private static string GetLogsDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "AutoBIMFusion", "Logs");
    }

    private static string BuildLogFileName() => $"merge-{DateTime.Today:yyyy-MM-dd}.log";

    private static Logger CreateFileLogger()
    {
        LogEventLevel minimumLevel = LogEventLevel.Information;
        string? logFile = null;

        try
        {
            minimumLevel = ResolveMinimumLevel();
            string logsDir = GetLogsDirectory();
            Directory.CreateDirectory(logsDir);
            logFile = Path.Combine(logsDir, BuildLogFileName());
            currentLogFilePath = logFile;
            return CreateFileLoggerCore(logFile, minimumLevel);
        }
        catch (Exception ex)
        {
            TryWriteBootstrapFailure("CreateFileLogger", ex, logFile);
            Debug.WriteLine($"[AutoBIMFusion] Failed to create file logger: {ex}");
            return CreateSilentLogger(minimumLevel);
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
                .WriteTo.File(
                    logFile,
                    rollingInterval: RollingInterval.Infinite,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: MaxFileSizeBytes,
                    retainedFileCountLimit: MaxRetainedFiles,
                    shared: true,
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (PID:{ProcessId}, TID:{ThreadId}) {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(new DiagnosticSink(minimumLevel))
                .CreateLogger();
        }
        catch (Exception ex)
        {
            TryWriteBootstrapFailure("CreateFileLoggerCore", ex, logFile);
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

    private static void TryWriteBootstrapFailure(string context, Exception ex, string? logFile = null)
    {
        try
        {
            string logsDir = ResolveBootstrapLogsDirectory();
            Directory.CreateDirectory(logsDir);
            string bootstrapFile = Path.Combine(logsDir, BootstrapFailureFileName);
            string message = BuildBootstrapFailureMessage(context, ex, logFile);
            File.AppendAllText(bootstrapFile, message, Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to write logger bootstrap diagnostics: {writeEx}");
        }
    }

    private static string ResolveBootstrapLogsDirectory()
    {
        try
        {
            return GetLogsDirectory();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to resolve logger bootstrap directory: {ex}");
            return Path.Combine(AppContext.BaseDirectory, "Logs");
        }
    }

    private static string BuildBootstrapFailureMessage(string context, Exception ex, string? logFile)
    {
        StringBuilder sb = new();
        sb.AppendLine("==== AutoBIMFusion logger bootstrap failure ====");
        sb.AppendLine($"Timestamp: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        sb.AppendLine($"Context: {context}");
        sb.AppendLine($"ProcessId: {Environment.ProcessId}");
        sb.AppendLine($"ThreadId: {Environment.CurrentManagedThreadId}");
        sb.AppendLine($"TargetLogFile: {logFile ?? "<not resolved>"}");
        sb.AppendLine($"AppContext.BaseDirectory: {AppContext.BaseDirectory}");
        sb.AppendLine($"LoggerFactory.Assembly.Location: {GetAssemblyLocation(typeof(LoggerFactory).Assembly)}");
        sb.AppendLine("Loaded Serilog assemblies:");

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => a.GetName().Name?.StartsWith("Serilog", StringComparison.OrdinalIgnoreCase) == true)
                     .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase))
        {
            AssemblyName name = assembly.GetName();
            sb.AppendLine($"- {name.Name}, Version={name.Version}, Location={GetAssemblyLocation(assembly)}");
        }

        sb.AppendLine("Exception:");
        sb.AppendLine(ex.ToString());
        sb.AppendLine();

        return sb.ToString();
    }

    private static string GetAssemblyLocation(Assembly assembly)
    {
        try
        {
            return assembly.Location;
        }
        catch (Exception ex)
        {
            return $"<unavailable: {ex.GetType().Name}: {ex.Message}>";
        }
    }

    private static LogEventLevel ResolveMinimumLevel()
    {
        string? envValue = Environment.GetEnvironmentVariable(LogLevelEnvVar);

        if (string.IsNullOrWhiteSpace(envValue))
        {
#if DEBUG
            return LogEventLevel.Debug;
#else
            return LogEventLevel.Information;
#endif
        }

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
        [ThreadStatic]
        private static LogEventProperty? _cachedProperty;

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            _cachedProperty ??= propertyFactory.CreateProperty("ThreadId", Environment.CurrentManagedThreadId);
            logEvent.AddPropertyIfAbsent(_cachedProperty);
        }
    }

    private sealed class DiagnosticSink : ILogEventSink
    {
        private readonly LogEventLevel _minimumLevel;

        public DiagnosticSink(LogEventLevel minimumLevel) => _minimumLevel = minimumLevel;

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level < _minimumLevel)
                return;

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

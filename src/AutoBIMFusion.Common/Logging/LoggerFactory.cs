using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Text;
using DiagnosticsTrace = System.Diagnostics.Trace;

namespace AutoBIMFusion.Common.Logging;

public static class LoggerFactory
{
    private const string BootstrapFailureFileName = "logger-bootstrap-failure.log";
    private const long MaxFileSizeBytes = 10L * 1024 * 1024; // 10 MB
    private const string LogLevelEnvVar = "LOG_LEVEL";
    private const int MaxRetainedFiles = 5;
    private static readonly Lazy<Logger> SharedLogger = new(CreateFileLogger);

    public static Logger GetSharedLogger()
    {
        return SharedLogger.Value;
    }

    public static string GetCurrentLogFilePath()
    {
        return Path.Combine(GetLogsDirectory(), BuildLogFileName());
    }

    /// <summary>
    ///     Returns the logs directory path. Falls back to the current directory
    ///     if the assembly location cannot be determined (e.g. memory-loaded assembly).
    /// </summary>
    private static string GetLogsDirectory()
    {
        string assemblyLocation = typeof(LoggerFactory).Assembly.Location;

        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            string? baseDir = Path.GetDirectoryName(assemblyLocation);

            if (baseDir is not null)
            {
                return Path.Combine(baseDir, "Logs");
            }
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
        LogEventLevel minimumLevel = LogEventLevel.Information;
        string? logFile = null;

        try
        {
            minimumLevel = ResolveMinimumLevel();
            string logsDir = GetLogsDirectory();
            _ = Directory.CreateDirectory(logsDir);

            logFile = Path.Combine(logsDir, BuildLogFileName());
            return CreateFileLoggerCore(logFile, minimumLevel);
        }
        catch (Exception ex) when (IsExpectedBootstrapException(ex))
        {
            TryWriteBootstrapFailure("CreateFileLogger", ex, logFile);
            Debug.WriteLine($"[AutoBIMFusion] Failed to create file logger: {ex}");
            return CreateSilentLogger(minimumLevel);
        }
        catch (Exception ex)
        {
            TryWriteBootstrapFailure("CreateFileLogger unexpected", ex, logFile);
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
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: MaxFileSizeBytes,
                    retainedFileCountLimit: MaxRetainedFiles,
                    shared: true,
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (PID:{ProcessId}, TID:{ThreadId}) {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(new DiagnosticSink(minimumLevel))
                .CreateLogger();
        }
        catch (Exception ex) when (IsExpectedBootstrapException(ex))
        {
            TryWriteBootstrapFailure("CreateFileLoggerCore", ex, logFile);
            Debug.WriteLine($"[AutoBIMFusion] Failed to create logger for file '{logFile}': {ex}");
            return CreateSilentLogger(minimumLevel);
        }
        catch (Exception ex)
        {
            TryWriteBootstrapFailure("CreateFileLoggerCore unexpected", ex, logFile);
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

    private static bool IsExpectedBootstrapException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or SecurityException
            or NotSupportedException
            or ArgumentException
            or TypeLoadException
            or MissingMethodException
            or FileLoadException
            or ReflectionTypeLoadException;
    }

    private static void TryWriteBootstrapFailure(string context, Exception ex, string? logFile = null)
    {
        try
        {
            string logsDir = ResolveBootstrapLogsDirectory();
            _ = Directory.CreateDirectory(logsDir);

            string bootstrapFile = Path.Combine(logsDir, BootstrapFailureFileName);
            string message = BuildBootstrapFailureMessage(context, ex, logFile);
            File.AppendAllText(bootstrapFile, message, Encoding.UTF8);
        }
        catch (Exception writeEx) when (IsExpectedBootstrapException(writeEx))
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to write logger bootstrap diagnostics: {writeEx}");
        }
        catch (Exception writeEx)
        {
            Debug.WriteLine(
                $"[AutoBIMFusion] Unexpected failure while writing logger bootstrap diagnostics: {writeEx}");
        }
    }

    private static string ResolveBootstrapLogsDirectory()
    {
        try
        {
            return GetLogsDirectory();
        }
        catch (Exception ex) when (IsExpectedBootstrapException(ex))
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to resolve logger bootstrap directory: {ex}");
            return Path.Combine(AppContext.BaseDirectory, "Logs");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoBIMFusion] Unexpected failure while resolving logger bootstrap directory: {ex}");
            return Path.Combine(AppContext.BaseDirectory, "Logs");
        }
    }

    private static string BuildBootstrapFailureMessage(string context, Exception ex, string? logFile)
    {
        StringBuilder sb = new();
        _ = sb.AppendLine("==== AutoBIMFusion logger bootstrap failure ====");
        _ = sb.AppendLine($"Timestamp: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        _ = sb.AppendLine($"Context: {context}");
        _ = sb.AppendLine($"ProcessId: {Environment.ProcessId}");
        _ = sb.AppendLine($"ThreadId: {Environment.CurrentManagedThreadId}");
        _ = sb.AppendLine($"TargetLogFile: {logFile ?? "<not resolved>"}");
        _ = sb.AppendLine($"AppContext.BaseDirectory: {AppContext.BaseDirectory}");
        _ = sb.AppendLine($"LoggerFactory.Assembly.Location: {GetAssemblyLocation(typeof(LoggerFactory).Assembly)}");
        _ = sb.AppendLine("Loaded Serilog assemblies:");

        foreach (Assembly? assembly in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => a.GetName().Name?.StartsWith("Serilog", StringComparison.OrdinalIgnoreCase) == true)
                     .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase))
        {
            AssemblyName name = assembly.GetName();
            _ = sb.AppendLine($"- {name.Name}, Version={name.Version}, Location={GetAssemblyLocation(assembly)}");
        }

        _ = sb.AppendLine("Exception:");
        _ = sb.AppendLine(ex.ToString());
        _ = sb.AppendLine();

        return sb.ToString();
    }

    private static string GetAssemblyLocation(Assembly assembly)
    {
        try
        {
            return assembly.Location;
        }
        catch (Exception ex) when (IsExpectedBootstrapException(ex))
        {
            return $"<unavailable: {ex.GetType().Name}: {ex.Message}>";
        }
        catch (Exception ex)
        {
            return $"<unexpected failure: {ex.GetType().Name}: {ex.Message}>";
        }
    }

    private static LogEventLevel ResolveMinimumLevel()
    {
        string? envValue = Environment.GetEnvironmentVariable(LogLevelEnvVar);

        return string.IsNullOrWhiteSpace(envValue)
            ? LogEventLevel.Information
            : envValue.Trim().ToUpperInvariant() switch
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
            {
                return;
            }

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

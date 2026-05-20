using System.Diagnostics;
using System.Security;
using System.Text.Json;
using AutoBIMFusion.Common.Logging;

namespace AutoBIMFusion.Merge.Diagnostics;

public sealed record MergeDiagnosticContext(string MergeFileId, string SourcePath, string SourceFileName);

public static class MergeDiagnostics
{
    public const int DefaultSampleLimit = 20;

    private const string DiagnosticFlagEnvVar = "AUTOBIMFUSION_DIAG";
    private const string LogLevelEnvVar = "LOG_LEVEL";
    private static readonly object WriteSync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static bool IsEnabled()
    {
        return IsEnabledFor(
            Environment.GetEnvironmentVariable(LogLevelEnvVar),
            Environment.GetEnvironmentVariable(DiagnosticFlagEnvVar));
    }

    public static bool IsEnabledFor(string? logLevel, string? diagnosticFlag)
    {
        if (IsTruthy(diagnosticFlag))
        {
            return true;
        }

        return string.Equals(logLevel?.Trim(), "DEBUG", StringComparison.OrdinalIgnoreCase)
               || string.Equals(logLevel?.Trim(), "VERBOSE", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetCurrentDiagnosticFilePath()
    {
        if (!IsEnabled())
        {
            return null;
        }

        string? logDirectory = Path.GetDirectoryName(LoggerFactory.GetCurrentLogFilePath());
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            return null;
        }

        return Path.Combine(logDirectory, $"merge-diagnostics-{DateTime.Today:yyyy-MM-dd}.jsonl");
    }

    public static MergeDiagnosticContext CreateFileContext(string sourcePath)
    {
        return new MergeDiagnosticContext(
            Guid.NewGuid().ToString("N"),
            sourcePath,
            Path.GetFileName(sourcePath));
    }

    public static IReadOnlyList<T> TakeSample<T>(IEnumerable<T> values, int limit = DefaultSampleLimit)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (limit <= 0)
        {
            return [];
        }

        List<T> result = [];
        foreach (T value in values)
        {
            if (result.Count >= limit)
            {
                break;
            }

            result.Add(value);
        }

        return result;
    }

    public static bool TryAddSample<T>(ICollection<T> samples, T sample, int limit = DefaultSampleLimit)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count >= limit)
        {
            return false;
        }

        samples.Add(sample);
        return true;
    }

    public static void WriteEvent(
        MergeDiagnosticContext? context,
        string eventName,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (context is null || !IsEnabled())
        {
            return;
        }

        string? path = GetCurrentDiagnosticFilePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            string json = BuildEventJson(context, eventName, properties);
            lock (WriteSync)
            {
                File.AppendAllText(path, json + Environment.NewLine);
            }
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to write merge diagnostics: {ex}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to write merge diagnostics: {ex}");
        }
        catch (SecurityException ex)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to write merge diagnostics: {ex}");
        }
        catch (NotSupportedException ex)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to write merge diagnostics: {ex}");
        }
        catch (ArgumentException ex)
        {
            Debug.WriteLine($"[AutoBIMFusion] Failed to write merge diagnostics: {ex}");
        }
    }

    public static string BuildEventJson(
        MergeDiagnosticContext context,
        string eventName,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["timestamp"] = DateTimeOffset.Now,
            ["eventName"] = eventName,
            ["mergeFileId"] = context.MergeFileId,
            ["sourcePath"] = context.SourcePath,
            ["sourceFileName"] = context.SourceFileName
        };

        if (properties is not null)
        {
            foreach (KeyValuePair<string, object?> property in properties)
            {
                payload[property.Key] = property.Value;
            }
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static IReadOnlyDictionary<string, double> FormatPoint(Point3d point)
    {
        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["x"] = point.X,
            ["y"] = point.Y,
            ["z"] = point.Z
        };
    }

    public static IReadOnlyDictionary<string, object?>? FormatExtents(Extents3d? extents)
    {
        if (!extents.HasValue)
        {
            return null;
        }

        return FormatExtents(extents.Value);
    }

    public static IReadOnlyDictionary<string, object?> FormatExtents(Extents3d extents)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["min"] = FormatPoint(extents.MinPoint),
            ["max"] = FormatPoint(extents.MaxPoint),
            ["diagonal"] = extents.MinPoint.DistanceTo(extents.MaxPoint)
        };
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "1" or "TRUE" or "YES" or "ON" => true,
            _ => false
        };
    }
}

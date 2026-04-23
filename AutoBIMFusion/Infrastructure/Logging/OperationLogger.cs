
using Serilog.Core;
using Serilog.Events;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Infrastructure.Logging;

internal sealed class OperationLogger(Editor ed)
{
    private readonly Logger _fileLogger = LoggerFactory.GetSharedLogger();

    public string Prefix { get; set; } = string.Empty;
    public bool EchoToEditor { get; set; } = true;

    public void Info(string message)
    {
        Log(LogEventLevel.Information, message);
    }

    public void Debug(string message)
    {
        Log(LogEventLevel.Debug, message);
    }

    public void Warn(string message)
    {
        Log(LogEventLevel.Warning, message);
    }

    public void Warn(System.Exception ex, string message)
    {
        Log(LogEventLevel.Warning, message, ex);
    }

    public void Error(System.Exception ex, string message)
    {
        Log(LogEventLevel.Error, message, ex);
    }

    private void Log(LogEventLevel level, string message, System.Exception? ex = null)
    {
        string full = Prefix.Length > 0 ? $"{Prefix} {message}" : message;
        string editorMsg = ex == null ? full : $"{full} ({ex.GetType().Name}: {Short(ex.Message, "Ошибка", 200)})";

        if (EchoToEditor && level != LogEventLevel.Debug)
        {
            string prefix = level switch
            {
                LogEventLevel.Warning => "[WARN] ",
                LogEventLevel.Error or LogEventLevel.Fatal => "[ERROR] ",
                _ => string.Empty
            };
            TryWriteToEditor($"\n{prefix}{editorMsg}");
        }

        if (ex == null)
        {
            _fileLogger.Write(level, full);
        }
        else
        {
            _fileLogger.Write(level, ex, full);
        }
    }

    private void TryWriteToEditor(string msg)
    {
        try
        {
            Editor? activeEd = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            (activeEd ?? ed).WriteMessage(msg);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
            when (ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.NotApplicable)
        {
            // Игнорируем вывод в невалидном UI-контексте
        }
        catch (System.Exception ex)
        {
            _fileLogger.Debug(ex, "Не удалось вывести сообщение в командную строку AutoCAD");
        }
    }

    private static string Short(string? message, string fallback, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return fallback;
        }

        ReadOnlySpan<char> span = message.AsSpan().Trim();
        int lineBreakIdx = span.IndexOfAny('\r', '\n');
        if (lineBreakIdx >= 0)
        {
            span = span[..lineBreakIdx].TrimEnd();
            if (span.Length == 0)
            {
                return fallback;
            }
        }
        return span.Length <= maxLength ? span.ToString() : span[..maxLength].ToString();
    }
}

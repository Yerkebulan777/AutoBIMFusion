using AutoBIMFusion.Application.Utils;
using Serilog.Core;
using Serilog.Events;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Infrastructure.Logging;

internal sealed class AILog(Editor ed)
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
        full = MaskSensitivePaths(full);

        string editorMsg = ex == null ? full : $"{full} ({ex.GetType().Name}: {StringUtils.Truncate(ex.Message, "Ошибка", 200)})";
        editorMsg = MaskSensitivePaths(editorMsg);

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

        System.Diagnostics.Trace.WriteLine($"[{level}] {full}");
        if (ex != null)
        {
            System.Diagnostics.Trace.WriteLine(ex.ToString());
        }
    }

    private static string MaskSensitivePaths(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        // Простое скрытие абсолютных путей для безопасности
        return System.Text.RegularExpressions.Regex.Replace(input, @"[A-Za-z]:\\[^:\*\?\""<>\|]*", "[PATH_HIDDEN]");
    }

    private void TryWriteToEditor(string msg)
    {
        try
        {
            Editor? activeEd = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            (activeEd ?? ed).WriteMessage(msg);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.NotApplicable)
        {
            // Игнорируем вывод в невалидном UI-контексте
        }
        catch (System.Exception ex)
        {
            _fileLogger.Debug(ex, "Не удалось вывести сообщение в командную строку AutoCAD");
        }
    }
}

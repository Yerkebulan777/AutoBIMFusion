using AutoBIMFusion.Application.Utils;
using Serilog.Core;
using Serilog.Events;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Infrastructure.Logging;

internal sealed class AILog(Editor ed)
{
    private readonly Logger _fileLogger = LoggerFactory.GetSharedLogger();

    public bool EchoToEditor { get; set; } = true;

    public void Info(string message) => Log(LogEventLevel.Information, message);
    public void Debug(string message) => Log(LogEventLevel.Debug, message);
    public void Warn(string message) => Log(LogEventLevel.Warning, message);
    public void Warn(System.Exception ex, string message) => Log(LogEventLevel.Warning, message, ex);
    public void Error(System.Exception ex, string message) => Log(LogEventLevel.Error, message, ex);

    private void Log(LogEventLevel level, string message, System.Exception? ex = null)
    {
        if (EchoToEditor && level != LogEventLevel.Debug)
        {
            string prefix = level switch
            {
                LogEventLevel.Warning => "[WARN] ",
                LogEventLevel.Error or LogEventLevel.Fatal => "[ERROR] ",
                _ => string.Empty
            };

            string editorMsg = ex == null ? message : $"{message} ({ex.GetType().Name}: {StringUtils.Truncate(ex.Message, "Ошибка", 200)})";
            TryWriteToEditor($"\n{prefix}{editorMsg}");
        }

        if (ex == null) _fileLogger.Write(level, message);
        else _fileLogger.Write(level, ex, message);

        System.Diagnostics.Trace.WriteLine($"[{level}] {message}");
        if (ex != null) System.Diagnostics.Trace.WriteLine(ex.ToString());
    }

    private void TryWriteToEditor(string msg)
    {
        try
        {
            Editor? activeEd = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            (activeEd ?? ed).WriteMessage(msg);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.NotApplicable) { }
        catch (System.Exception ex)
        {
            _fileLogger.Debug(ex, "Не удалось вывести сообщение в командную строку AutoCAD");
        }
    }
}

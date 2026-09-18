using Autodesk.AutoCAD.ApplicationServices;
using AutoBIMFusion.Common.Logging;
using AutoBIMFusion.QuickPdf;
using Serilog;
using System.Runtime.Versioning;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = System.Exception;

namespace AutoBIMFusion.Plugin.Commands;

[SupportedOSPlatform("Windows")]
public sealed class QuickPdfCommands
{
    [CommandMethod("QUICKPDF", CommandFlags.Modal)]
    public static void QuickPdfCommand()
    {
        ILogger log = LoggerFactory.GetCommandLogger(LoggerFactory.QuickPdfCommand);
        string logPath = LoggerFactory.GetCurrentLogFilePath(LoggerFactory.QuickPdfCommand);
        Document? document = AcadApp.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            log.Warning("{Command}: no active drawing", LoggerFactory.QuickPdfCommand);
            return;
        }

        Editor editor = document.Editor;
        try
        {
            if (!document.Database.TileMode)
            {
                log.Warning("{Command}: command requires model space", LoggerFactory.QuickPdfCommand);
                editor.WriteMessage("\nКоманда доступна только в пространстве модели.");
                return;
            }

#if CORECONSOLE_DIAGNOSTICS
            QuickPdfOrchestrator.ExportFrameList(document, log);
#else
            if (!QuickPdfOptionsDialog.TryShow(out QuickPdfOptions options))
            {
                editor.WriteMessage("\nQuickPDF: отменено.");
                return;
            }

            log.Information(
                "QUICKPDF options: scale={Scale}; color={Color}; start={Start}",
                options.ScaleLabel, options.ColorLabel, options.StartSheetLabel);
            if (!QuickPdfOrchestrator.TryExportInteractively(document, log, options))
            {
                editor.WriteMessage("\nQuickPDF: отменено.");
            }
#endif
        }
        catch (Exception ex) when (ex is QuickPdfException or Autodesk.AutoCAD.Runtime.Exception)
        {
            log.Warning(ex, "{Command} failed: {Message}", LoggerFactory.QuickPdfCommand, ex.Message);
            editor.WriteMessage("\nQuickPDF: " + ex.Message + "\nLog: " + logPath);
        }
        catch (Exception ex)
        {
            log.Error(ex, "{Command} failed", LoggerFactory.QuickPdfCommand);
            editor.WriteMessage("\nQuickPDF: " + ex.Message + "\nLog: " + logPath);
        }
    }

}

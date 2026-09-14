using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Logging;
using AutoBIMFusion.QuickPdf;
using Serilog;
using Serilog.Core;
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
        ILogger log = LoggerFactory.GetSharedLogger()
            .ForContext(Constants.SourceContextPropertyName, LoggerFactory.QuickPdfContext);
        Document? document = AcadApp.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            log.Warning("QUICKPDF: нет активного чертежа.");
            return;
        }

        Editor editor = document.Editor;
        try
        {
            if (!editor.IsInModel())
            {
                editor.WriteMessage("\nКоманда доступна только в пространстве модели.");
                return;
            }

            PromptPointResult first = editor.GetPoint("\nУкажите первый угол рамки чертежа: ");
            if (first.Status != PromptStatus.OK)
            {
                return;
            }

            PromptCornerOptions cornerOptions = new("\nУкажите противоположный угол: ", first.Value);
            PromptPointResult second = editor.GetCorner(cornerOptions);
            if (second.Status != PromptStatus.OK)
            {
                return;
            }

            QuickPdfOrchestrator.ExportFrame(document, first.Value, second.Value);
        }
        catch (Exception ex) when (ex is QuickPdfException or Autodesk.AutoCAD.Runtime.Exception)
        {
            log.Warning(ex, "QUICKPDF failed: {Message}", ex.Message);
            editor.WriteMessage("\nQuickPDF: " + ex.Message + "\nЛог: " + LoggerFactory.GetCurrentLogFilePath());
        }
        catch (Exception ex)
        {
            log.Error(ex, "QUICKPDF failed");
            editor.WriteMessage("\nQuickPDF: " + ex.Message + "\nЛог: " + LoggerFactory.GetCurrentLogFilePath());
        }
    }
}

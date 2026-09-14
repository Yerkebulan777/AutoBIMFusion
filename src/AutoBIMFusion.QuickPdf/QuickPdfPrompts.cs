using AutoBIMFusion.QuickPdf.Frames;
using AutoBIMFusion.QuickPdf.Naming;
using AutoBIMFusion.QuickPdf.Plotting;

namespace AutoBIMFusion.QuickPdf;

internal readonly struct QuickPdfInput
{
    internal QuickPdfInput(FrameWindow? area, string outputPath)
    {
        Area = area;
        OutputPath = outputPath;
    }

    internal FrameWindow? Area { get; }

    internal string OutputPath { get; }
}

/// <summary>
///     Интерактивный ввод QuickPDF: участок рамок и имя файла.
/// </summary>
internal static class QuickPdfPrompts
{
    internal static bool TryCollect(Editor editor, string drawingName, out QuickPdfInput input)
    {
        input = default;
        if (!TryPickArea(editor, out FrameWindow? area) ||
            !TryPromptPath(editor, QuickPdfNaming.SafeName(drawingName), out string? outputPath) ||
            outputPath is null)
        {
            return false;
        }

        input = new QuickPdfInput(area, outputPath);
        return true;
    }

    private static bool TryPickArea(Editor editor, out FrameWindow? area)
    {
        area = null;
        PromptPointResult first = editor.GetPoint(new PromptPointOptions(
            "\nУкажите первый угол участка с рамками (Escape — все рамки):")
        {
            AllowNone = true
        });
        if (IsAllFrames(first.Status))
        {
            return true;
        }

        if (first.Status != PromptStatus.OK)
        {
            return false;
        }

        PromptPointResult second = editor.GetCorner(new PromptCornerOptions(
            "\nУкажите противоположный угол:", first.Value)
        {
            AllowNone = true,
            UseDashedLine = true
        });
        if (IsAllFrames(second.Status))
        {
            return true;
        }

        if (second.Status != PromptStatus.OK)
        {
            return false;
        }

        area = new FrameWindow(first.Value.X, first.Value.Y, second.Value.X, second.Value.Y);
        return true;
    }

    private static bool TryPromptPath(Editor editor, string defaultName, out string? path)
    {
        path = null;
        PromptSaveFileOptions options = new("\nИмя распечатываемого файла:")
        {
            DialogCaption = "Сохранить PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            InitialDirectory = QuickPdfNaming.ResolveDesktop(),
            InitialFileName = defaultName + ".pdf"
        };
        using (new SystemVariableScope(("FILEDIA", (short)1)))
        {
            PromptFileNameResult result = editor.GetFileNameForSave(options);
            if (result.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(result.StringResult))
            {
                return false;
            }

            path = result.StringResult;
            return true;
        }
    }

    private static bool IsAllFrames(PromptStatus status)
    {
        return status is PromptStatus.Cancel or PromptStatus.None;
    }
}

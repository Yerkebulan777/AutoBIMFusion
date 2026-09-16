using AutoBIMFusion.QuickPdf.Frames;
using AutoBIMFusion.QuickPdf.Naming;
using SaveFileDialog = Autodesk.AutoCAD.Windows.SaveFileDialog;

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
            !TryPromptPath(QuickPdfNaming.SafeName(drawingName), out string? outputPath) ||
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
            "\nУкажите первый угол участка с рамками (Escape или Enter — все рамки):")
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

    private static bool TryPromptPath(string defaultName, out string? path)
    {
        path = null;
        SaveFileDialog dialog = new(
            "Сохранить PDF",
            Path.Combine(QuickPdfNaming.ResolveDesktop(), defaultName + ".pdf"),
            "pdf",
            "QuickPDF",
            SaveFileDialog.SaveFileDialogFlags.DoNotWarnIfFileExist);

        // DialogResult lives in WinForms; avoid a desktop framework reference in headless builds.
        object? result = typeof(SaveFileDialog).GetMethod(nameof(SaveFileDialog.ShowDialog), Type.EmptyTypes)!
            .Invoke(dialog, null);
        if (!string.Equals(result?.ToString(), "OK", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(dialog.Filename))
        {
            return false;
        }

        path = dialog.Filename;
        return true;
    }

    private static bool IsAllFrames(PromptStatus status)
    {
        return status is PromptStatus.Cancel or PromptStatus.None;
    }
}

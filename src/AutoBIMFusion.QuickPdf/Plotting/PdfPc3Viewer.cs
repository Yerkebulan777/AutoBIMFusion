using System.Text.RegularExpressions;

namespace AutoBIMFusion.QuickPdf.Plotting;

/// <summary>
///     В JSON-PC3 AutoCAD PDF флаг «Show results in viewer» хранится как View_New_File.
/// </summary>
internal static class PdfPc3Viewer
{
    private static readonly Regex ViewNewFileTrue = new(
        "(?<prefix>\"name\"\\s*:\\s*\"View_New_File\"\\s*,\\s*\"value\"\\s*:\\s*)true\\b",
        RegexOptions.CultureInvariant);

    internal static bool TryDisable(string contents, out string patched)
    {
        patched = contents;
        if (string.IsNullOrEmpty(contents) || contents.IndexOf("View_New_File", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        string replaced = ViewNewFileTrue.Replace(contents, "${prefix}false");
        if (replaced.Equals(contents, StringComparison.Ordinal))
        {
            return false;
        }

        patched = replaced;
        return true;
    }
}

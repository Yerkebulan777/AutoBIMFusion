using System.Text;

namespace AutoBIMFusion.QuickPdf.Naming;

public readonly struct PdfDestination
{
    public PdfDestination(string folder, string prefix)
    {
        Folder = folder;
        Prefix = prefix;
    }

    public string Folder { get; }

    public string Prefix { get; }
}

public static class QuickPdfNaming
{
    public static string SafeName(string? drawingName)
    {
        string value = drawingName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Drawing";
        }
        int separator = Max(value.LastIndexOf('\\'), value.LastIndexOf('/'));
        if (separator >= 0)
        {
            value = value[(separator + 1)..];
        }

        int dot = value.LastIndexOf('.');
        if (dot > 0)
        {
            value = value[..dot];
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string trimmed = builder.ToString().Trim(' ', '.');
        return trimmed.Length == 0 ? "Drawing" : trimmed;
    }

    public static PdfDestination Resolve(string? selectedPath, string? drawingName)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            string prefix = SafeName(drawingName);
            return new PdfDestination(Path.Combine(ResolveDesktop(), prefix), prefix);
        }

        string? directory = Path.GetDirectoryName(selectedPath);
        return new PdfDestination(
            string.IsNullOrWhiteSpace(directory) ? ResolveDesktop() : directory,
            SafeName(Path.GetFileName(selectedPath)));
    }

    public static string DrawingName(string? dwgName, string? documentName)
    {
        return dwgName is { Length: > 0 } ? dwgName : documentName ?? string.Empty;
    }

    public static string ResolveDesktop()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return desktop.Length > 0 ? desktop : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}

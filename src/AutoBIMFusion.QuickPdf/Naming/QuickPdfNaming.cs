using System.Globalization;
using System.Text;

namespace AutoBIMFusion.QuickPdf.Naming;

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

    public static string ResolveDesktop()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return desktop.Length > 0 ? desktop : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public static int NextSheetIndex(string folder, string prefix)
    {
        int max = -1;
        foreach (string file in Directory.EnumerateFiles(folder, prefix + "_*.pdf"))
        {
            if (TryReadSheetIndex(Path.GetFileNameWithoutExtension(file), prefix, out int index) && index > max)
            {
                max = index;
            }
        }

        return max + 1;
    }

    public static bool TryReadSheetIndex(string baseName, string prefix, out int index)
    {
        index = 0;
        if (string.IsNullOrEmpty(baseName) || string.IsNullOrEmpty(prefix))
        {
            return false;
        }

        if (!baseName.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string suffix = baseName[(prefix.Length + 1)..];
        return suffix.Length > 0 && suffix.All(char.IsDigit) &&
               int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}

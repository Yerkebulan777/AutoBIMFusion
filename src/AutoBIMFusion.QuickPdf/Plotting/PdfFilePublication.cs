namespace AutoBIMFusion.QuickPdf.Plotting;

internal static class PdfFilePublication
{
    internal static void Commit(string temporaryPath, string destinationPath)
    {
        if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
        {
            throw new QuickPdfException("Плоттер не создал непустой PDF.");
        }

        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null);
        }
        else
        {
            File.Move(temporaryPath, destinationPath);
        }
    }
}

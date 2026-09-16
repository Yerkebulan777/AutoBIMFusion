using AutoBIMFusion.QuickPdf;
using AutoBIMFusion.QuickPdf.Plotting;

internal static class PdfFilePublicationTests
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "QuickPDF-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "temporary.pdf");
        string destination = Path.Combine(directory, "Plan_000.pdf");
        string unrelated = Path.Combine(directory, "Other_000.pdf");
        try
        {
            File.WriteAllText(unrelated, "unrelated");
            File.WriteAllText(temporary, "first PDF");
            PdfFilePublication.Commit(temporary, destination);
            Assert(File.ReadAllText(destination) == "first PDF" && !File.Exists(temporary), "new PDF published");

            File.WriteAllText(temporary, "replacement PDF");
            PdfFilePublication.Commit(temporary, destination);
            Assert(File.ReadAllText(destination) == "replacement PDF" && !File.Exists(temporary), "existing PDF replaced");
            Assert(File.ReadAllText(unrelated) == "unrelated", "other PDFs preserved");

            foreach (bool empty in new[] { false, true })
            {
                if (empty) File.WriteAllText(temporary, string.Empty);
                try
                {
                    PdfFilePublication.Commit(temporary, destination);
                    throw new InvalidOperationException("missing or empty output accepted");
                }
                catch (QuickPdfException) { }
                Assert(File.ReadAllText(destination) == "replacement PDF", "failed plot preserves existing PDF");
            }

            File.WriteAllText(temporary, "locked replacement");
            using (FileStream locked = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    PdfFilePublication.Commit(temporary, destination);
                    throw new InvalidOperationException("locked PDF was overwritten");
                }
                catch (IOException) { }
                Assert(File.ReadAllText(destination) == "replacement PDF", "locked PDF preserved");
                Assert(File.Exists(temporary), "failed replacement retains temporary output for caller cleanup");
            }
        }
        finally
        {
            File.Delete(temporary);
            File.Delete(destination);
            File.Delete(unrelated);
            Directory.Delete(directory);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

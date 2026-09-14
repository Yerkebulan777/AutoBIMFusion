using System.Globalization;
using System.Text;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = System.Exception;

namespace AutoBIMFusion.QuickPdf.Plotting;

/// <summary>
///     Одна рабочая копия PDF-плоттера с выключенным автооткрытием файла.
///     Если патч невозможен, <see cref="Name"/> совпадает с исходным устройством.
/// </summary>
internal sealed class SilentPdfDevice : IDisposable
{
    private const string FileName = "AutoBIMFusion.QuickPDF.pc3";

    private readonly string? _tempPath;
    private bool _disposed;

    private SilentPdfDevice(string name, string source, string? tempPath)
    {
        Name = name;
        Source = source;
        _tempPath = tempPath;
    }

    internal string Name { get; }

    internal string Source { get; }

    internal static SilentPdfDevice Open(string sourceDevice)
    {
        string? sourcePath = FindPlotterFile(sourceDevice);
        if (sourcePath is null)
        {
            return new SilentPdfDevice(sourceDevice, sourceDevice, null);
        }

        string contents;
        try
        {
            contents = File.ReadAllText(sourcePath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SilentPdfDevice(sourceDevice, sourceDevice, null);
        }

        if (!PdfPc3Viewer.TryDisable(contents, out string patched))
        {
            return new SilentPdfDevice(sourceDevice, sourceDevice, null);
        }

        foreach (string directory in DestinationDirectories(sourcePath))
        {
            string destPath = Path.Combine(directory, FileName);
            try
            {
                File.WriteAllText(destPath, patched, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return new SilentPdfDevice(FileName, sourceDevice, destPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Каталог плоттеров может быть из Program Files — пробуем следующий.
            }
        }

        return new SilentPdfDevice(sourceDevice, sourceDevice, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_tempPath is null)
        {
            return;
        }

        try
        {
            File.Delete(_tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of the temporary plotter copy.
        }
    }

    private static IEnumerable<string> DestinationDirectories(string sourcePath)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string? sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (!string.IsNullOrEmpty(sourceDirectory) && seen.Add(sourceDirectory))
        {
            yield return sourceDirectory;
        }

        string? roam = Convert.ToString(AcadApp.GetSystemVariable("ROAMABLEROOTPREFIX"), CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(roam))
        {
            yield break;
        }

        string roamPlotters = Path.Combine(roam, "Plotters");
        if (Directory.Exists(roamPlotters) && seen.Add(roamPlotters))
        {
            yield return roamPlotters;
        }
    }

    private static string? FindPlotterFile(string device)
    {
        foreach (string? root in new[]
                 {
                     Convert.ToString(AcadApp.GetSystemVariable("ROAMABLEROOTPREFIX"), CultureInfo.InvariantCulture),
                     Convert.ToString(AcadApp.GetSystemVariable("LOCALROOTPREFIX"), CultureInfo.InvariantCulture)
                 })
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            string path = Path.Combine(root, "Plotters", device);
            if (File.Exists(path))
            {
                return path;
            }
        }

        try
        {
            string found = HostApplicationServices.Current.FindFile(
                device, HostApplicationServices.WorkingDatabase, FindFileHint.Default);
            return File.Exists(found) ? found : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

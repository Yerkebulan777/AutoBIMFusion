using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Transmittal;
using System.IO.Compression;
using System.Runtime.Versioning;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

[SupportedOSPlatform("Windows")]
public sealed class TransmittalCommands
{
    private const string OutputFolderName = "ETransmitOutput";
    private const string TempFolderSuffix = "_TempPack";
    private const string ZipFileSuffix = "_Package.zip";

    [CommandMethod("CreateETransmitZip")]
    public void CreateETransmitZip()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        OperationLogger log = new(doc.Editor);

        if (!doc.IsNamedDrawing || string.IsNullOrWhiteSpace(doc.Name))
        {
            log.Warn("Сначала сохраните текущий чертеж на диск, затем повторите команду CreateETransmitZip.");
            return;
        }

        string dwgPath = doc.Name;
        string dwgNameOnly = Path.GetFileNameWithoutExtension(dwgPath);
        string drawingDirectory = Path.GetDirectoryName(dwgPath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        string destinationRoot = Path.Combine(drawingDirectory, OutputFolderName);
        string tempFolder = Path.Combine(destinationRoot, $"{dwgNameOnly}{TempFolderSuffix}");
        string zipFilePath = Path.Combine(destinationRoot, $"{dwgNameOnly}{ZipFileSuffix}");

        try
        {
            PrepareOutputFolders(destinationRoot, tempFolder, zipFilePath);

            log.Info("Сбор файлов eTransmit...");

            TransmittalOperation tro = new();
            TransmittalInfo ti = tro.getTransmittalInfoInterface();

            ti.destinationRoot = tempFolder;
            ti.preserveSubdirs = 0;
            ti.includeXrefDwg = 1;
            ti.includeImageFile = 1;
            ti.includeFontFile = 1;
            ti.includePlotFile = 1;
            ti.includeDataLinkFile = 1;

            TransmittalFile? transmittalFile;
            AddFileReturnVal addResult = tro.addDrawingFile(dwgPath, out transmittalFile);
            if (addResult != AddFileReturnVal.eFileAdded)
            {
                log.Warn($"Не удалось добавить чертеж в пакет eTransmit. Код: {addResult}");
                return;
            }

            tro.createTransmittalPackage();

            log.Info("Создание ZIP-архива...");
            ZipFile.CreateFromDirectory(tempFolder, zipFilePath, CompressionLevel.Optimal, false);

            log.Info($"Пакет eTransmit сохранен: {zipFilePath}");
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "Ошибка при создании eTransmit-пакета.");
        }
        finally
        {
            TryDeleteTempFolder(tempFolder, log);
        }
    }

    private static void PrepareOutputFolders(string destinationRoot, string tempFolder, string zipFilePath)
    {
        Directory.CreateDirectory(destinationRoot);

        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, true);
        }

        Directory.CreateDirectory(tempFolder);

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }
    }

    private static void TryDeleteTempFolder(string tempFolder, OperationLogger log)
    {
        try
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
        catch (IOException ex)
        {
            log.Warn(ex, $"Не удалось удалить временную папку: {tempFolder}");
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Warn(ex, $"Нет прав на удаление временной папки: {tempFolder}");
        }
    }
}

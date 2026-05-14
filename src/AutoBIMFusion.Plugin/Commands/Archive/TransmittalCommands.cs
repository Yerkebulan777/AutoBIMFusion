using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Plugin.Commands;

[SupportedOSPlatform("Windows")]
public sealed class TransmittalCommands
{
    private const string OutputFolderName = "ETransmitOutput";
    private const string TempFolderSuffix = "_TempPack";
    private const string ZipFileSuffix = "_Package.zip";

    [CommandMethod("CREATE_ETRANSMIT_ZIP", CommandFlags.Modal)]
    public static void CreateETransmitZip()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        Logger log = LoggerFactory.GetSharedLogger();
        log.Information("Запуск команды CreateETransmitZip...");

        if (!doc.IsNamedDrawing || string.IsNullOrWhiteSpace(doc.Name))
        {
            log.Warning("Сначала сохраните текущий чертеж на диск, затем повторите команду CreateETransmitZip.");
            log.Information("Завершение команды CreateETransmitZip.");
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
            FileUtil.PrepareOutputFolders(destinationRoot, tempFolder, zipFilePath);

            log.Information("Сбор файлов eTransmit...");

            if (!TryCreateTransmittalOperation(out object? operation, out string reason, log))
            {
                log.Warning(reason);
                return;
            }

            ArgumentNullException.ThrowIfNull(operation);
            dynamic tro = operation;

            dynamic ti = tro.getTransmittalInfoInterface();
            ConfigureTransmittalInfo(ti, tempFolder, log);

            object addResult = tro.addDrawingFile(dwgPath, out object? transmittalFile);
            string addResultName = Convert.ToString(addResult, CultureInfo.InvariantCulture) ?? string.Empty;

            if (!string.Equals(addResultName, "eFileAdded", StringComparison.OrdinalIgnoreCase))
            {
                log.Warning($"Не удалось добавить чертеж в пакет eTransmit. Код: {addResultName}");
                return;
            }

            tro.createTransmittalPackage();

            log.Information("Создание ZIP-архива...");
            ZipFile.CreateFromDirectory(tempFolder, zipFilePath, CompressionLevel.Optimal, false);

            log.Information($"Пакет eTransmit сохранен: {zipFilePath}");
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "Ошибка при создании eTransmit-пакета.");
        }
        finally
        {
            FileUtil.TryDeleteDirectory(tempFolder, log);
            log.Information("Завершение команды CreateETransmitZip.");
        }
    }

    private static void ConfigureTransmittalInfo(dynamic transmittalInfo, string tempFolder, Logger log)
    {
        // AutoCAD версии могут использовать разные имена для поля назначения
        bool destinationSet = false;
        foreach (string candidateName in new[] { "destinationRoot", "DestinationRoot", "destination_root", "DestFolder", "destFolder" })
        {
            try
            {
                ReflectionHelper.SetMemberValue(transmittalInfo, candidateName, tempFolder, required: true);
                log.Debug($"Поле назначения eTransmit задано через: {candidateName}");
                destinationSet = true;
                break;
            }
            catch (MissingMemberException)
            {
                // Попробуем следующий вариант
            }
        }

        if (!destinationSet)
        {
            log.Warning("Не удалось задать папку назначения eTransmit — ни одно из известных имён полей не найдено. Пакет может быть создан в папке по умолчанию.");
        }

        ReflectionHelper.SetMemberValue(transmittalInfo, "preserveSubdirs", 0);
        ReflectionHelper.SetMemberValue(transmittalInfo, "includeXrefDwg", 1);
        ReflectionHelper.SetMemberValue(transmittalInfo, "includeImageFile", 1);
        ReflectionHelper.SetMemberValue(transmittalInfo, "includeFontFile", 1);
        ReflectionHelper.SetMemberValue(transmittalInfo, "includePlotFile", 1);
        ReflectionHelper.SetMemberValue(transmittalInfo, "includeDataLinkFile", 1);
    }

    private static bool TryCreateTransmittalOperation(out object? operation, out string reason, Logger log)
    {
        operation = null;
        reason = string.Empty;

        TryLoadAssemblyByName("AcETransmitMgd", log);
        TryLoadAssemblyByName("Autodesk.AutoCAD.Transmittal", log);

        string? acadDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(acadDirectory))
        {
            TryLoadAssemblyByPath(Path.Combine(acadDirectory, "AcETransmitMgd.dll"), log);
            TryLoadAssemblyByPath(Path.Combine(acadDirectory, "AcETransmit.dll"), log);
        }

        Type? transmittalOperationType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(ReflectionHelper.SafeGetTypes)
            .FirstOrDefault(type => string.Equals(type.Name, "TransmittalOperation", StringComparison.Ordinal));

        if (transmittalOperationType is null)
        {
            reason = "eTransmit API недоступен в текущем окружении AutoCAD (не найден тип TransmittalOperation).";
            return false;
        }

        object? instance = Activator.CreateInstance(transmittalOperationType);
        if (instance is null)
        {
            reason = "Не удалось создать экземпляр eTransmit-операции.";
            return false;
        }

        operation = instance;
        return true;
    }

    private static void TryLoadAssemblyByName(string assemblyName, Logger log)
    {
        try
        {
            _ = Assembly.Load(assemblyName);
        }
        catch (System.Exception ex)
        {
            log.Debug($"TryLoadAssemblyByName: {assemblyName} — {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryLoadAssemblyByPath(string assemblyPath, Logger log)
    {
        try
        {
            if (File.Exists(assemblyPath))
            {
                _ = Assembly.LoadFrom(assemblyPath);
            }
        }
        catch (System.Exception ex)
        {
            log.Debug($"TryLoadAssemblyByPath: {assemblyPath} — {ex.GetType().Name}: {ex.Message}");
        }
    }
}

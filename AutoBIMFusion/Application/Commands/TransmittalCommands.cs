using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

[SupportedOSPlatform("Windows")]
public sealed class TransmittalCommands
{
    private const string OutputFolderName = "ETransmitOutput";
    private const string TempFolderSuffix = "_TempPack";
    private const string ZipFileSuffix = "_Package.zip";

    [CommandMethod("CreateETransmitZip", CommandFlags.Modal)]
    public static void CreateETransmitZip()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        AILog log = new(doc.Editor);
        log.Info("Запуск команды CreateETransmitZip...");

        if (!doc.IsNamedDrawing || string.IsNullOrWhiteSpace(doc.Name))
        {
            log.Warn("Сначала сохраните текущий чертеж на диск, затем повторите команду CreateETransmitZip.");
            log.Info("Завершение команды CreateETransmitZip.");
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

            if (!TryCreateTransmittalOperation(out object? operation, out string reason))
            {
                log.Warn(reason);
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
                log.Warn($"Не удалось добавить чертеж в пакет eTransmit. Код: {addResultName}");
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
            log.Info("Завершение команды CreateETransmitZip.");
        }
    }

    private static bool TryCreateTransmittalOperation(out object? operation, out string reason)
    {
        operation = null;
        reason = string.Empty;

        TryLoadAssemblyByName("AcETransmitMgd");
        TryLoadAssemblyByName("Autodesk.AutoCAD.Transmittal");

        string? acadDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(acadDirectory))
        {
            TryLoadAssemblyByPath(Path.Combine(acadDirectory, "AcETransmitMgd.dll"));
            TryLoadAssemblyByPath(Path.Combine(acadDirectory, "AcETransmit.dll"));
        }

        Type? transmittalOperationType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(SafeGetTypes)
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

    private static void ConfigureTransmittalInfo(dynamic transmittalInfo, string tempFolder, AILog log)
    {
        // AutoCAD версии могут использовать разные имена для поля назначения
        bool destinationSet = false;
        foreach (string candidateName in new[] { "destinationRoot", "DestinationRoot", "destination_root", "DestFolder", "destFolder" })
        {
            try
            {
                SetMemberValue(transmittalInfo, candidateName, tempFolder, log, required: true);
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
            log.Warn("Не удалось задать папку назначения eTransmit — ни одно из известных имён полей не найдено. Пакет может быть создан в папке по умолчанию.");
        }

        SetMemberValue(transmittalInfo, "preserveSubdirs", 0, log);
        SetMemberValue(transmittalInfo, "includeXrefDwg", 1, log);
        SetMemberValue(transmittalInfo, "includeImageFile", 1, log);
        SetMemberValue(transmittalInfo, "includeFontFile", 1, log);
        SetMemberValue(transmittalInfo, "includePlotFile", 1, log);
        SetMemberValue(transmittalInfo, "includeDataLinkFile", 1, log);
    }

    private static void SetMemberValue(object target, string memberName, object value, AILog log, bool required = false)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

        Type targetType = target.GetType();

        PropertyInfo? property = targetType.GetProperty(memberName, flags);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(target, ConvertMemberValue(value, property.PropertyType));
            return;
        }

        FieldInfo? field = targetType.GetField(memberName, flags);
        if (field is not null)
        {
            field.SetValue(target, ConvertMemberValue(value, field.FieldType));
            return;
        }

        if (required)
        {
            throw new MissingMemberException(targetType.FullName, memberName);
        }

        log.Debug($"Параметр eTransmit недоступен и будет пропущен: {memberName}");
    }

    private static object? ConvertMemberValue(object value, Type targetType)
    {
        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return effectiveType == typeof(bool) && value is int intValue
            ? intValue != 0
            : effectiveType == typeof(int) && value is bool boolValue
            ? boolValue ? 1 : 0
            : effectiveType.IsEnum
            ? Enum.ToObject(effectiveType, value)
            : effectiveType == value.GetType()
            ? value
            : Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
        catch
        {
            return [];
        }
    }

    private static void TryLoadAssemblyByName(string assemblyName)
    {
        try
        {
            _ = Assembly.Load(assemblyName);
        }
        catch
        {
            // Ассембли может отсутствовать для некоторых AutoCAD сборок.
        }
    }

    private static void TryLoadAssemblyByPath(string assemblyPath)
    {
        try
        {
            if (File.Exists(assemblyPath))
            {
                _ = Assembly.LoadFrom(assemblyPath);
            }
        }
        catch
        {
            // Игнорируем: сборка может быть unmanaged или несовместимой для прямой загрузки.
        }
    }

    private static void PrepareOutputFolders(string destinationRoot, string tempFolder, string zipFilePath)
    {
        _ = Directory.CreateDirectory(destinationRoot);

        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, true);
        }

        _ = Directory.CreateDirectory(tempFolder);

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }
    }

    private static void TryDeleteTempFolder(string tempFolder, AILog log)
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

using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.GraphicsInterface;
using System.Runtime.Versioning;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

[SupportedOSPlatform("Windows")]
public sealed class TextStyleCommands
{
    private const double NumericTolerance = 0.0001;

    [CommandMethod("MergeTextStyles", CommandFlags.Modal)]
    public static void MergeTextStyles()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        AILog log = new(doc.Editor);
        log.Info("Запуск команды MergeTextStyles...");
        Database db = doc.Database;

        int updatedObjects = 0;
        int deletedStyles = 0;

        try
        {
            using (doc.LockDocument())
            {
                using Transaction tr = db.TransactionManager.StartTransaction();

                TextStyleTable styleTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                List<TextStyleData> styles = CollectStyles(styleTable, tr);

                List<List<TextStyleData>> duplicateGroups = styles
                    .GroupBy(x => x.Signature)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.ToList())
                    .ToList();

                if (duplicateGroups.Count == 0)
                {
                    log.Info("MergeTextStyles: дубликаты текстовых стилей не найдены.");
                    log.Info("Завершение команды MergeTextStyles.");
                    return;
                }

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                ObjectId currentStyleId = db.Textstyle;

                foreach (List<TextStyleData> group in duplicateGroups)
                {
                    ObjectId masterStyleId = ChooseMasterStyle(group, currentStyleId);
                    HashSet<ObjectId> duplicateIds = group
                        .Select(x => x.StyleId)
                        .Where(id => id != masterStyleId)
                        .ToHashSet();

                    if (duplicateIds.Count == 0)
                    {
                        continue;
                    }

                    updatedObjects += ReassignStyles(bt, tr, masterStyleId, duplicateIds);
                    deletedStyles += DeleteStyles(tr, duplicateIds, log);
                }

                tr.Commit();
                log.Info($"MergeTextStyles: групп дубликатов {duplicateGroups.Count}, обновлено объектов {updatedObjects}, удалено стилей {deletedStyles}.");
            }

            log.Info("Завершение команды MergeTextStyles.");
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "Ошибка выполнения MergeTextStyles.");
        }
    }

    private static List<TextStyleData> CollectStyles(TextStyleTable styleTable, Transaction tr)
    {
        List<TextStyleData> styles = [];

        foreach (ObjectId styleId in styleTable)
        {
            if (tr.GetObject(styleId, OpenMode.ForRead) is not TextStyleTableRecord styleRecord)
            {
                continue;
            }

            if (styleRecord.IsDependent || styleRecord.IsErased)
            {
                continue;
            }

            styles.Add(new TextStyleData(styleId, styleRecord.Name, BuildSignature(styleRecord)));
        }

        return styles;
    }

    private static TextStyleSignature BuildSignature(TextStyleTableRecord styleRecord)
    {
        FontDescriptor font = styleRecord.Font;

        return new TextStyleSignature(
            styleRecord.FileName ?? string.Empty,
            styleRecord.BigFontFileName ?? string.Empty,
            font.TypeFace ?? string.Empty,
            font.Bold,
            font.Italic,
            font.CharacterSet,
            font.PitchAndFamily,
            styleRecord.IsShapeFile,
            styleRecord.IsVertical,
            Normalize(styleRecord.TextSize),
            Normalize(styleRecord.XScale),
            Normalize(styleRecord.ObliquingAngle));
    }

    private static ObjectId ChooseMasterStyle(List<TextStyleData> group, ObjectId currentStyleId)
    {
        TextStyleData? current = group.FirstOrDefault(item => item.StyleId == currentStyleId);
        return current is not null
            ? current.StyleId
            : group
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .First()
            .StyleId;
    }

    private static int ReassignStyles(
        BlockTable blockTable,
        Transaction tr,
        ObjectId masterStyleId,
        HashSet<ObjectId> duplicateStyleIds)
    {
        int updated = 0;
        HashSet<ObjectId> visitedBtr = [];

        foreach (ObjectId btrId in blockTable)
        {
            updated += ReassignStylesInBlock(btrId, tr, masterStyleId, duplicateStyleIds, visitedBtr);
        }

        return updated;
    }

    private static int ReassignStylesInBlock(
        ObjectId btrId,
        Transaction tr,
        ObjectId masterStyleId,
        HashSet<ObjectId> duplicateStyleIds,
        HashSet<ObjectId> visitedBtr)
    {
        if (!visitedBtr.Add(btrId))
        {
            return 0;
        }

        if (tr.GetObject(btrId, OpenMode.ForRead) is not BlockTableRecord btr)
        {
            return 0;
        }

        int updated = 0;

        foreach (ObjectId entId in btr)
        {
            if (tr.GetObject(entId, OpenMode.ForRead, false) is not Entity entity)
            {
                continue;
            }

            if (entity is DBText dbText && duplicateStyleIds.Contains(dbText.TextStyleId))
            {
                dbText.UpgradeOpen();
                dbText.TextStyleId = masterStyleId;
                updated++;
            }
            else if (entity is MText mText && duplicateStyleIds.Contains(mText.TextStyleId))
            {
                mText.UpgradeOpen();
                mText.TextStyleId = masterStyleId;
                updated++;
            }
            else if (entity is AttributeDefinition attrDef && duplicateStyleIds.Contains(attrDef.TextStyleId))
            {
                attrDef.UpgradeOpen();
                attrDef.TextStyleId = masterStyleId;
                updated++;
            }
            else if (entity is BlockReference blockRef)
            {
                updated += ReassignBlockAttributes(blockRef, tr, masterStyleId, duplicateStyleIds);

                // Рекурсивно обходим вложенный блок (в т.ч. анонимные динамические блоки)
                if (!blockRef.BlockTableRecord.IsNull)
                {
                    updated += ReassignStylesInBlock(blockRef.BlockTableRecord, tr, masterStyleId, duplicateStyleIds, visitedBtr);
                }
            }
        }

        return updated;
    }

    private static int ReassignBlockAttributes(
        BlockReference blockRef,
        Transaction tr,
        ObjectId masterStyleId,
        HashSet<ObjectId> duplicateStyleIds)
    {
        int updated = 0;

        foreach (ObjectId attrId in blockRef.AttributeCollection)
        {
            if (tr.GetObject(attrId, OpenMode.ForRead, false) is not AttributeReference attrRef)
            {
                continue;
            }

            if (!duplicateStyleIds.Contains(attrRef.TextStyleId))
            {
                continue;
            }

            attrRef.UpgradeOpen();
            attrRef.TextStyleId = masterStyleId;
            updated++;
        }

        return updated;
    }

    private static int DeleteStyles(Transaction tr, HashSet<ObjectId> duplicateStyleIds, AILog log)
    {
        int deleted = 0;

        foreach (ObjectId styleId in duplicateStyleIds)
        {
            if (tr.GetObject(styleId, OpenMode.ForWrite, false) is not TextStyleTableRecord styleRecord)
            {
                continue;
            }

            try
            {
                styleRecord.Erase(true);
                deleted++;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                log.Warn($"MergeTextStyles: не удалось удалить стиль '{styleRecord.Name}': {ex.Message}");
            }
        }

        return deleted;
    }

    private static double Normalize(double value)
    {
        return Round(value / NumericTolerance) * NumericTolerance;
    }

    private sealed record TextStyleData(ObjectId StyleId, string Name, TextStyleSignature Signature);

    private sealed record TextStyleSignature(
        string FileName,
        string BigFontFileName,
        string Typeface,
        bool Bold,
        bool Italic,
        int CharacterSet,
        int PitchAndFamily,
        bool IsShapeFile,
        bool IsVertical,
        double TextSize,
        double XScale,
        double ObliquingAngle);
}

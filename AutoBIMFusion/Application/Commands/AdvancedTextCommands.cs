using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using System.Runtime.Versioning;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

[SupportedOSPlatform("Windows")]
public sealed class AdvancedTextCommands
{
    private const double WordSpacingFactor = 1.5;
    private const double LineHeightFactor = 0.5;
    private const double HeightTolerance = 0.001;

    [CommandMethod("SMART_MERGE_TEXT")]
    public void SmartMergeModelText()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        OperationLogger log = new(doc.Editor);
        Database db = doc.Database;
        int mergedGroupsCount = 0;

        try
        {
            using Transaction tr = db.TransactionManager.StartTransaction();

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            List<DBText> textsInModel = CollectDbText(modelSpace, tr);

            if (textsInModel.Count == 0)
            {
                log.Info("Текст в Model Space не найден.");
                return;
            }

            List<List<DBText>> groups = SmartGroupText(textsInModel);

            foreach (List<DBText> group in groups.Where(g => g.Count > 1))
            {
                List<DBText> sortedGroup = group.OrderBy(t => t.Position.X).ToList();
                string combinedString = string.Join(" ", sortedGroup.Select(t => t.TextString));

                MText mergedText = new()
                {
                    Contents = combinedString,
                    Location = sortedGroup[0].Position,
                    Layer = sortedGroup[0].Layer,
                    TextStyleId = sortedGroup[0].TextStyleId,
                    TextHeight = sortedGroup[0].Height,
                    Rotation = sortedGroup[0].Rotation,
                    Normal = sortedGroup[0].Normal,
                    Color = sortedGroup[0].Color
                };

                _ = modelSpace.AppendEntity(mergedText);
                tr.AddNewlyCreatedDBObject(mergedText, true);

                foreach (DBText text in sortedGroup)
                {
                    text.Erase();
                }

                mergedGroupsCount++;
            }

            tr.Commit();
            log.Info($"SMART_MERGE_TEXT: собрано групп текста: {mergedGroupsCount}");
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "Ошибка выполнения SMART_MERGE_TEXT.");
        }
    }

    private static List<DBText> CollectDbText(BlockTableRecord modelSpace, Transaction tr)
    {
        List<DBText> result = [];

        foreach (ObjectId id in modelSpace)
        {
            if (id.ObjectClass.DxfName != "TEXT")
            {
                continue;
            }

            if (tr.GetObject(id, OpenMode.ForWrite) is DBText text)
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static List<List<DBText>> SmartGroupText(List<DBText> texts)
    {
        List<List<DBText>> groups = [];
        HashSet<ObjectId> visited = [];

        foreach (DBText startText in texts)
        {
            if (!visited.Add(startText.ObjectId))
            {
                continue;
            }

            List<DBText> currentGroup = [];
            Queue<DBText> queue = [];
            queue.Enqueue(startText);

            while (queue.Count > 0)
            {
                DBText current = queue.Dequeue();
                currentGroup.Add(current);

                foreach (DBText other in texts)
                {
                    if (visited.Contains(other.ObjectId))
                    {
                        continue;
                    }

                    if (current.TextStyleId != other.TextStyleId)
                    {
                        continue;
                    }

                    if (Abs(current.Height - other.Height) > HeightTolerance)
                    {
                        continue;
                    }

                    if (!AreTextsClose(current, other))
                    {
                        continue;
                    }

                    _ = visited.Add(other.ObjectId);
                    queue.Enqueue(other);
                }
            }

            groups.Add(currentGroup);
        }

        return groups;
    }

    private static bool AreTextsClose(DBText current, DBText other)
    {
        double baseHeight = Max(current.Height, other.Height);
        double dy = Abs(current.Position.Y - other.Position.Y);

        if (dy > baseHeight * LineHeightFactor)
        {
            return false;
        }

        (double currentLeft, double currentRight) = GetTextBoundsX(current);
        (double otherLeft, double otherRight) = GetTextBoundsX(other);

        double gap = 0;
        if (currentRight < otherLeft)
        {
            gap = otherLeft - currentRight;
        }
        else if (otherRight < currentLeft)
        {
            gap = currentLeft - otherRight;
        }

        return gap <= baseHeight * WordSpacingFactor;
    }

    private static (double Left, double Right) GetTextBoundsX(DBText text)
    {
        if (!text.Bounds.HasValue)
        {
            double x = text.Position.X;
            return (x, x);
        }

        Extents3d bounds = text.Bounds.Value;
        return (bounds.MinPoint.X, bounds.MaxPoint.X);
    }
}

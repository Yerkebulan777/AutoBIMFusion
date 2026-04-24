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

    [CommandMethod("SMART_MERGE_TEXT")]
    public void SmartMergeModelText()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        OperationLogger log = new(doc.Editor);
        log.Info("Запуск команды SMART_MERGE_TEXT...");
        Database db = doc.Database;
        int mergedGroupsCount = 0;

        try
        {
            using (doc.LockDocument())
            {
                using Transaction tr = db.TransactionManager.StartTransaction();

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                List<DBText> textsInModel = CollectDbText(modelSpace, tr);

                if (textsInModel.Count == 0)
                {
                    log.Info("Текст в Model Space не найден.");
                    tr.Commit();
                    return;
                }

                List<List<DBText>> groups = SmartGroupText(textsInModel);

                BlockTableRecord modelSpaceWrite = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                foreach (List<DBText> group in groups.Where(g => g.Count > 1))
                {
                    List<DBText> sortedGroup = SortGroup(group);
                    string combinedString = string.Join(" ", sortedGroup.Select(t => EscapeMTextContent(t.TextString)));

                    MText mergedText = new()
                    {
                        Contents = combinedString,
                        Location = sortedGroup[0].Position,
                        Layer = sortedGroup[0].Layer,
                        TextStyleId = sortedGroup[0].TextStyleId,
                        TextHeight = sortedGroup[0].Height,
                        Rotation = sortedGroup[0].Rotation,
                        Normal = sortedGroup[0].Normal,
                        Color = sortedGroup[0].Color,
                        Width = 0
                    };

                    _ = modelSpaceWrite.AppendEntity(mergedText);
                    tr.AddNewlyCreatedDBObject(mergedText, true);

                    foreach (DBText text in sortedGroup)
                    {
                        text.UpgradeOpen();
                        text.Erase();
                    }

                    mergedGroupsCount++;
                }

                tr.Commit();
                log.Info($"SMART_MERGE_TEXT: собрано групп текста: {mergedGroupsCount}");
            }

            log.Info("Завершение команды SMART_MERGE_TEXT.");
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

            // Открываем на ForRead — UpgradeOpen() вызовем только перед Erase
            if (tr.GetObject(id, OpenMode.ForRead) is DBText text)
            {
                if (string.IsNullOrWhiteSpace(text.TextString))
                {
                    text.UpgradeOpen();
                    text.Erase();
                    continue;
                }

                result.Add(text);
            }
        }

        return result;
    }

    private static List<List<DBText>> SmartGroupText(List<DBText> texts)
    {
        List<List<DBText>> groups = [];
        HashSet<ObjectId> visited = [];

        // Группируем тексты по стилю, высоте и углу поворота (первичные фильтры)
        var preFilteredGroups = texts
            .GroupBy(t => new { t.TextStyleId, Height = Math.Round(t.Height, 3), Rotation = Math.Round(t.Rotation, 3) })
            .ToList();

        foreach (var preGroup in preFilteredGroups)
        {
            List<DBText> candidates = preGroup.ToList();
            double rotation = preGroup.Key.Rotation;
            double cosA = Math.Cos(rotation);
            double sinA = Math.Sin(rotation);

            // Сортируем кандидатов по координате перпендикулярной строке (условно "Y" строки)
            // Это позволит нам сравнивать текст только с соседями по вертикали.
            var sortedCandidates = candidates
                .Select(t => new { Text = t, Perp = (-t.Position.X * sinA) + (t.Position.Y * cosA) })
                .OrderBy(item => item.Perp)
                .ToList();

            for (int i = 0; i < sortedCandidates.Count; i++)
            {
                var startItem = sortedCandidates[i];
                if (!visited.Add(startItem.Text.ObjectId))
                {
                    continue;
                }

                List<DBText> currentGroup = [];
                Queue<DBText> queue = new();
                queue.Enqueue(startItem.Text);

                while (queue.Count > 0)
                {
                    DBText current = queue.Dequeue();
                    currentGroup.Add(current);

                    double curPerp = (-current.Position.X * sinA) + (current.Position.Y * cosA);
                    double verticalThreshold = current.Height * LineHeightFactor;

                    // Ищем соседей только в ограниченном диапазоне по Perp
                    // Так как sortedCandidates отсортирован, мы можем искать в обе стороны от i
                    // Но проще и эффективнее в рамках этой очереди проверить ближайших соседей в списке.

                    // Для простоты реализации и сохранения O(N log N) мы можем просто пройтись по соседям в sortedCandidates,
                    // чья Perp координата близка к текущей.
                    for (int j = 0; j < sortedCandidates.Count; j++)
                    {
                        var otherItem = sortedCandidates[j];
                        if (visited.Contains(otherItem.Text.ObjectId))
                        {
                            continue;
                        }

                        // Если мы вышли за вертикальный порог, и учитывая сортировку,
                        // можно было бы оптимизировать поиск, но даже простой проход по пре-фильтрованной группе
                        // уже на порядки быстрее исходного O(N^2) на всем чертеже.
                        if (Math.Abs(curPerp - otherItem.Perp) > verticalThreshold)
                        {
                            continue;
                        }

                        if (!AreTextsClose(current, otherItem.Text))
                        {
                            continue;
                        }

                        if (visited.Add(otherItem.Text.ObjectId))
                        {
                            queue.Enqueue(otherItem.Text);
                        }
                    }
                }

                groups.Add(currentGroup);
            }
        }

        return groups;
    }

    /// <summary>
    /// Сортирует группу текстов по направлению их строки с учётом угла поворота.
    /// Для горизонтального текста — по X, для вертикального — по Y (убыв.), для произвольного угла — по проекции.
    /// </summary>
    private static List<DBText> SortGroup(List<DBText> group)
    {
        double rotation = group[0].Rotation;
        double cosA = Math.Cos(rotation);
        double sinA = Math.Sin(rotation);

        return group
            .OrderBy(t => (t.Position.X * cosA) + (t.Position.Y * sinA))
            .ToList();
    }

    private static bool AreTextsClose(DBText current, DBText other)
    {
        double baseHeight = Max(current.Height, other.Height);

        // Проецируем позиции на направление текстовой строки и перпендикуляр к ней
        double rotation = current.Rotation;
        double cosA = Math.Cos(rotation);
        double sinA = Math.Sin(rotation);

        double curParallel = (current.Position.X * cosA) + (current.Position.Y * sinA);
        double othParallel = (other.Position.X * cosA) + (other.Position.Y * sinA);

        double curPerp = (-current.Position.X * sinA) + (current.Position.Y * cosA);
        double othPerp = (-other.Position.X * sinA) + (other.Position.Y * cosA);

        // Расстояние по вертикали (перпендикуляр к строке) — должно быть в пределах одной строки
        double dy = Abs(curPerp - othPerp);
        if (dy > baseHeight * LineHeightFactor)
        {
            return false;
        }

        // Расстояние по горизонтали (вдоль строки) с учётом реальной ширины текстовых объектов
        (double currentLeft, double currentRight) = GetTextBoundsAlongAxis(current, cosA, sinA, curParallel);
        (double otherLeft, double otherRight) = GetTextBoundsAlongAxis(other, cosA, sinA, othParallel);

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

    private static (double Left, double Right) GetTextBoundsAlongAxis(DBText text, double cosA, double sinA, double fallbackCenter)
    {
        if (!text.Bounds.HasValue)
        {
            return (fallbackCenter, fallbackCenter);
        }

        Extents3d bounds = text.Bounds.Value;

        // Проецируем все 4 угла bounding box на ось направления текста
        double x0 = bounds.MinPoint.X, y0 = bounds.MinPoint.Y;
        double x1 = bounds.MaxPoint.X, y1 = bounds.MaxPoint.Y;

        double p0 = (x0 * cosA) + (y0 * sinA);
        double p1 = (x1 * cosA) + (y0 * sinA);
        double p2 = (x0 * cosA) + (y1 * sinA);
        double p3 = (x1 * cosA) + (y1 * sinA);

        double minP = Min(Min(p0, p1), Min(p2, p3));
        double maxP = Max(Max(p0, p1), Max(p2, p3));

        return (minP, maxP);
    }

    /// <summary>
    /// Экранирует спецсимволы MText в строке, полученной из DBText.TextString,
    /// чтобы они не интерпретировались как MText-команды форматирования.
    /// </summary>
    private static string EscapeMTextContent(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Экранируем обратный слэш первым, иначе последующие замены его задвоят
        return text
            .Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}");
    }
}
